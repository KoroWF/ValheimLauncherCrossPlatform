using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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

            _settings.Modpack.ExpectedModFiles = new List<string>(apiData.dependencies ?? Array.Empty<string>());
            string localVersion = _settings.Modpack.CurrentLocalVersion;
            bool needsUpdate = localVersion != apiData.versionNumber;

            _updateStatus(needsUpdate ? $"Update auf v.{apiData.versionNumber} verfügbar!" : "Mods sind aktuell.");
            return (apiData.versionNumber, needsUpdate);
        }

        public async Task ForceUpdateModpackAsync()
        {
            _updateStatus("Starte Mod-Update...");
            var apiData = await GetThunderstoreApiData("ImmernDarNew/ImmernDarNew_Modpack");
            if (apiData.downloadUrl == null || apiData.dependencies == null)
            {
                _updateStatus("Fehler: Konnte Modpack-Daten nicht abrufen.");
                return;
            }

            _settings.Modpack.ExpectedModFiles = new List<string>(apiData.dependencies);

            string bepinexPath = Path.Combine(_settings.ValheimInstallPath, "BepInEx");
            await CleanOldModsAsync(bepinexPath, _settings.Modpack.ExpectedModFiles);

            bool success = await DownloadAndExtractModpackAsync(apiData.downloadUrl, bepinexPath);

            if (success)
            {
                _settings.Modpack.CurrentLocalVersion = apiData.versionNumber;
                _saveSettings();
                _updateStatus("Modpack erfolgreich aktualisiert!");
            }
            else
            {
                _updateStatus("Mod-Update mit Fehlern abgeschlossen.");
            }
        }

        private async Task<bool> DownloadAndExtractModpackAsync(string downloadUrl, string extractPath)
        {
            try
            {
                _updateStatus("Lade Modpack herunter...");
                _updateProgress("0");
                string tempZipPath = Path.Combine(Path.GetTempPath(), "modpack.zip"); // Use a consistent temp name

                // --- Start Caching Logic ---
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
                    Debug.WriteLine($"Could not get online file size: {ex.Message}");
                }

                if (File.Exists(tempZipPath) && onlineFileSize > 0)
                {
                    var localFileInfo = new FileInfo(tempZipPath);
                    if (localFileInfo.Length == onlineFileSize)
                    {
                        _updateStatus("Verwende zwischengespeichertes Modpack.");
                        _updateProgress("100");
                        // Skip download, proceed to extraction
                    }
                    else
                    {
                        await DownloadFile(downloadUrl, tempZipPath);
                    }
                }
                else
                {
                    await DownloadFile(downloadUrl, tempZipPath);
                }
                // --- End Caching Logic ---


                _updateStatus("Entpacke Mods...");
                _updateProgress("0"); // Reset for extraction progress if needed, simple for now
                using (var archive = ArchiveFactory.Open(tempZipPath))
                {
                    archive.WriteToDirectory(extractPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                }

                // We keep the zip for caching, so we don't delete it.
                // File.Delete(tempZipPath); 

                return true;
            }
            catch (Exception ex)
            {
                _updateStatus($"Fehler beim Mod-Download: {ex.Message}");
                Debug.WriteLine(ex);
                return false;
            }
        }

        private async Task DownloadFile(string url, string destinationPath)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var readBytes = 0L;

            using (var contentStream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    readBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        double percentage = (double)readBytes / totalBytes * 100;
                        _updateProgress(percentage.ToString("F0", CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        private async Task CleanOldModsAsync(string bepinexPath, List<string> expectedMods)
        {
            _updateStatus("Bereinige alte Mod-Dateien...");
            await Task.Run(() =>
            {
                string pluginsPath = Path.Combine(bepinexPath, "plugins");
                if (!Directory.Exists(pluginsPath)) return;

                // Create a set of expected folder/file names for easy lookup
                var modsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in expectedMods)
                {
                    // Basic conversion from "Author-Mod-Version" to "Author-Mod"
                    int lastDash = mod.LastIndexOf('-');
                    if (lastDash > 0)
                    {
                        // This is a guess; Thunderstore package names can differ from DLL names.
                        // A more robust system would map package names to actual files.
                        // For now, we clean very selectively.
                    }
                }

                // --- SAFER CLEANUP ---
                // Instead of deleting everything, we only delete folders that look like old versions of what we are about to install.
                // The old launcher's logic was very specific. A simpler, safer approach for now:
                // Let the extraction process overwrite files. A dedicated "clean install" button
                // could perform a more aggressive cleanup if needed.

                // For now, we will only delete patchers and core to ensure BepInEx updates correctly.
                var foldersToClean = new[] { "patchers", "core" };
                foreach (var folder in foldersToClean)
                {
                    var fullPath = Path.Combine(bepinexPath, folder);
                    if (Directory.Exists(fullPath))
                    {
                        try
                        {
                            Directory.Delete(fullPath, true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Could not delete old mod folder {fullPath}: {ex.Message}");
                        }
                    }
                }
            });
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
    }
}