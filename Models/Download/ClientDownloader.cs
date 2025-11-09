using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using HtmlAgilityPack;
using SharpCompress.Archives;
using SharpCompress.Common;
using ValheimLauncher2.Models.PerformanceGame;

namespace ValheimLauncher2.Models.Download
{
    public class ClientDownloader
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly Action<string> _updateStatusAction;
        private readonly Action<double> _updateProgressAction;
        private long _totalBytesDownloaded;

        public ClientDownloader(Action<string> updateStatus, Action<double> updateProgress)
        {
            _updateStatusAction = updateStatus;
            _updateProgressAction = updateProgress;
        }

        public async Task InstallGameAsync(string installPath)
        {
            try
            {
                _updateStatusAction("Installiere das Hauptspiel...");
                await installer(installPath);
                _updateStatusAction("Das Hauptspiel wurde fertig geladen.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FATALER FEHLER in InstallGameAsync: {ex}");
                _updateStatusAction($"Fehler bei der Installation: {ex.Message}");
            }
        }

        public async Task FixValheimAsync(string installPath)
        {
            try
            {
                _updateStatusAction("Überprüfe vorhandene Daten auf Fehler!");
                await deleteFolder(installPath);
                await installer(installPath);
                _updateStatusAction("Überprüfung beendet, bereit zum Starten!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FATALER FEHLER in FixValheimAsync: {ex}");
                _updateStatusAction($"Fehler bei der Reparatur: {ex.Message}");
            }
        }

        private async Task deleteFolder(string baseDirectory)
        {
            
            string[] foldersToDelete = { "BepInEx/patchers", "BepInEx/config/Azumatt.MinimalUI_Backgrounds", "BepInEx/config/Intermission", "BepInEx/config/Seasonality", "valheim_Data" };
            foreach (string path in foldersToDelete)
            {
                string fullPath = Path.Combine(baseDirectory, path);
                if (Directory.Exists(fullPath))
                {
                    try { Directory.Delete(fullPath, recursive: true); }
                    catch (Exception ex) { throw new IOException($"Fehler beim Löschen von {fullPath}", ex); }
                }
            }
        }

        public async Task installer(string installPath)
        {
            string serverUri = string.Empty;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                serverUri = "https://www.immerndar.de/ValheimWithBepInEx/Windows/";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                serverUri = "https://www.immerndar.de/ValheimWithBepInEx/Linux/";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                serverUri = "https://www.immerndar.de/ValheimWithBepInEx/Mac/";
            }

            if (string.IsNullOrEmpty(serverUri))
            {
                _updateStatusAction("Fehler: Unbekanntes Betriebssystem.");
                return;
            }

            _updateStatusAction("Lade Spieldateien herunter...");
            Dispatcher.UIThread.Invoke(() => _updateProgressAction(0.0));
            _totalBytesDownloaded = 0L;

            try
            {
                string zipFilePath = Path.Combine(installPath, "ValheimWithBepInEx.zip");
                if (File.Exists(zipFilePath)) File.Delete(zipFilePath);

                long totalSize = await GetTotalSizeFromServer(_httpClient);
                await DownloadDirectoryAsync(_httpClient, serverUri, installPath, totalSize);
            }
            catch (Exception ex)
            {
                _updateStatusAction($"Fehler beim Download: {ex.Message}");
                throw;
            }

            try
            {
                _updateStatusAction("Optimiere Start-Konfiguration...");
                var configModifier = new BootConfigModifier(installPath);
                configModifier.ApplyPerformanceSettings();
            }
            catch (Exception ex)
            {
                _updateStatusAction($"Fehler bei boot.config Optimierung: {ex.Message}");
            }

            Dispatcher.UIThread.Invoke(() => _updateProgressAction(100));
        }

        private async Task<long> GetTotalSizeFromServer(HttpClient httpClient)
        {
            string requestUri = "https://www.immerndar.de/gesamtgroesse.txt";
            try
            {
                string sizeString = await httpClient.GetStringAsync(requestUri);
                if (long.TryParse(sizeString, out var result)) return result;
                _updateStatusAction("Fehler: Konnte Gesamtgröße nicht lesen.");
                return 0L;
            }
            catch (Exception ex)
            {
                _updateStatusAction($"Fehler beim Abrufen der Dateigröße: {ex.Message}");
                return 0L;
            }
        }

        public async Task DownloadDirectoryAsync(HttpClient httpClient, string serverUri, string localBasePath, long totalSize)
        {
            string html = await httpClient.GetStringAsync(serverUri);
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);

            var filesToDownload = htmlDocument.DocumentNode.SelectNodes("//a[@href]")
                ?.Where(node => !node.InnerText.Contains("[To Parent Directory]"))
                .ToList();

            if (filesToDownload == null || !filesToDownload.Any())
            {
                _updateStatusAction("Keine Dateien zum Herunterladen gefunden.");
                return;
            }

            var baseUri = new Uri(serverUri);

            foreach (HtmlNode item in filesToDownload)
            {
                string href = item.GetAttributeValue("href", string.Empty);
                var fullUri = new Uri(baseUri, href);

                string relativePath = baseUri.MakeRelativeUri(fullUri).ToString();
                relativePath = Uri.UnescapeDataString(relativePath);

                if (relativePath.EndsWith('/'))
                {
                    string newLocalPath = Path.Combine(localBasePath, relativePath.Trim('/'));
                    Directory.CreateDirectory(newLocalPath);
                    await DownloadDirectoryAsync(httpClient, fullUri.ToString(), newLocalPath, totalSize);
                    continue;
                }

                string localPath = Path.Combine(localBasePath, relativePath);

                try
                {
                    using (var response = await httpClient.GetAsync(fullUri, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        _updateStatusAction($"Lade herunter: {Path.GetFileName(localPath)}");
                        Directory.CreateDirectory(Path.GetDirectoryName(localPath));

                        using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            using (Stream responseStream = await response.Content.ReadAsStreamAsync())
                            {
                                var buffer = new byte[81920];
                                int bytesRead;
                                while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    _totalBytesDownloaded += bytesRead;

                                    if (totalSize > 0)
                                    {
                                        double progressPercentage = (double)_totalBytesDownloaded / totalSize * 100;
                                        Dispatcher.UIThread.Invoke(() => _updateProgressAction(progressPercentage));
                                    }
                                }
                            }
                        }
                    }

                    await Task.Delay(100);

                    if (Path.GetExtension(localPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractAndMoveZip(localPath);
                    }
                }
                catch (Exception ex)
                {
                    _updateStatusAction($"Fehler beim Herunterladen von {Path.GetFileName(localPath)}: {ex.Message}");
                    throw;
                }
            }
        }

        private void ExtractAndMoveZip(string zipFilePath)
        {
            _updateStatusAction("Extrahiere Spieldateien...");
            string tempDir = Path.Combine(Path.GetDirectoryName(zipFilePath), "ValheimWithBepInExTemp");
            try
            {
                Directory.CreateDirectory(tempDir);
                using (var archive = ArchiveFactory.Open(zipFilePath))
                {
                    foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        entry.WriteToDirectory(tempDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                    }
                }
                _updateStatusAction("Verschiebe extrahierte Daten...");
                MergeDirectory(tempDir, Path.GetDirectoryName(zipFilePath));
                _updateStatusAction("Räume auf...");
                Directory.Delete(tempDir, recursive: true);
                File.Delete(zipFilePath);
            }
            catch (Exception ex)
            {
                _updateStatusAction($"Fehler beim Entpacken oder Verschieben: {ex.Message}");
                throw;
            }
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
    }
}