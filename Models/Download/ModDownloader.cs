using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using ValheimCrossPlatformLauncher;
using ValheimLauncher2.Models.Settings;
using File = System.IO.File;

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
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public void CancelOperations()
        {
            _cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// Checks for available updates for the modpack by comparing local and online versions.
        /// </summary>
        /// <returns>A tuple containing the online version and a boolean indicating if an update is needed.</returns>
        public async Task<(string? onlineVersion, bool needsUpdate)> CheckForUpdatesAsync()
        {
            _updateStatus("Checke mod version...");
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
            //var apiData = await GetThunderstoreApiData("TeamKoro/Mithrael_Modpack");
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

            _updateStatus($"Räume auf...");
            await CleanupOldModsAsync(pluginsPath);
            await CleanupOldZipsAsync(pluginZipPath, apiData.dependencies.ToList());

            bool success = await DownloadAndExtractDependenciesAsync(apiData.dependencies);

            if (success)
            {
                await InstallBepInExCoreAsync();

                _settings.Modpack.CurrentLocalVersion = apiData.versionNumber;
                _saveSettings();
                _updateStatus("Modpack updated!");
            }
            else
            {
                _updateStatus("Mod Update hatte Fehler.");
            }
        }

        /// <summary>
        /// Downloads and extracts all required mod dependencies.
        /// </summary>
        /// <param name="dependencies">The list of mod dependencies to download and extract.</param>
        /// <returns>True if all operations were successful; otherwise, false.</returns>
        private async Task<bool> DownloadAndExtractDependenciesAsync(string[] dependencies)
        {
            _updateStatus($"Überprüfe Mods...");
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


                bool isBepInExPack = dependency.Contains("denikson-BepInExPack_Valheim", StringComparison.OrdinalIgnoreCase);

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
                catch (HttpRequestException hex)
                {
                    _updateStatus($"HEAD failed for {dependency} (will download anyway): {hex.Message}");
                    await MessageBox.Show(_parentWindow, $"HEAD failed for {dependency} (will download anyway)");
                    allOperationsSuccessful = false;
                }

                if (File.Exists(cachedZipPath))
                {
                    if (IsZipValid(cachedZipPath, onlineFileSize))
                    {
                        downloadNeeded = false;
                    }
                    else
                    {

                        _updateStatus($"Lösche alte Zip-Datei: {dependency}");
                        await Task.Delay(1000);
                        await DeleteFileWithRetryAsync(cachedZipPath);
                        await Task.Delay(1000);
                        downloadNeeded = true;
                    }

                }

                if (downloadNeeded)
                {
                    bool downloadSuccess = false;

                    try
                    {
                        long fileSize = onlineFileSize > 0 ? onlineFileSize : 0;

                        _updateStatus($"Downloade: {dependency}");


                        var buffer = new byte[81920];
                        long totalBytes = 0;
                        var lastReportTime = DateTime.UtcNow;

                        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);
                        response.EnsureSuccessStatusCode();

                        using var responseStream = await response.Content.ReadAsStreamAsync();

                        using var fileStream = new FileStream(cachedZipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, bufferSize: 81920, useAsync: true);

                        int bytesRead;

                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                        {

                            await fileStream.WriteAsync(buffer, 0, bytesRead, _cancellationTokenSource.Token);
                            totalBytes += bytesRead;


                            var now = DateTime.UtcNow;
                            if ((now - lastReportTime).TotalMilliseconds >= 100)
                            {
                                double downloadedMb = totalBytes / (1024.0 * 1024.0);
                                double totalMb = fileSize / (1024.0 * 1024.0);
                                string progressText = fileSize > 0 ? $"{downloadedMb:F2} / {totalMb:F2} MB" : $"{downloadedMb:F2} MB";
                                _updateSpeed?.Invoke(progressText);
                                lastReportTime = now;
                            }
                        }

                        downloadSuccess = true;
                    }
                    // Issues related to file access
                    catch (IOException ioEx)
                    {
                        string errorMessage = "Fehler beim Zugriff auf die Festplatte (Datei gesperrt/Platte voll/Pfadproblem).";
                        if (ioEx.Message.Contains("access") || ioEx.Message.Contains("use by another process"))
                        {
                            errorMessage = "Fehler: Die Datei wird gesperrt (Access Denied). Bitte schließen Sie externe Programme (z.B. Virenscanner).";
                        }
                        _updateStatus($"I/O Fehler beim Download von {dependency}: {errorMessage}");
                    }
                    // Issues related to HTTP requests
                    catch (HttpRequestException httpEx)
                    {
                        string statusCodeInfo = httpEx.StatusCode.HasValue
                            ? $"Status Code: {(int)httpEx.StatusCode} {httpEx.StatusCode.Value}"
                            : "Verbindungsfehler";

                        _updateStatus($"HTTP Fehler beim Download von {dependency}: {statusCodeInfo} ({httpEx.Message})");
                    }
                    // Issues related to cancellation
                    catch (OperationCanceledException)
                    {
                        _updateStatus($"Download von {dependency} wurde abgebrochen.");
                    }
                    // All other exceptions
                    catch (Exception ex)
                    {
                        _updateStatus($"Unbekannter kritischer Fehler beim Download von {dependency}: {ex.Message}");
                    }

                    // After all attempts, check if download was successful
                    if (!downloadSuccess)
                    {
                        await MessageBox.Show(_parentWindow, $"Download-Fehler für {dependency}. Siehe Log für Details.");
                    }

                }




                _updateSpeed?.Invoke("");
                ExtractToPlugins(dependency, pluginZipPath);
            }

            return allOperationsSuccessful;
        }


        /// <summary>
        /// Extract data from zip files, but only those who we need.
        /// </summary>
        private void ExtractToPlugins(string dependency, string pluginZipPath)
        {
            try
            {
                string baseDirectory = _settings.ValheimInstallPath;
                string bepinexPath = Path.Combine(baseDirectory, "BepInEx");
                string pluginsPath = Path.Combine(bepinexPath, "plugins");

                bool isBepInExPack = dependency.Contains("denikson-BepInExPack_Valheim", StringComparison.OrdinalIgnoreCase);

                string cachedZipPath = Path.Combine(pluginZipPath, $"{dependency}.zip");
                string extractPath = isBepInExPack ? baseDirectory : Path.Combine(pluginsPath, dependency);
                Directory.CreateDirectory(extractPath);

                using (var archive = ArchiveFactory.Open(cachedZipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string entryKey = entry.Key;
                        var notAllowed = new HashSet<string> { "CHANGELOG.md", "icon.png", "manifest.json", "README.md", "LICENSE.md" };

                        if (entryKey.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            string tempInnerZipPath = Path.Combine(Path.GetTempPath(), $"temp_inner_{Guid.NewGuid()}.zip");
                            try
                            {

                                entry.WriteToFile(tempInnerZipPath, new ExtractionOptions { Overwrite = true });


                                ExtractInnerZipContents(tempInnerZipPath, extractPath, dependency);
                            }
                            finally
                            {

                                if (File.Exists(tempInnerZipPath))
                                {
                                    File.Delete(tempInnerZipPath);
                                }
                            }
                        }
                        else if (!notAllowed.Contains(entry.Key))
                        {

                            entry.WriteToDirectory(extractPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _updateStatus($"Error extracting {dependency}: {ex.Message}");

            }
        }

        /// <summary>
        /// Helps out for Zipfiles in Zipfiles to get the language yml files.
        /// </summary>
        private void ExtractInnerZipContents(string innerZipPath, string extractPath, string originalDependency)
        {
            using (var innerArchive = ArchiveFactory.Open(innerZipPath))
            {
                foreach (var innerEntry in innerArchive.Entries)
                {
                    string innerEntryKey = innerEntry.Key;
                    string innerExtension = Path.GetExtension(innerEntryKey).ToLowerInvariant();
                    var allowedExtensions = new HashSet<string> { ".dll", ".yaml", ".yml", ".json", ".db", ".mdb", ".xml" };

                    if (innerEntryKey.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {

                        string tempDeeperZipPath = Path.Combine(Path.GetTempPath(), $"temp_deeper_{Guid.NewGuid()}.zip");
                        try
                        {
                            innerEntry.WriteToFile(tempDeeperZipPath, new ExtractionOptions { Overwrite = true });
                            ExtractInnerZipContents(tempDeeperZipPath, extractPath, originalDependency);
                        }
                        finally
                        {
                            if (File.Exists(tempDeeperZipPath))
                            {
                                Task.Run(() => DeleteFileWithRetryAsync(tempDeeperZipPath)).Wait();
                            }
                        }
                    }
                    else if (allowedExtensions.Contains(innerExtension) && !innerEntryKey.Contains("manifest.json"))
                    {

                        innerEntry.WriteToDirectory(extractPath, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                    }
                }
            }
        }

        /// <summary>
        /// Cleans up old zip files in the plugin zip directory that are not part of the expected dependencies.
        /// </summary>
        /// <param name="pluginZipPath">The path to the plugin zip directory.</param>
        /// <param name="expectedDependencies">The list of expected dependencies.</param>
        private async Task CleanupOldZipsAsync(string pluginZipPath, List<string> expectedDependencies)
        {
            try
            {
                await Task.Run(async () =>
                {
                    if (!Directory.Exists(pluginZipPath))
                    {
                        return;
                    }

                    var zipsToKeep = new HashSet<string>(expectedDependencies.Select(dep => $"{dep}.zip"), StringComparer.OrdinalIgnoreCase);
                    var allZips = Directory.GetFiles(pluginZipPath, "*.zip");

                    foreach (var zipFile in allZips)
                    {
                        var zipName = Path.GetFileName(zipFile);
                        if (!zipsToKeep.Contains(zipName))
                        {

                            await DeleteFileWithRetryAsync(zipFile);

                        }
                    }


                });

            }
            catch (Exception ex)
            {

                await MessageBox.Show(_parentWindow, $"Error");
            }

        }

        /// <summary>
        /// Cleans up old mod folders in the plugins directory that are not part of the expected dependencies.
        /// </summary>
        /// <param name="pluginsPath">The path to the plugins directory.</param>
        /// <param name="expectedDependencies">The list of expected dependencies.</param>
        private async Task CleanupOldModsAsync(string pluginsPath)
        {
            try
            {

                await Task.Run(() =>
                {
                    if (!Directory.Exists(pluginsPath)) return;

                    var foldersToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                                Directory.Delete(dirPath, true);
                            }
                            catch (Exception ex)
                            {

                            }
                        }
                    }
                });

            }
            catch (Exception ex)
            {
                _updateStatus($"Konnte Mod Ordner nicht löschen. {ex.Message} ");
                await MessageBox.Show(_parentWindow, $"Error");
            }

        }


        /// <summary>
        /// Check if a file can be deleted, with retries if it's locked.
        /// </summary>
        private async Task<bool> DeleteFileWithRetryAsync(string filePath, int maxRetries = 10, int delayMs = 1500)
        {

            for (int i = 0; i < maxRetries; i++)
            {

                if (!File.Exists(filePath))
                {

                    return true;
                }

                try
                {

                    File.Delete(filePath);
                    return true; 
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {

                    if (i == maxRetries - 1)
                    {
                        break;
                    }

                    _updateStatus($"Warte auf Freigabe von '{Path.GetFileName(filePath)}' (Versuch {i + 1}/{maxRetries})...");


                    await Task.Delay(delayMs * (i + 1));
                }
                catch (Exception ex)
                {

                    _updateStatus($"Unbekannter Fehler beim Löschen von '{Path.GetFileName(filePath)}': {ex.Message}");
                    return false;
                }
            }


            if (File.Exists(filePath))
            {
                _updateStatus($"FEHLER: Konnte '{Path.GetFileName(filePath)}' nach {maxRetries} Versuchen nicht löschen. Die Datei bleibt gesperrt.");
            }
            else
            {
                return true;
            }

            return false;
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
        /// checks if the cached zip file is valid by verifying its existence, size, and integrity.
        /// </summary>
        private bool IsZipValid(string cachedZipPath, long onlineFileSize)
        {
            if (!System.IO.File.Exists(cachedZipPath))
            {
                return false;
            }

            try
            {
                var localFileInfo = new FileInfo(cachedZipPath);
                if (localFileInfo.Length != onlineFileSize)
                {
                    _updateStatus("Cache-Prüfung: Größe stimmt nicht überein. (Download benötigt)");
                    return false;
                }

                using (var archive = SharpCompress.Archives.ArchiveFactory.Open(cachedZipPath))
                {

                    if (archive.Entries.Any())
                    {
                        return true;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {

                _updateStatus($"Cache-Prüfung: ZIP-Datei ist beschädigt/korrupt. {ex.Message} (Download benötigt)");
                return false;
            }
        }
    }
}