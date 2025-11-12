using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using ValheimCrossPlatformLauncher;
using ValheimLauncher2.Models.Download;
using ValheimLauncher2.Models.Settings;
using ValheimLauncher2.Models.Utils;

namespace ValheimLauncher2.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly Window _parentWindow;
        private readonly ClientDownloader _clientDownloader;
        private readonly ModDownloader _modDownloader;

        private LauncherSettings currentSettings;
        private const string SettingsFileName = "launcher_settings.json";
        public string settingsFilePath;

        [ObservableProperty]
        private string _statusText = "Bereit.";

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private string _downloadSpeedText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsStartEnabled))]
        [NotifyPropertyChangedFor(nameof(IsInstallGameVisible))]
        [NotifyPropertyChangedFor(nameof(IsFixValheimEnabled))]
        [NotifyPropertyChangedFor(nameof(IsMPDownloadEnabled))]
        [NotifyPropertyChangedFor(nameof(IsResetVisible))]
        private bool _isGameInstalled;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsStartEnabled))]
        [NotifyPropertyChangedFor(nameof(IsInstallGameVisible))]
        [NotifyPropertyChangedFor(nameof(IsFixValheimEnabled))]
        [NotifyPropertyChangedFor(nameof(IsMPDownloadEnabled))]
        [NotifyPropertyChangedFor(nameof(IsResetVisible))]
        private bool _isBusy;

        public bool IsStartEnabled => IsGameInstalled && !IsBusy;
        public bool IsInstallGameVisible => !IsGameInstalled && !IsBusy;
        public bool IsFixValheimEnabled => IsGameInstalled && !IsBusy;
        public bool IsMPDownloadEnabled => IsGameInstalled && !IsBusy;
        public bool IsResetVisible => IsGameInstalled && !IsBusy;



        [ObservableProperty]
        private string _localModpackVersion = "v. -";

        [ObservableProperty]
        private string _onlineModpackVersion = "v. -";

        [ObservableProperty]
        private string _installPathText = "-";

        [ObservableProperty]
        private bool _vulkanEnabled;

        partial void OnVulkanEnabledChanged(bool value)
        {
            SaveSettings();
        }

        public MainViewModel(Window parent)
        {
            _parentWindow = parent;
            string launcherFolderPath = PlatformUtils.GetAppConfigFolderPath();

            if (!Directory.Exists(launcherFolderPath))
            {
                Directory.CreateDirectory(launcherFolderPath);
            }
            settingsFilePath = Path.Combine(launcherFolderPath, SettingsFileName);

            LoadSettings(); 

            _clientDownloader = new ClientDownloader(
            status => StatusText = status,
            progress => ProgressValue = progress,
            speed => DownloadSpeedText = speed
            );

            _modDownloader = new ModDownloader(
            status => StatusText = status,
            progress =>
            {
                if (double.TryParse(progress, NumberStyles.Any, CultureInfo.InvariantCulture, out double p))
                {
                    ProgressValue = p;
                }
            },
            speed => DownloadSpeedText = speed,
            currentSettings,
            () => SaveSettings(),
            async errorMsg => await MessageBox.Show(_parentWindow, errorMsg)
            );

            Checkstatus();

            _ = CheckAndUpdateModpackAsync();
        }

        /// <summary>
        /// Loads launcher settings from disk or creates default settings if none exist.
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    currentSettings = JsonConvert.DeserializeObject<LauncherSettings>(json) ?? new LauncherSettings();
                }
                else
                {
                    currentSettings = new LauncherSettings();
                }
            }
            catch (Exception ex)
            {
                StatusText = "Fehler beim Laden der Einstellungen.";
                Debug.WriteLine($"Error loading settings: {ex.Message}");
                currentSettings = new LauncherSettings();
            }

            VulkanEnabled = currentSettings.VulkanEnabled;
            LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "-");
            InstallPathText = currentSettings.ValheimInstallPath;
        }

        /// <summary>
        /// Saves the current launcher settings to disk.
        /// </summary>
        private void SaveSettings()
        {
            currentSettings.VulkanEnabled = VulkanEnabled;
            try
            {
                using (var writer = new StreamWriter(settingsFilePath, false))
                {
                    string json = JsonConvert.SerializeObject(currentSettings, Formatting.Indented);
                    writer.Write(json);
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                StatusText = "Fehler beim Speichern der Einstellungen.";
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the game is installed by verifying required files and directories.
        /// </summary>
        private void Checkstatus()
        {

            string bootConfigPath = "";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {

                bootConfigPath = Path.Combine(currentSettings.ValheimInstallPath, "Valheim.app", "Contents", "Resources", "Data", "boot.config");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {

                bootConfigPath = Path.Combine(currentSettings.ValheimInstallPath, "Valheim_Data", "boot.config");
            }
            else
            {

                bootConfigPath = Path.Combine(currentSettings.ValheimInstallPath, "valheim_data", "boot.config");
            }
            string patcherPath = Path.Combine(currentSettings.ValheimInstallPath, "BepInEx", "patchers");

            IsGameInstalled = Directory.Exists(patcherPath) && File.Exists(bootConfigPath);
            InstallPathText = currentSettings.ValheimInstallPath;
        }

        /// <summary>
        /// Starts the Valheim game with the appropriate launch arguments for the current platform.
        /// </summary>
        [RelayCommand]
        private async Task StartGame()
        {
            IsBusy = true;
            StatusText = "Starte das Spiel...";
            string launchArgs = currentSettings.VulkanEnabled ? "-force-vulkan -window-mode exclusive" : "-force-d3d11 -window-mode exclusive";

            await Task.Run(() =>
            {
                if (Process.GetProcessesByName("steam").Length == 0)
                {
                    StatusText = "Steam wird gestartet...";
                    if (!PlatformUtils.TryStartSteam())
                    {
                        
                        return;
                    }
                    System.Threading.Thread.Sleep(5000);
                }


                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                        string exePath = Path.Combine(currentSettings.ValheimInstallPath, "valheim.exe");
                        Process.Start(new ProcessStartInfo(exePath) { Arguments = launchArgs, UseShellExecute = true });

                }

                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    string installPath = currentSettings.ValheimInstallPath;
                    string scriptPath = Path.Combine(installPath, "start_game_bepinex.sh");
                    string binaryPath = Path.Combine(installPath, "Valheim.x86_64");


                    if (!File.Exists(scriptPath))
                    {

                        StatusText = "Fehler: start_game_bepinex.sh nicht gefunden!";
                        IsBusy = false;
                        return;
                    }
                    if (!File.Exists(binaryPath))
                    {

                        StatusText = "Fehler: Valheim.x86_64 nicht gefunden!";
                        IsBusy = false;
                        return;
                    }

                    EnsureExecutable(scriptPath);
                    EnsureExecutable(binaryPath);

                    try
                    {

                        string command = $"\"{scriptPath}\""; 


                        var terminalCandidates = new List<(string Name, string Arguments)>
            {
 ("ptyxis", $"-e /bin/bash -c \"{command}\""),
 ("gnome-terminal", $"-- /bin/bash -c \"{command}\""),
 ("konsole", $"-e /bin/bash -c \"{command}\""),
 ("xfce4-terminal", $"-e /bin/bash -c \"{command}\""),
 ("xterm", $"-e /bin/bash -c \"{command}\""),
 ("flatpak", $"run org.gnome.Ptyxis -e /bin/bash -c \"{command}\"")
            };


                        string terminal = null;
                        string terminalArgs = null;

                        foreach (var candidate in terminalCandidates)
                        {
                            try
                            {

                                var checkProcess = new ProcessStartInfo
                                {
                                    FileName = "which",
                                    Arguments = candidate.Name.Split(' ')[0], 
                                    RedirectStandardOutput = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };

                                using (var process = Process.Start(checkProcess))
                                {
                                    string output = process.StandardOutput.ReadToEnd();
                                    process.WaitForExit();

                                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                                    {
                                        terminal = candidate.Name;
                                        terminalArgs = candidate.Arguments;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        if (terminal == null)
                        {
                           
                            StatusText = "Fehler: Kein Terminal-Emulator (ptyxis, gnome-terminal, konsole, xterm etc.) gefunden!";
                            IsBusy = false;
                            return;
                        }

                      
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = terminal,
                            Arguments = terminalArgs,
                            WorkingDirectory = installPath,
                            UseShellExecute = false
                        };

                        try
                        {
                            Process.Start(processInfo);
                        }
                        catch (Exception ex)
                        {
                           
                            StatusText = $"Fehler beim Starten des Spiels: {ex.Message}";
                            IsBusy = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        
                        StatusText = $"Fehler beim Starten des Spiels: {ex.Message}";
                        IsBusy = false;
                    }
                }
                else
                {
                    
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
         {
             StatusText = "Fehler: start_game_bepinex.sh nicht gefunden!";
             IsBusy = false;
         });
                    return;
                }
            });

            if (!StatusText.StartsWith("Fehler:"))
            {
                _parentWindow?.Close();
            }
        }


        /// <summary>
        /// Sets execute permissions for a file on Linux or macOS.
        /// </summary>
        private void EnsureExecutable(string filePath)
        {
            // This method is only called if we already know we're on Linux/macOS.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return;
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        // Use "u+x" for more precise permission setting
                        Arguments = $"u+x \"{filePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                Debug.WriteLine($"Set execute permission (u+x) on {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not set execute permission on {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Installs the game to a user-selected directory and sets up required files.
        /// </summary>
        [RelayCommand]
        private async Task InstallGame()
        {
            IsBusy = true;

            var message = "Wählen Sie im Folgenden das Zielverzeichnis, in dem der Ordner 'VImmerndar' erstellt werden soll.";
            var result = await ConfirmDialog.Show(_parentWindow, "Installationshinweis", message);

            if (result == ConfirmDialog.DialogResult.Yes)
            {
             
                string? initialDirectory = PlatformUtils.GetDefaultSystemPath();

                var dialog = new OpenFolderDialog
                {
                    Title = "Wähle einen neuen Installationsordner",
                    Directory = initialDirectory
                };
                var newPath = await dialog.ShowAsync(_parentWindow);

                if (string.IsNullOrEmpty(newPath))
                {
                    StatusText = "Installation abgebrochen.";
                    IsBusy = false;
                    return;
                }

                IsBusy = true;
                try
                {

                    currentSettings.ValheimInstallPath = Path.Combine(newPath, "VImmerndar");
                    InstallPathText = Path.Combine(newPath, "VImmerndar");

                    IsGameInstalled = false;

                    if (!Directory.Exists(currentSettings.ValheimInstallPath))
                    {
                        Directory.CreateDirectory(currentSettings.ValheimInstallPath);
                    }

                    await _clientDownloader.InstallGameAsync(currentSettings.ValheimInstallPath);
                    PlatformUtils.ModifyBootConfig(currentSettings.ValheimInstallPath);
                    (string? onlineVersion, bool needsUpdate) = await _modDownloader.CheckForUpdatesAsync();
                    if (onlineVersion != null)
                    {
                        OnlineModpackVersion = "v. " + onlineVersion;
                    }
                    await _modDownloader.ForceUpdateModpackAsync();
                    LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "-");
                    StatusText = "Installation abgeschlossen!";
                    SaveSettings();
                    Checkstatus();
                }
                finally
                {
                    
                    IsBusy = false;
                }
            }
            else
            {
                    IsBusy = false;
                    return;
            }
        }

        /// <summary>
        /// Repairs the Valheim installation and resets the modpack version.
        /// </summary>
        [RelayCommand]
        private async Task FixValheim()
        {
            currentSettings.Modpack.CurrentLocalVersion = "0.0.1";
            SaveSettings();

            LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "-");

            IsBusy = true;
            try
            {
                IsGameInstalled = false;
                await _clientDownloader.FixValheimAsync(currentSettings.ValheimInstallPath);
                (string? onlineVersion, bool needsUpdate) = await _modDownloader.CheckForUpdatesAsync();
                if (onlineVersion != null)
                {
                    OnlineModpackVersion = "v. " + onlineVersion;

                }
                await _modDownloader.ForceUpdateModpackAsync();
                LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "-");
                Checkstatus();
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Forces a manual update of the modpack.
        /// </summary>
        [RelayCommand]
        private async Task ManualModUpdate()
        {
            currentSettings.Modpack.CurrentLocalVersion = "0.0.1";
            SaveSettings();

            LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "-");

            IsBusy = true;
            try
            {
                (string? onlineVersion, bool needsUpdate) = await _modDownloader.CheckForUpdatesAsync();
                if (onlineVersion != null)
                {
                    OnlineModpackVersion = "v. " + onlineVersion;
                }

                await _modDownloader.ForceUpdateModpackAsync();
                LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "-");
                Checkstatus();
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Checks for modpack updates and applies them if needed.
        /// </summary>
        private async Task CheckAndUpdateModpackAsync()
        {
            if (!IsGameInstalled) return;

            (string? onlineVersion, bool needsUpdate) = await _modDownloader.CheckForUpdatesAsync();
            if (onlineVersion != null)
            {
                OnlineModpackVersion = "v. " + onlineVersion;
            }

            if (needsUpdate)
            {
               await ManualModUpdate();
            }
        }

        /// <summary>
        /// Allows the user to change the installation path and moves game data if necessary.
        /// </summary>
        [RelayCommand]
        private async Task ChangeInstallPath()
        {
            IsBusy = true;
            var confirmResult = await ConfirmDialog.Show(_parentWindow, "Installation verschieben?", "Möchten Sie Ihre bestehende Installation an einen neuen Ort verschieben?");
            if (confirmResult == ConfirmDialog.DialogResult.No)
            {
                IsBusy = false;
                return;
            }

            string? initialDirectory = PlatformUtils.GetDefaultSystemPath();

            var dialog = new OpenFolderDialog
            {
                Title = "Wähle einen neuen Installationsordner",
                Directory = initialDirectory
            };

            string oldPath = currentSettings.ValheimInstallPath;
            var newPath = await dialog.ShowAsync(_parentWindow);

            if (string.IsNullOrEmpty(newPath))
            {
                IsBusy = false;
                return;
            }

            string newInstallPath = Path.Combine(newPath, "VImmerndar");
            if (string.Equals(oldPath, newInstallPath, StringComparison.OrdinalIgnoreCase))
            {
                IsBusy = false;
                return;
            }


            currentSettings.ValheimInstallPath = Path.Combine(newPath, "VImmerndar");
            InstallPathText = Path.Combine(newPath, "VImmerndar");

            if (Directory.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {

                await MoveGameDataAsync(oldPath, currentSettings.ValheimInstallPath);

            }

            currentSettings.ValheimInstallPath = currentSettings.ValheimInstallPath;


            IsBusy = false;
            SaveSettings();
            Checkstatus();

        }

        /// <summary>
        /// Moves all game data from the source directory to the destination directory.
        /// </summary>
        private async Task MoveGameDataAsync(string sourceDir, string destinationDir)
        {
            StatusText = "Verschiebe Spieldaten...";

            await Task.Run(() =>
            {
                try
                {

                    if (Directory.Exists(destinationDir))
                    {
                        Directory.Delete(destinationDir, true);
                    }
                    Directory.CreateDirectory(destinationDir);


                    StatusText = "Kopiere Dateien...";


                    foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                    {
                        Directory.CreateDirectory(dir.Replace(sourceDir, destinationDir));
                    }


                    foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                    {
                        File.Copy(file, file.Replace(sourceDir, destinationDir), true);
                    }

                        StatusText = "Räume alte Daten auf...";
                        Directory.Delete(sourceDir, true);

                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusText = $"Fehler beim Verschieben: {ex.Message}";
                    });
                    Debug.WriteLine($"Error in MoveGameDataAsync: {ex}");
                }
            });

            StatusText = "Verschieben abgeschlossen!";
            Checkstatus();
            IsBusy = false;
        }

        /// <summary>
        /// Opens the current installation path in the system's file explorer.
        /// </summary>
        [RelayCommand]
        private void OpenInstallPath()
        {
            if (Directory.Exists(currentSettings.ValheimInstallPath))
            {
                PlatformUtils.OpenUrl(currentSettings.ValheimInstallPath);
            }
            else
            {
                StatusText = "Pfad existiert nicht.";
            }
        }

        /// <summary>
        /// Closes the main application window.
        /// </summary>
        [RelayCommand]
        private void CloseApplication()
        {
            _parentWindow?.Close();
        }
    }
}