using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
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

        public bool IsStartEnabled => IsGameInstalled;
        public bool IsInstallGameVisible => !IsGameInstalled;
        public bool IsFixValheimEnabled => IsGameInstalled;
        public bool IsMPDownloadEnabled => IsGameInstalled;
        public bool IsResetVisible => IsGameInstalled;

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
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
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
            string exePath = Path.Combine(currentSettings.ValheimInstallPath, "valheim.exe");
            string dataPath = PlatformUtils.GetValheimDataPath(currentSettings.ValheimInstallPath);
            string bootConfigPath = Path.Combine(dataPath, "boot.config");

            IsGameInstalled = File.Exists(exePath) && File.Exists(bootConfigPath);
            InstallPathText = currentSettings.ValheimInstallPath;
        }

        [RelayCommand]
        private async Task StartGame()
        {
            IsGameInstalled = false;
            StatusText = "Starte das Spiel...";

            // Correct launch arguments including exclusive window mode
            string launchArgs = VulkanEnabled ? "-force-vulkan -window-mode exclusive" : "-force-d3d11 -window-mode exclusive";

            await Task.Run(() =>
            {
                // Ensure manifest is protected before launch
                PlatformUtils.SetSteamManifestProtection(currentSettings.ValheimInstallPath, true);

                if (currentSettings.ValheimInstallPath.Contains("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    PlatformUtils.OpenUrl($"steam://run/892970//{Uri.EscapeDataString(launchArgs)}/");
                }
                else
                {
                    string exePath = Path.Combine(currentSettings.ValheimInstallPath, "valheim.exe");
                    Process.Start(new ProcessStartInfo(exePath) { Arguments = launchArgs, UseShellExecute = true });
                }
            });
        }

        [RelayCommand]
        private async Task InstallGame()
        {
            var dialog = new OpenFolderDialog { Title = "Wähle einen Installationsordner" };
            var newPath = await dialog.ShowAsync(_parentWindow);

            if (string.IsNullOrEmpty(newPath))
            {
                StatusText = "Installation abgebrochen.";
                return;
            }

            currentSettings.ValheimInstallPath = newPath;
            InstallPathText = newPath;
            IsGameInstalled = false;

            await _clientDownloader.InstallGameAsync(currentSettings.ValheimInstallPath);
            await _modDownloader.ForceUpdateModpackAsync();

            // Protect manifest after installation
            PlatformUtils.SetSteamManifestProtection(currentSettings.ValheimInstallPath, true);

            SaveSettings();
            Checkstatus();
        }

        [RelayCommand]
        private async Task FixValheim()
        {
            IsGameInstalled = false;
            await _clientDownloader.FixValheimAsync(currentSettings.ValheimInstallPath);
            await _modDownloader.ForceUpdateModpackAsync();

            // Protect manifest after fix
            PlatformUtils.SetSteamManifestProtection(currentSettings.ValheimInstallPath, true);

            Checkstatus();
        }

        [RelayCommand]
        private async Task ManualModUpdate()
        {
            IsGameInstalled = false;
            await _modDownloader.ForceUpdateModpackAsync();
            LocalModpackVersion = "v. " + (currentSettings.Modpack?.CurrentLocalVersion ?? "unbekannt");
            Checkstatus();
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
            var dialog = new OpenFolderDialog { Title = "Wähle einen neuen Installationsordner" };
            var newPath = await dialog.ShowAsync(_parentWindow);

            if (string.IsNullOrEmpty(newPath)) return;

            // Remove protection from old path before moving
            PlatformUtils.SetSteamManifestProtection(currentSettings.ValheimInstallPath, false);

            string oldPath = currentSettings.ValheimInstallPath;

            if (Directory.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                var result = await ConfirmDialog.Show(_parentWindow, "Installation verschieben?", $"Möchten Sie Ihre bestehende Installation nach '{newPath}' verschieben?");
                if (result == ConfirmDialog.DialogResult.Yes)
                {
                    await MoveGameDataAsync(oldPath, newPath);
                }
            }

            currentSettings.ValheimInstallPath = newPath;

            // Add protection to new path
            PlatformUtils.SetSteamManifestProtection(currentSettings.ValheimInstallPath, true);

            SaveSettings();
            Checkstatus();
        }

        private async Task MoveGameDataAsync(string sourceDir, string destinationDir)
        {
            IsGameInstalled = false;
            StatusText = "Verschiebe Spieldaten...";

            await Task.Run(() => {
                if (Directory.Exists(destinationDir))
                {
                    Directory.Delete(destinationDir, true);
                }
                Directory.CreateDirectory(destinationDir);

                foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                {
                    Directory.CreateDirectory(dir.Replace(sourceDir, destinationDir));
                }

                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    File.Copy(file, file.Replace(sourceDir, destinationDir), true);
                }
            });

            if (!sourceDir.Contains("steamapps", StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(sourceDir, true);
            }
            StatusText = "Verschieben abgeschlossen!";
            Checkstatus();
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