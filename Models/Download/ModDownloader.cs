using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Newtonsoft.Json.Linq;
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
        internal CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

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
                _updateStatus("Error: Thunderstore API nicht erreichbar.");
                return (null, false);
            }

            string localVersion = _settings.Modpack.CurrentLocalVersion;
            bool needsUpdate = localVersion != apiData.versionNumber;

            _updateStatus(needsUpdate ? $"Update zu v.{apiData.versionNumber} verfügbar!" : "Mods sind auf dem neusten stand.");
            return (apiData.versionNumber, needsUpdate);
        }

        public bool IsValheimRunningMod()
        {

            var processNames = new[] { "valheim", "valheim.exe" };

            foreach (var name in processNames)
            {
                var processes = Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(name)
                );

                if (processes.Length > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Forces an update of the modpack, downloading and installing all dependencies.
        /// </summary>
        public async Task ForceUpdateModpackAsync()
        {

            if (IsValheimRunningMod())
            {
                _updateStatus("Valheim läuft noch! Bitte schließe das Spiel zuerst.");
                return;
            }

            _updateStatus("Starte Mod update...");

            var apiData = await GetThunderstoreApiData("ImmernDarNew/ImmernDarNew_Modpack");
            //var apiData = await GetThunderstoreApiData("TeamKoro/Mithrael_Modpack"); //for testing

            if (apiData.dependencies == null)
            {
                _updateStatus("Error: Konnte die Mod Anforderungen nicht downloaden.");
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

            bool success = await DownloadDependenciesAsync(apiData.dependencies);

            if (success)
            {

                _updateStatus("Entpacke alle Mods...");
                await ExtractAllZipsToPluginsAsync(apiData.dependencies);

                await InstallBepInExCoreAsync();

                _settings.Modpack.CurrentLocalVersion = apiData.versionNumber;
                _saveSettings();
                _updateStatus("Modpack erfolgreich geupdated!");
            }

        }

        /// <summary>
        /// Downloads all required mod dependencies WITHOUT extracting them.
        /// </summary>
        private async Task<bool> DownloadDependenciesAsync(string[] dependencies)
        {
            _updateStatus("Überprüfe und downloade Mods...");

            string baseDirectory = _settings.ValheimInstallPath;
            string bepinexPath = Path.Combine(baseDirectory, "BepInEx");
            string pluginZipPath = Path.Combine(bepinexPath, "pluginZip");

            Directory.CreateDirectory(pluginZipPath);

            bool allOperationsSuccessful = true;
            int totalDependencies = dependencies.Length;
            int completedDependencies = 0;

            foreach (var dependency in dependencies)
            {
                completedDependencies++;
                double percentage = (double)completedDependencies / totalDependencies * 100;
                _updateProgress(percentage.ToString("F0", CultureInfo.InvariantCulture));

                string cachedZipPath = Path.Combine(pluginZipPath, $"{dependency}.zip");
                string downloadUrl = $"https://gcdn.thunderstore.io/live/repository/packages/{dependency}.zip";

                bool downloadNeeded = true;
                long onlineFileSize = -1;

                // HEAD-Request für Dateigröße
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
                    allOperationsSuccessful = false;
                }

                if (File.Exists(cachedZipPath))
                {  // Check if local file size matches online size (if we got it)
                    var localFileInfo = new FileInfo(cachedZipPath);
                    if (onlineFileSize > 0 && localFileInfo.Length == onlineFileSize)
                    {
                        downloadNeeded = false;
                        _updateStatus($"{dependency} ist bereits aktuell.");
                    }
                }

                if (downloadNeeded)
                {
                    bool downloadSuccess = false;
                    while (!downloadSuccess)
                    {
                        try
                        {
                            _updateStatus($"Downloade: {dependency}");
                            long fileSize = onlineFileSize > 0 ? onlineFileSize : 0;
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
                            _updateSpeed?.Invoke("");
                        }
                        catch (OperationCanceledException)
                        {
                            _updateStatus($"Download von {dependency} wurde abgebrochen.");
                            if (File.Exists(cachedZipPath))
                                await WaitForFileDeletionAsync(cachedZipPath, TimeSpan.FromSeconds(5), _cancellationTokenSource.Token);
                            return false;
                        }
                        catch (Exception ex)
                        {
                            _updateStatus($"Fehler beim Download von {dependency}: {ex.Message}");
                            if (!downloadSuccess)
                                await _showError($"Download-Fehler für {dependency}. Starte erneut...");
                            _updateSpeed?.Invoke("");
                        }
                    }
                }
            }

            return allOperationsSuccessful;
        }

        /// <summary>
        /// Entpackt ALLE ZIP-Dateien aus dem pluginZip-Ordner nach Abschluss aller Downloads.
        /// Verwendet exakt die gleiche Logik wie vorher.
        /// </summary>
        private async Task ExtractAllZipsToPluginsAsync(string[] dependencies)
        {
            string pluginsPath = Path.Combine(_settings.ValheimInstallPath, "BepInEx", "plugins");
            string pluginZipPath = Path.Combine(_settings.ValheimInstallPath, "BepInEx", "pluginZip");

            foreach (var dependency in dependencies)
            {
                string cachedZipPath = Path.Combine(pluginZipPath, $"{dependency}.zip");

                if (!File.Exists(cachedZipPath))
                {
                    _updateStatus($"Warnung: {dependency}.zip nicht gefunden.");
                    continue;
                }

                _updateStatus($"Entpacke: {dependency}");
                await ExtractToPlugins(dependency, pluginZipPath);   // Deine bestehende Methode (unverändert!)
            }

            _updateStatus("Alle Mods wurden entpackt.");
        }

        /// <summary>
        /// Extract data from zip files, but only those who we need.
        /// </summary>
        private async Task ExtractToPlugins(string dependency, string pluginZipPath)
        {
            string baseDirectory = _settings.ValheimInstallPath;
            string bepinexPath = Path.Combine(baseDirectory, "BepInEx");
            string pluginsPath = Path.Combine(bepinexPath, "plugins");

            bool isBepInExPack = dependency.Contains("denikson-BepInExPack_Valheim", StringComparison.OrdinalIgnoreCase);

            string cachedZipPath = Path.Combine(pluginZipPath, $"{dependency}.zip");
            string extractPath = isBepInExPack ? baseDirectory : Path.Combine(pluginsPath, dependency);
            Directory.CreateDirectory(extractPath);

            using (var archive = ZipFile.OpenRead(cachedZipPath))
            {
                var notAllowed = new HashSet<string> { "CHANGELOG.md", "icon.png", "manifest.json", "README.md", "LICENSE.md" };

                foreach (var entry in archive.Entries)
                {
                    try
                    {

                        string entryKey = entry.FullName.Replace('/', Path.DirectorySeparatorChar);


                        if (string.IsNullOrEmpty(entryKey) || entry.Length == 0 || entryKey.EndsWith(Path.DirectorySeparatorChar.ToString()))
                        {
                            continue;
                        }

                        if (notAllowed.Contains(Path.GetFileName(entryKey)))
                        {
                            continue;
                        }

                        if (entryKey.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            string tempInnerZipPath = Path.Combine(Path.GetTempPath(), $"temp_inner_{Guid.NewGuid()}.zip");
                            try
                            {

                                entry.ExtractToFile(tempInnerZipPath, overwrite: true);

                                await ExtractInnerZipContents(tempInnerZipPath, extractPath, dependency);
                            }
                            catch (Exception ex)
                            {
                                _updateStatus($"Error extracting {dependency}: {ex.Message}");
                            }
                            finally
                            {

                                if (File.Exists(tempInnerZipPath))
                                {
                                    await Task.Delay(100);

                                    bool deletedConfirmed = await WaitForFileDeletionAsync(
                                           tempInnerZipPath,
                                           TimeSpan.FromSeconds(10),
                                           _cancellationTokenSource.Token
                                        );

                                    if (!deletedConfirmed)
                                    {
                                        _updateStatus("Warnung: ZIP-Datei konnte nicht vollständig gelöscht werden (Timeout).");
                                        await _showError("Warnung: ZIP-Datei konnte nicht vollständig gelöscht werden (Timeout).");
                                    }
                                }
                            }
                        }
                        else
                        {

                            string destinationPath = Path.Combine(extractPath, entryKey);


                            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);


                            entry.ExtractToFile(destinationPath, overwrite: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Task.Delay(1000).Wait();
                        _updateStatus($"Error extracting from {dependency}: {ex.Message}");
                    }
                }
            }
            _updateStatus("Überprüfe Mods...");
        }

        /// <summary>
        /// Helps out for Zipfiles in Zipfiles to get the language yml files.
        /// </summary>
        private async Task ExtractInnerZipContents(string innerZipPath, string extractPath, string originalDependency)
        {
            using (var innerArchive = ZipFile.OpenRead(innerZipPath))
            {
                foreach (var innerEntry in innerArchive.Entries)
                {
                    string innerEntryKey = innerEntry.FullName;
                    string innerExtension = Path.GetExtension(innerEntryKey).ToLowerInvariant();
                    var allowedExtensions = new HashSet<string> { ".dll", ".yaml", ".yml", ".json", ".db", ".mdb", ".xml" };


                    if (string.IsNullOrEmpty(innerEntryKey) || innerEntry.Length == 0) continue;

                    if (innerEntryKey.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        string tempDeeperZipPath = Path.Combine(Path.GetTempPath(), $"temp_deeper_{Guid.NewGuid()}.zip");
                        try
                        {

                            innerEntry.ExtractToFile(tempDeeperZipPath, overwrite: true);
                            await ExtractInnerZipContents(tempDeeperZipPath, extractPath, originalDependency);
                        }
                        finally
                        {

                            if (File.Exists(tempDeeperZipPath))
                            {
                                bool deletedConfirmed = await WaitForFileDeletionAsync(
                                        tempDeeperZipPath,
                                        TimeSpan.FromSeconds(10)
                                    );

                                if (!deletedConfirmed)
                                {
                                    _updateStatus("Warnung: ZIP-Datei konnte nicht vollständig gelöscht werden (Timeout).");
                                    await _showError("Warnung: ZIP-Datei konnte nicht vollständig gelöscht werden (Timeout).");
                                }
                            }
                        }
                    }

                    else if (allowedExtensions.Contains(innerExtension) && !innerEntryKey.Contains("manifest.json"))
                    {
                        string fileName = Path.GetFileName(innerEntryKey);
                        string destinationPath = Path.Combine(extractPath, fileName);

                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                        innerEntry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }
            }
        }

        /// <summary>
        /// Cleanups old zip files that are no longer in the list of expected dependencies.
        /// </summary>
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

                    _updateStatus("Räume alte ZIP-Dateien auf...");

                    foreach (var zipFile in allZips)
                    {
                        var zipName = Path.GetFileName(zipFile);
                        if (!zipsToKeep.Contains(zipName))
                        {
                            _updateStatus($"Lösche alte ZIP: {zipName}");

                            bool deletedConfirmed = await WaitForFileDeletionAsync(
                                   zipFile,
                                   TimeSpan.FromSeconds(10),
                                   _cancellationTokenSource.Token
                                 );

                            if (!deletedConfirmed)
                            {
                                _updateStatus($"Warnung: Konnte alte ZIP-Datei ({zipName}) nicht löschen.");
                                await _showError($"Warnung: Konnte alte ZIP-Datei ({zipName}) nicht löschen. Bitte manuell löschen.");
                            }
                        }
                    }
                });

            }
            catch (Exception ex)
            {
                await _showError($"Fehler beim Aufräumen alter Zips: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up old mod folders in the plugins directory that are not part of the expected dependencies.
        /// </summary>
        /// <param name="pluginsPath">The path to the plugins directory.</param>
        /// <param name="expectedDependencies">The list of expected dependencies.</param>
        private async Task CleanupOldModsAsync(string pluginsPath)
        {
            _updateStatus("Bereinige alte Mod Ordner...");

            await Task.Run(async () =>
            {
                if (!Directory.Exists(pluginsPath)) return;

                var foldersToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "1ExtraMods",
                    "MMHOOK",
                    "HappyDragoon-DragoonCapes"
                };

                foreach (var dirPath in Directory.GetDirectories(pluginsPath))
                {
                    var dirName = new DirectoryInfo(dirPath).Name;
                    if (!foldersToKeep.Contains(dirName))
                    {
                        try
                        {
                            bool deleted = await ForceDeleteDirectoryAsync(dirPath);

                            if (!deleted)
                            {
                                _updateStatus($"Warnung: Konnte Mod Ordner nicht löschen.");
                                await _showError($"Warnung: Konnte Mod Ordner nicht löschen.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _updateStatus($"Konnte Mod Ordner nicht löschen.");
                            await _showError($"Konnte Mod Ordner nicht löschen.");
                        }
                    }
                }

                await Task.Delay(500);
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


        #region delete Files

        /// <summary>
        /// Trys to delete a file within a specified timeout period.
        /// Stellt sicher, dass auf die Freigabe des Dateihandles gewartet wird, ohne vorzeitig abzubrechen.
        /// </summary>
        public static async Task<bool> WaitForFileDeletionAsync(string filePath, TimeSpan timeout, CancellationToken externalCancellationToken = default)
        {
            if (!File.Exists(filePath)) return true;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch { /* Ignore */ }

            while (stopwatch.Elapsed < timeout && !externalCancellationToken.IsCancellationRequested)
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (IOException) { /* File is locked */ }
                catch (UnauthorizedAccessException) { /* access denied */ }


                if (!File.Exists(filePath))
                {
                    return true;
                }


                try
                {
                    await Task.Delay(50, externalCancellationToken);
                }
                catch (OperationCanceledException)
                {

                }
            }

            return !File.Exists(filePath);
        }

        /// <summary>
        /// Deletes a directory and all its contents, retrying if necessary.
        /// Nutzt ein Timeout, um auf die Freigabe von Dateihandles zu warten.
        /// </summary>
        public static async Task<bool> ForceDeleteDirectoryAsync(string path)
        {
            if (!Directory.Exists(path)) return true;


            var timeout = TimeSpan.FromSeconds(5);
            var stopwatch = Stopwatch.StartNew();

            return await Task.Run(async () =>
            {
                while (stopwatch.Elapsed < timeout)
                {
                    try
                    {
                        var dir = new DirectoryInfo(path);

                        foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                        {
                            if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            {
                                file.Attributes &= ~FileAttributes.ReadOnly;
                            }
                        }


                        dir.Delete(recursive: true);

                        return true;
                    }

                    catch (IOException)
                    {

                    }
                    catch (UnauthorizedAccessException)
                    {

                    }
                    catch (Exception)
                    {

                    }

                    await Task.Delay(250);
                }

                return !Directory.Exists(path);
            });
        }
        #endregion
    }
}