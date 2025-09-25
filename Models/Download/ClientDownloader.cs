using Avalonia.Threading;
using HtmlAgilityPack;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ValheimLauncher2.Models.PerformanceGame; // Import the new namespace

namespace ValheimLauncher2.Models.Download
{
    public class ClientDownloader
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly Action<string> _updateStatusAction;
        private readonly Action<double> _updateProgressAction;

        public ClientDownloader(Action<string> updateStatus, Action<double> updateProgress)
        {
            _updateStatusAction = updateStatus;
            _updateProgressAction = updateProgress;
        }

        public async Task InstallGameAsync(string installPath)
        {
            try
            {
                // This is where the entire logic from InstallGame_Click and the called methods will go.
                // UI-specific actions (changing visibility) will remain in the ViewModel!
                _updateStatusAction("Installiere das Hauptspiel...");
                await installer(installPath);
                _updateStatusAction("Das Hauptspiel wurde fertig geladen.");

            }
            catch (Exception ex)
            {
                _updateStatusAction($"Fehler bei der Installation: {ex.Message}");

            }
        }

        public async Task FixValheimAsync(string installPath)
        {
            try
            {
                _updateStatusAction("Überprüfe vorhandene Daten auf Fehler!");
                // The logic for the repair (GameInstallProgress) will be moved here.
                // Since I don't have the code for GameInstallProgress, here is a placeholder.
                // It now calls deleteFolder and then installer.
                await deleteFolder(installPath);
                await installer(installPath);
                _updateStatusAction("Überprüfung beendet, bereit zum Starten!");
            }
            catch (Exception ex)
            {
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
                    try
                    {
                        Directory.Delete(fullPath, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        // Error handling
                        throw new IOException($"Fehler beim Löschen von {fullPath}", ex);
                    }
                }
            }
        }

        public async Task installer(string installPath)
        {
            string serverUri = "https://www.immerndar.de/ValheimWithBepInEx/";
            _updateStatusAction("Lade Spieldateien herunter...");

            // Update UI here via callback
            Dispatcher.UIThread.Invoke(() => _updateProgressAction(0.0));

            try
            {
                // Delete the old zip file
                string zipFilePath = Path.Combine(installPath, "ValheimWithBepInEx.zip");
                if (File.Exists(zipFilePath))
                {
                    File.Delete(zipFilePath);
                }

                long totalSize = await GetTotalSizeFromServer(_httpClient);
                await DownloadDirectoryAsync(_httpClient, serverUri, installPath, totalSize);
            }
            catch (Exception ex)
            {
                _updateStatusAction($"Fehler beim Download: {ex.Message}");
                throw; // Throw the exception so it can be handled in the calling task
            }

            // NEW: Apply performance settings after installation is complete.
            try
            {
                _updateStatusAction("Optimiere Start-Konfiguration...");
                BootConfigModifier configModifier = new BootConfigModifier(installPath);
                configModifier.ApplyPerformanceSettings();
            }
            catch (Exception ex)
            {
                // Log the error, but don't stop the whole process.
                // The UI will get the message via the status action.
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
                if (long.TryParse(sizeString, out var result))
                {
                    return result;
                }
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
            HtmlDocument htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);

            // This variable needs to track the total download progress across all files.
            // It must be declared outside the loop.
            long downloadedSize = 0L;

            var filesToDownload = htmlDocument.DocumentNode.SelectNodes("//a[@href]")
                ?.Where(node => !node.InnerText.Contains("[To Parent Directory]"))
                .ToList();

            if (filesToDownload == null || !filesToDownload.Any())
            {
                _updateStatusAction("Keine Dateien zum Herunterladen gefunden.");
                return;
            }

            foreach (HtmlNode item in filesToDownload)
            {
                string relativePath = item.GetAttributeValue("href", string.Empty);
                Uri fullUri = new Uri(new Uri(serverUri), relativePath);
                string localPath = Path.Combine(localBasePath, relativePath.TrimStart('/'));

                if (relativePath.EndsWith('/'))
                {
                    Directory.CreateDirectory(localPath);
                    await DownloadDirectoryAsync(httpClient, fullUri.ToString(), localPath, totalSize);
                    continue;
                }

                try
                {
                    using HttpResponseMessage response = await httpClient.GetAsync(fullUri, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long? contentLength = response.Content.Headers.ContentLength;
                    byte[] buffer = new byte[8192];

                    _updateStatusAction($"Lade herunter: {Path.GetFileName(localPath)}");

                    using (FileStream fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
                    {
                        using Stream responseStream = await response.Content.ReadAsStreamAsync();
                        int bytesRead;
                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedSize += bytesRead;

                            // Correct logic for updating progress
                            if (totalSize > 0)
                            {
                                double progressPercentage = (double)downloadedSize / totalSize * 100;
                                Dispatcher.UIThread.Invoke(() => _updateProgressAction(progressPercentage));
                            }
                        }
                    }

                    if (Path.GetExtension(localPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        await ExtractAndMoveZipAsync(localPath);
                    }
                }
                catch (Exception ex)
                {
                    _updateStatusAction($"Fehler beim Herunterladen von {Path.GetFileName(localPath)}: {ex.Message}");
                    throw;
                }
            }
        }

        private async Task ExtractAndMoveZipAsync(string zipFilePath)
        {
            _updateStatusAction("Extrahiere die Zip Datei...");
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

                _updateStatusAction("Verschiebe die extrahierten Daten...");
                string[] files = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    string relativePath = file.Substring(tempDir.Length + 1);
                    string destinationPath = Path.Combine(Path.GetDirectoryName(zipFilePath), relativePath);

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.Move(file, destinationPath, true);
                }

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
    }
}
