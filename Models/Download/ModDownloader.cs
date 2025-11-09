using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using ValheimLauncher2.Models.Settings;

namespace ValheimLauncher2.Models.Download
{
    public class ModDownloader
    {
        private static readonly HttpClient _httpClient = new();
        private readonly Action<string> _updateStatus;
        private readonly Action<string> _updateProgress;
        private readonly LauncherSettings _settings;
        private readonly Action _saveSettings;

        public ModDownloader(Action<string> updateStatus, Action<string> updateProgress, LauncherSettings settings, Action saveSettings)
        {
            _updateStatus = updateStatus;
            _updateProgress = updateProgress;
            _settings = settings;
            _saveSettings = saveSettings;
        }

        public async Task<(string? onlineVersion, bool needsUpdate)> CheckForUpdatesAsync()
        {
            _updateStatus("Prüfe Mod-Versionen...");
            var apiData = await GetThunderstoreApiData("ImmernDarNew/ImmernDarNew_Modpack");
            if (apiData.versionNumber == null)
            {
                _updateStatus("Fehler: Thunderstore API nicht erreichbar.");
                return (null, false);
            }

            string localVersion = _settings.Modpack.CurrentLocalVersion;
            bool needsUpdate = localVersion != apiData.versionNumber;

            _updateStatus(needsUpdate ? $"Update auf v.{apiData.versionNumber} verfügbar!" : "Mods sind aktuell.");
            return (apiData.versionNumber, needsUpdate);
        }

        public async Task ForceUpdateModpackAsync()
        {
            _updateStatus("Starte Mod-Update...");
            var apiData = await GetThunderstoreApiData("ImmernDarNew/ImmernDarNew_Modpack");
            if (apiData.dependencies == null)
            {
                _updateStatus("Fehler: Konnte Modpack-Abhängigkeiten nicht abrufen.");
                return;
            }

            _settings.Modpack.ExpectedModFiles = new List<string>(apiData.dependencies);
            string pluginsPath = Path.Combine(_settings.ValheimInstallPath, "BepInEx", "plugins");
            string pluginZipPath = Path.Combine(_settings.ValheimInstallPath, "BepInEx", "pluginZip");
            string extraModsPath = Path.Combine(pluginsPath, "1ExtraMods");
            Directory.CreateDirectory(extraModsPath);


            await CleanupOldModsAsync(pluginsPath, apiData.dependencies.ToList());
            await CleanupOldZipsAsync(pluginZipPath, apiData.dependencies.ToList());


            await WaitForDirectoryCleanup(pluginsPath, apiData.dependencies.Concat(new[] { "1ExtraMods", "MMHOOK", "HappyDragoon-DragoonCapes" }));

            bool success = await DownloadAndExtractDependenciesAsync(apiData.dependencies);

            if (success)
            {
                await InstallBepInExCoreAsync();

                _settings.Modpack.CurrentLocalVersion = apiData.versionNumber;
                _saveSettings();
                _updateStatus("Modpack erfolgreich aktualisiert!");
            }
            else
            {
                _updateStatus("Mod-Update mit Fehlern abgeschlossen.");
            }
        }

        private async Task<bool> DownloadAndExtractDependenciesAsync(string[] dependencies)
        {
            string baseDirectory = _settings.ValheimInstallPath;
            string bepinexPath = Path.Combine(baseDirectory, "BepInEx");
            string pluginsPath = Path.Combine(bepinexPath, "plugins");
            string pluginZipPath = Path.Combine(bepinexPath, "pluginZip");

            Directory.CreateDirectory(pluginsPath);
            Directory.CreateDirectory(pluginZipPath);

            bool allOperationsSuccessful = true;
            int totalDependencies = dependencies.Length;
            int completedDependencies = 0;

            foreach (var dependency in dependencies)
            {
                completedDependencies++;
                double percentage = (double)completedDependencies / totalDependencies * 100;
                _updateProgress(percentage.ToString("F0", CultureInfo.InvariantCulture));

                _updateStatus($"Verarbeite: {dependency}");
                bool isBepInExPack = dependency.Contains("denikson-BepInExPack_Valheim", StringComparison.OrdinalIgnoreCase);

                try
                {
                    string cachedZipPath = Path.Combine(pluginZipPath, $"{dependency}.zip");
                    string downloadUrl = $"https://gcdn.thunderstore.io/live/repository/packages/{dependency}.zip";
                    bool downloadNeeded = true;

                    long onlineFileSize = -1;
                    try
                    {
                        using var headRequest = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
                        using var headResponse = await _httpClient.SendAsync(headRequest);
                        headResponse.EnsureSuccessStatusCode();
                        onlineFileSize = headResponse.Content.Headers.ContentLength ?? -1;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Konnte Online-Dateigröße für {dependency} nicht abrufen: {ex.Message}");
                    }


                    if (File.Exists(cachedZipPath) && onlineFileSize > 0)
                    {
                        var localFileInfo = new FileInfo(cachedZipPath);
                        if (localFileInfo.Length == onlineFileSize)
                        {
                            _updateStatus($"Verwende Cache für: {dependency}");
                            downloadNeeded = false;
                        }
                    }


                    if (downloadNeeded)
                    {
                        File.Delete(cachedZipPath);
                        _updateStatus($"Lade herunter: {dependency}");
                        using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var fileStream = new FileStream(cachedZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await response.Content.CopyToAsync(fileStream);
                            }
                        }
                    }

                    string extractPath = isBepInExPack ? baseDirectory : Path.Combine(pluginsPath, dependency);
                    Directory.CreateDirectory(extractPath);
                    using (var archive = ArchiveFactory.Open(cachedZipPath))
                    {

                        foreach (var entry in archive.Entries)
                        {

                            if (!entry.IsDirectory && !entry.Key.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                            {

                                entry.WriteToDirectory(extractPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _updateStatus($"Fehler bei {dependency}: {ex.Message}");
                    Debug.WriteLine(ex);
                    allOperationsSuccessful = false;
                }
            }

            return allOperationsSuccessful;
        }

        private async Task CleanupOldZipsAsync(string pluginZipPath, List<string> expectedDependencies)
        {
            _updateStatus("Bereinige alte ZIP-Dateien...");
            await Task.Run(() =>
            {
                if (!Directory.Exists(pluginZipPath)) return;
                var zipsToKeep = new HashSet<string>(expectedDependencies.Select(dep => $"{dep}.zip"), StringComparer.OrdinalIgnoreCase);

                foreach (var zipFile in Directory.GetFiles(pluginZipPath, "*.zip"))
                {
                    var zipName = Path.GetFileName(zipFile);
                    if (!zipsToKeep.Contains(zipName))
                    {
                        try
                        {
                            _updateStatus($"Lösche alte ZIP-Datei: {zipName}");
                            File.Delete(zipFile);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Konnte alte ZIP-Datei nicht löschen {zipName}: {ex.Message}");
                        }
                    }
                }
            });
        }

        private async Task CleanupOldModsAsync(string pluginsPath, List<string> expectedDependencies)
        {
            _updateStatus("Bereinige alte Mod-Ordner...");
            await Task.Run(() =>
            {
                if (!Directory.Exists(pluginsPath)) return;
                var foldersToKeep = new HashSet<string>(expectedDependencies, StringComparer.OrdinalIgnoreCase);
                foldersToKeep.Add("1ExtraMods");
                foldersToKeep.Add("MMHOOK");
                foldersToKeep.Add("HappyDragoon-DragoonCapes");
                foreach (var dirPath in Directory.GetDirectories(pluginsPath))
                {
                    var dirName = new DirectoryInfo(dirPath).Name;
                    if (!foldersToKeep.Contains(dirName))
                    {
                        try
                        {
                            _updateStatus($"Lösche alten Mod-Ordner: {dirName}");
                            Directory.Delete(dirPath, true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Konnte alten Mod-Ordner nicht löschen {dirName}: {ex.Message}");
                        }
                    }
                }
            });
        }

        private async Task InstallBepInExCoreAsync()
        {
            _updateStatus("Installiere BepInEx Kernkomponenten...");
            string baseDirectory = _settings.ValheimInstallPath;
            string sourceFolderPath = Path.Combine(baseDirectory, "BepInExPack_Valheim");

            if (Directory.Exists(sourceFolderPath))
            {
                try
                {
                    MergeDirectory(sourceFolderPath, baseDirectory);
                    Directory.Delete(sourceFolderPath, true);
                    _updateStatus("BepInEx Kernkomponenten erfolgreich installiert.");
                }
                catch (Exception ex)
                {
                    _updateStatus($"Fehler bei BepInEx-MOD-Installation: {ex.Message}");
                    Debug.WriteLine(ex);
                }
            }
            await Task.CompletedTask;
        }

        private void MergeDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string targetFile = Path.Combine(targetDir, file.Substring(sourceDir.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                File.Move(file, targetFile, true);
            }
        }

        private async Task<(string? downloadUrl, string? versionNumber, string[]? dependencies)> GetThunderstoreApiData(string modpackId)
        {
            try
            {
                string url = $"https://thunderstore.io/api/experimental/package/{modpackId}/";
                string jsonResponse = await _httpClient.GetStringAsync(url);
                JObject data = JObject.Parse(jsonResponse);

                string? downloadUrl = data.SelectToken("latest.download_url")?.ToString();
                string? versionNumber = data.SelectToken("latest.version_number")?.ToString();
                var dependencies = data.SelectToken("latest.dependencies")?.ToObject<List<string>>() ?? new List<string>();

                return (downloadUrl, versionNumber, dependencies.ToArray());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching Thunderstore API data: {ex.Message}");
                return (null, null, null);
            }
        }

        private async Task WaitForDirectoryCleanup(string path, IEnumerable<string> expectedFolders)
        {
            int retries = 10;
            while (retries-- > 0)
            {
                var remaining = Directory.GetDirectories(path)
                    .Select(d => new DirectoryInfo(d).Name)
                    .Except(expectedFolders, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!remaining.Any())
                    break;
                await Task.Delay(200);
            }
        }
    }
}