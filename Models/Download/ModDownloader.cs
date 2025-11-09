using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using ValheimCrossPlatformLauncher;
using ValheimLauncher2.Models.Settings;

namespace ValheimLauncher2.Models.Download
{
    /// <summary>
    /// Provides functionality to download, update, and manage Valheim modpacks and their dependencies.
    /// </summary>
    public class ModDownloader
    {
        private static readonly HttpClient _httpClient = new();
        private readonly Action<string> _updateStatus;
        private readonly Action<string> _updateProgress;
        private readonly Action<string> _updateSpeed;
        private readonly LauncherSettings _settings;
        private readonly Action _saveSettings;
        private readonly Func<string, Task> _showError;
        private Window _parentWindow;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModDownloader"/> class.
        /// </summary>
        /// <param name="updateStatus">Action to update the status message.</param>
        /// <param name="updateProgress">Action to update the progress value.</param>
        /// <param name="updateSpeed">Action to update the download speed.</param>
        /// <param name="settings">The launcher settings instance.</param>
        /// <param name="saveSettings">Action to save the settings.</param>
        /// <param name="showError">Function to display error messages asynchronously.</param>
        public ModDownloader(Action<string> updateStatus, Action<string> updateProgress, Action<string> updateSpeed, LauncherSettings settings, Action saveSettings, Func<string, Task> showError)
        {
            _updateStatus = updateStatus;
            _updateProgress = updateProgress;
            _updateSpeed = updateSpeed;
            _settings = settings;
            _saveSettings = saveSettings;
            _showError = showError;
        }

        /// <summary>
        /// Checks for available updates for the modpack by comparing local and online versions.
        /// </summary>
        /// <returns>A tuple containing the online version and a boolean indicating if an update is needed.</returns>
        public async Task<(string? onlineVersion, bool needsUpdate)> CheckForUpdatesAsync()
        {
            _updateStatus("Checking mod versions...");
            var apiData = await GetThunderstoreApiData("ImmernDarNew/ImmernDarNew_Modpack");
            if (apiData.versionNumber == null)
            {
                _updateStatus("Error: Thunderstore API not reachable.");
                return (null, false);
            }

            string localVersion = _settings.Modpack.CurrentLocalVersion;
            bool needsUpdate = localVersion != apiData.versionNumber;

            _updateStatus(needsUpdate ? $"Update to v.{apiData.versionNumber} available!" : "Mods are up to date.");
            return (apiData.versionNumber, needsUpdate);
        }

        /// <summary>
        /// Forces an update of the modpack, downloading and installing all dependencies.
        /// </summary>
        public async Task ForceUpdateModpackAsync()
        {
            _updateStatus("Starting mod update...");
            var apiData = await GetThunderstoreApiData("ImmernDarNew/ImmernDarNew_Modpack");
            if (apiData.dependencies == null)
            {
                _updateStatus("Error: Could not retrieve modpack dependencies.");
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
                _updateStatus("Modpack updated successfully!");
            }
            else
            {
                _updateStatus("Mod update completed with errors.");
            }
        }

        /// <summary>
        /// Downloads and extracts all required mod dependencies.
        /// </summary>
        /// <param name="dependencies">The list of mod dependencies to download and extract.</param>
        /// <returns>True if all operations were successful; otherwise, false.</returns>
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

                _updateStatus($"Processing: {dependency}");
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
                        _updateStatus($"Could not retrieve online file size for {dependency}: {ex.Message}");
                        await MessageBox.Show(_parentWindow, $"Could not retrieve online file size for {dependency}: {ex.Message}");
                    }

                    if (File.Exists(cachedZipPath) && onlineFileSize > 0)
                    {
                        var localFileInfo = new FileInfo(cachedZipPath);
                        if (localFileInfo.Length == onlineFileSize)
                        {
                            _updateStatus($"Using cache for: {dependency}");
                            downloadNeeded = false;
                        }
                    }

                    if (downloadNeeded)
                    {
                        File.Delete(cachedZipPath);
                        // Hole Dateigröße
                        long fileSize =0;
                        using (var headResp = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, downloadUrl)))
                        {
                            if (headResp.IsSuccessStatusCode)
                                fileSize = headResp.Content.Headers.ContentLength ??0;
                        }
                        string fileSizeMb = fileSize >0 ? $"({(fileSize /1024.0 /1024.0):F2} MB)" : "";
                        _updateStatus($"Downloading: {dependency}");
                        using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var fileStream = new FileStream(cachedZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                var buffer = new byte[81920];
                                int bytesRead;
                                long totalBytes =0;
                                var lastReportTime = DateTime.UtcNow;
                                long lastBytes =0;
                                using (var responseStream = await response.Content.ReadAsStreamAsync())
                                {
                                    while ((bytesRead = await responseStream.ReadAsync(buffer,0, buffer.Length)) >0)
                                    {
                                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                                        totalBytes += bytesRead;
                                        var now = DateTime.UtcNow;
                                        if ((now - lastReportTime).TotalMilliseconds >= 100)
                                        {
                                            double downloadedMb = totalBytes / (1024.0 * 1024.0);
                                            double totalMb = fileSize / (1024.0 * 1024.0);
                                            string progressText = fileSize > 0 ? $"{downloadedMb:F2} / {totalMb:F2} MB" : $"{downloadedMb:F2} MB";
                                            _updateSpeed?.Invoke(progressText);
                                            lastBytes = totalBytes;
                                            lastReportTime = now;
                                        }
                                    }
                                }
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
                    _updateStatus($"Error with {dependency}. Maybe in use!");
                    await MessageBox.Show(_parentWindow, $"Error with {dependency}. Maybe in use!");
                    allOperationsSuccessful = false;
                }

            }

            _updateSpeed?.Invoke(""); 
            return allOperationsSuccessful;
        }

        /// <summary>
        /// Cleans up old zip files in the plugin zip directory that are not part of the expected dependencies.
        /// </summary>
        /// <param name="pluginZipPath">The path to the plugin zip directory.</param>
        /// <param name="expectedDependencies">The list of expected dependencies.</param>
        private async Task CleanupOldZipsAsync(string pluginZipPath, List<string> expectedDependencies)
        {
            _updateStatus("Cleaning up old ZIP files...");
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
                            _updateStatus($"Deleting old ZIP file: {zipName}");
                            File.Delete(zipFile);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Could not delete old ZIP file {zipName}: {ex.Message}");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Cleans up old mod folders in the plugins directory that are not part of the expected dependencies.
        /// </summary>
        /// <param name="pluginsPath">The path to the plugins directory.</param>
        /// <param name="expectedDependencies">The list of expected dependencies.</param>
        private async Task CleanupOldModsAsync(string pluginsPath, List<string> expectedDependencies)
        {
            _updateStatus("Cleaning up old mod folders...");
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
                            _updateStatus($"Deleting old mod folder: {dirName}");
                            Directory.Delete(dirPath, true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Could not delete old mod folder {dirName}: {ex.Message}");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Installs the BepInEx core components by merging the extracted folder into the base directory.
        /// </summary>
        private async Task InstallBepInExCoreAsync()
        {
            _updateStatus("Installing BepInEx core components...");
            string baseDirectory = _settings.ValheimInstallPath;
            string sourceFolderPath = Path.Combine(baseDirectory, "BepInExPack_Valheim");

            if (Directory.Exists(sourceFolderPath))
            {
                try
                {
                    MergeDirectory(sourceFolderPath, baseDirectory);
                    Directory.Delete(sourceFolderPath, true);
                    _updateStatus("BepInEx core components installed successfully.");
                }
                catch (Exception ex)
                {
                    _updateStatus($"Error during BepInEx core installation: {ex.Message}");
                    Debug.WriteLine(ex);
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Merges the contents of the source directory into the target directory.
        /// </summary>
        /// <param name="sourceDir">The source directory.</param>
        /// <param name="targetDir">The target directory.</param>
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

        /// <summary>
        /// Retrieves Thunderstore API data for the specified modpack.
        /// </summary>
        /// <param name="modpackId">The modpack identifier.</param>
        /// <returns>A tuple containing the download URL, version number, and dependencies.</returns>
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

        /// <summary>
        /// Waits for the directory cleanup to complete by checking for remaining unexpected folders.
        /// </summary>
        /// <param name="path">The directory path to check.</param>
        /// <param name="expectedFolders">The set of expected folder names.</param>
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