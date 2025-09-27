using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;
using ValheimCrossPlatformLauncher;
using ValheimLauncher2.Models.Download;
using ValheimLauncher2.Models.PerformanceGame;
using ValheimLauncher2.Models.Settings;
using ValheimLauncher2.Models.Utils;
// Du musst eventuell den Namespace für PlatformUtils anpassen, falls er in einem anderen Ordner liegt
// using ValheimLauncher2.Models.Utils; 

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
        private string _localModpackVersion = "v. Unbekannt";

        [ObservableProperty]
        private string _onlineModpackVersion = "v. Unbekannt";

        [ObservableProperty]
        private string _installPathText = "Nicht festgelegt";

        [ObservableProperty]
        private bool _vulkanEnabled;

        public MainViewModel(Window parent)
        {
            _parentWindow = parent;
            string appDataPath = PlatformUtils.GetAppConfigFolderPath();
            string launcherFolderPath = Path.Combine(appDataPath, "ValheimImmerndar");

            if (!Directory.Exists(launcherFolderPath))
            {
                Directory.CreateDirectory(launcherFolderPath);
            }
            settingsFilePath = Path.Combine(launcherFolderPath, SettingsFileName);

            LoadSettings(); // Load settings before initializing downloaders that depend on them

            _clientDownloader = new ClientDownloader(
                status => StatusText = status,
                progress => ProgressValue = progress
            );

            _modDownloader = new ModDownloader(
                status => StatusText = status,
                progress => {
                    if (double.TryParse(progress, NumberStyles.Any, CultureInfo.InvariantCulture, out double p))
                    {
                        ProgressValue = p;
                    }
                },
                currentSettings,
                () => SaveSettings()
            );

            Checkstatus();

            _ = CheckAndUpdateModpackAsync();
        }

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
            LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "nie installiert");
            InstallPathText = currentSettings.ValheimInstallPath;
        }

        private void SaveSettings()
        {
            try
            {
                currentSettings.VulkanEnabled = this.VulkanEnabled;
                string json = JsonConvert.SerializeObject(currentSettings, Formatting.Indented);
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                StatusText = "Fehler beim Speichern der Einstellungen.";
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private void Checkstatus()
        {

            string bootConfigPath = "";
            // Prüfe, ob der Code auf einem macOS-System läuft
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS-spezifischer Pfad
                bootConfigPath = Path.Combine(currentSettings.ValheimInstallPath, "Valheim.app", "Contents", "Resources", "Data", "boot.config");
            }
            else
            {
                // Der gemeinsame Pfad für Windows und Linux
                bootConfigPath = Path.Combine(currentSettings.ValheimInstallPath, "valheim_data", "boot.config");
            }
            string patcherPath = Path.Combine(currentSettings.ValheimInstallPath, "BepInEx", "patchers");

            IsGameInstalled = Directory.Exists(patcherPath) && File.Exists(bootConfigPath);
            InstallPathText = currentSettings.ValheimInstallPath;
        }

        [RelayCommand]
        private async Task StartGame()
        {
            IsBusy = true;
            StatusText = "Starte das Spiel...";

            string launchArgs = VulkanEnabled ? "-force-vulkan -window-mode exclusive" : "-force-d3d11 -window-mode exclusive";

            await Task.Run(() =>
            {
                // Überprüfen, ob Steam läuft, und bei Bedarf starten.
                if (Process.GetProcessesByName("steam").Length == 0)
                {
                    StatusText = "Steam wird gestartet...";

                    // HIER IST DIE ÄNDERUNG: Wir rufen die neue, saubere Methode auf.
                    if (!PlatformUtils.TryStartSteam())
                    {
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusText = "Fehler: Steam konnte nicht gestartet werden!";
                            IsBusy = false;
                        });
                        return; // Beende den Task, wenn Steam nicht gestartet werden kann
                    }

                    // Gib Steam einen Moment zum Starten
                    System.Threading.Thread.Sleep(5000);
                }

                // --- HIER BEGINNT DIE PLATTFORM-UNTERSCHEIDUNG ---

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows-Logik (unverändert)
                    if (currentSettings.ValheimInstallPath.Contains("steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        PlatformUtils.OpenUrl($"steam://run/892970//{Uri.EscapeDataString(launchArgs)}/");
                    }
                    else
                    {
                        string exePath = Path.Combine(currentSettings.ValheimInstallPath, "valheim.exe");
                        Process.Start(new ProcessStartInfo(exePath) { Arguments = launchArgs, UseShellExecute = true });
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Linux Logik
                    string scriptPath = Path.Combine(currentSettings.ValheimInstallPath, "start_game_bepinex.sh");
                    if (File.Exists(scriptPath))
                    {
                        EnsureExecutable(scriptPath); // Stellt 'chmod +x' sicher
                        var processInfo = new ProcessStartInfo(scriptPath)
                        {
                            Arguments = launchArgs,
                            WorkingDirectory = currentSettings.ValheimInstallPath,
                            UseShellExecute = true
                        };
                        Process.Start(processInfo);
                    }
                    else
                    {
                        // Fehlerbehandlung für Linux
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusText = "Fehler: start_game_bepinex.sh nicht gefunden!";
                            IsBusy = false;
                        });
                        return;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // macOS Logik
                    // BepInEx legt das Start-Skript normalerweise in den Hauptordner, neben das .app-Paket.
                    string scriptPath = Path.Combine(currentSettings.ValheimInstallPath, "start_game_bepinex.sh");
                    if (File.Exists(scriptPath))
                    {
                        EnsureExecutable(scriptPath); // Stellt 'chmod +x' sicher
                        var processInfo = new ProcessStartInfo(scriptPath)
                        {
                            Arguments = launchArgs,
                            WorkingDirectory = currentSettings.ValheimInstallPath,
                            UseShellExecute = true
                        };
                        Process.Start(processInfo);
                    }
                    else
                    {
                        // Fehlerbehandlung für macOS
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusText = "Fehler: start_game_bepinex.sh nicht gefunden!";
                            IsBusy = false;
                        });
                        return;
                    }
                }
            });

            if (!StatusText.StartsWith("Fehler:"))
            {
                _parentWindow?.Close();
            }
        }

        // NEUE HILFSMETHODE: Setzt Ausführungsrechte auf Linux/macOS
        private void EnsureExecutable(string filePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{filePath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();
                    Debug.WriteLine($"Set execute permission on {filePath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not set execute permission on {filePath}: {ex.Message}");
                    // Hier könnte man den Nutzer informieren, dass er es manuell tun muss.
                    // Für den Moment loggen wir es nur.
                }
            }
        }

        [RelayCommand]
        private async Task InstallGame()
        {

            var message = "Für eine reibungslose Installation empfehlen wir einen einfachen Installationspfad, " +
                         "zum Beispiel \"C:\". Dabei wird ein eigener Ordner 'VImmerndar' erstellt. \n\n" + 
                         "Notiz: Der Steam Ordner wird wegen automatischen Client Updates nicht empfohlen.";
            var result = await ConfirmDialog.Show(_parentWindow, "Installationshinweis", message);

            if (result == ConfirmDialog.DialogResult.Yes)
            {
                // HIER IST DIE ÄNDERUNG: Wir rufen die neue Methode für den Startordner auf
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
                return;
            }

                // Erst HIER, wenn die eigentliche Arbeit beginnt, setzen wir IsBusy
                IsBusy = true;
                try
                {
                    if (newPath.Contains("steamapps\\common\\Valheim", StringComparison.OrdinalIgnoreCase))
                    {
                        currentSettings.ValheimInstallPath = newPath;
                        InstallPathText = newPath;

                    }
                    else if (newPath.Contains("steamapps\\common", StringComparison.OrdinalIgnoreCase))
                    {
                        currentSettings.ValheimInstallPath = Path.Combine(newPath, "Valheim");
                        InstallPathText = Path.Combine(newPath, "Valheim");

                    }
                    else
                    {
                        currentSettings.ValheimInstallPath = Path.Combine(newPath, "VImmerndar");
                        InstallPathText = Path.Combine(newPath, "VImmerndar");

                    }

                IsGameInstalled = false;

            if(!Directory.Exists(currentSettings.ValheimInstallPath))
            {
                Directory.CreateDirectory(currentSettings.ValheimInstallPath);
            }

            await _clientDownloader.InstallGameAsync(currentSettings.ValheimInstallPath);
            PlatformUtils.ModifyBootConfig(currentSettings.ValheimInstallPath);
            await _modDownloader.ForceUpdateModpackAsync();

                    StatusText = "Installation abgeschlossen!";
                   SaveSettings();
                   Checkstatus();
            }
    finally
    {
                // Dieser Block wird IMMER ausgeführt, egal ob es einen Fehler gab oder nicht.
                IsBusy = false;
            }
        }
        }

        [RelayCommand]
        private async Task FixValheim()
        {
            IsBusy = true;
            try
            {
                    IsGameInstalled = false;
                await _clientDownloader.FixValheimAsync(currentSettings.ValheimInstallPath);
                await _modDownloader.ForceUpdateModpackAsync();

                Checkstatus();
                    }
                finally
                {
                    IsBusy = false;
                }
            }

        [RelayCommand]
        private async Task ManualModUpdate()
        {
            IsBusy = true;
            try
            {
                await _modDownloader.ForceUpdateModpackAsync();
                LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "unbekannt");
                Checkstatus();
            }
            finally
            {
                IsBusy = false;
            }
        }

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
                IsGameInstalled = false; // Disable buttons during auto-update
                await _modDownloader.ForceUpdateModpackAsync();
                LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "unbekannt");
                Checkstatus(); // Re-enable buttons
            }
        }

        [RelayCommand]
        private async Task ChangeInstallPath()
        {
                var confirmResult = await ConfirmDialog.Show(_parentWindow, "Installation verschieben?", "Möchten Sie Ihre bestehende Installation an einen neuen Ort verschieben?");
                if (confirmResult == ConfirmDialog.DialogResult.No) return;

                // HIER IST DIE ÄNDERUNG: Wir rufen die neue Methode für den Startordner auf
                string? initialDirectory = PlatformUtils.GetDefaultSystemPath();

                var dialog = new OpenFolderDialog
                {
                    Title = "Wähle einen neuen Installationsordner",
                    Directory = initialDirectory
                };
                string oldPath = currentSettings.ValheimInstallPath;
                var newPath = await dialog.ShowAsync(_parentWindow);

                if (string.IsNullOrEmpty(newPath)) return;


                if (newPath.Contains("steamapps\\common\\Valheim", StringComparison.OrdinalIgnoreCase))
                {
                    currentSettings.ValheimInstallPath = newPath;
                    InstallPathText = newPath;

                }
                else if (newPath.Contains("steamapps\\common", StringComparison.OrdinalIgnoreCase))
                {
                    currentSettings.ValheimInstallPath = Path.Combine(newPath, "Valheim");
                    InstallPathText = Path.Combine(newPath, "Valheim");

                }
                else
                {
                    currentSettings.ValheimInstallPath = Path.Combine(newPath, "VImmerndar");
                    InstallPathText = Path.Combine(newPath, "VImmerndar");

                }

            if (Directory.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {

                    await MoveGameDataAsync(oldPath, currentSettings.ValheimInstallPath);

            }

            currentSettings.ValheimInstallPath = currentSettings.ValheimInstallPath;



            SaveSettings();
            Checkstatus();
            
        }

        private async Task MoveGameDataAsync(string sourceDir, string destinationDir)
        {
            StatusText = "Verschiebe Spieldaten...";

            await Task.Run(() => {
                try
                {
                    // 1. Bereinige das Ziel: Lösche den Zielordner, falls er existiert, um Konflikte zu vermeiden.
                    if (Directory.Exists(destinationDir))
                    {
                        Directory.Delete(destinationDir, true);
                    }
                    Directory.CreateDirectory(destinationDir);

                    // 2. Kopiere den Inhalt "Stück für Stück". Das ist am sichersten.
                    StatusText = "Kopiere Dateien...";

                    // Zuerst alle Unterverzeichnisse im Ziel erstellen
                    foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                    {
                        Directory.CreateDirectory(dir.Replace(sourceDir, destinationDir));
                    }

                    // Dann alle Dateien kopieren
                    foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                    {
                        File.Copy(file, file.Replace(sourceDir, destinationDir), true);
                    }

                    // 3. Lösche die Quelle, aber NUR, wenn es kein Steam-Ordner ist.
                    if (!sourceDir.Contains("steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusText = "Räume alte Daten auf...";
                        Directory.Delete(sourceDir, true);
                    }
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

        [RelayCommand]
        private void CloseApplication()
        {
            // _parentWindow ist die Referenz auf das MainWindow, die wir im Konstruktor übergeben.
            _parentWindow?.Close();
        }
    }
}