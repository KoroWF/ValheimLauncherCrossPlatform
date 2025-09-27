using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ValheimLauncher2.Models.Utils
{
    public static class PlatformUtils
    {
        public static string GetAppConfigFolderPath()
        {
            string folderName = "ValheimImmerndar";
            string configPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: C:\Users\USER\AppData\Local\ValheimImmerndar
                configPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: /home/USER/.config/ValheimImmerndar
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configPath = Path.Combine(home, ".config");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: /Users/USER/Library/Application Support/ValheimImmerndar
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configPath = Path.Combine(home, "Library", "Application Support");
            }
            else
            {
                // Fallback für andere Systeme
                configPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            return Path.Combine(configPath, folderName);
        }

        public static bool TryStartSteam()
        {

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    string? steamPath = GetSteamInstallPath(); // Holt den Ordner
                    if (string.IsNullOrEmpty(steamPath)) return false;

                    string steamExePath = Path.Combine(steamPath, "Steam.exe");
                    if (File.Exists(steamExePath))
                    {
                        Process.Start(new ProcessStartInfo(steamExePath) { UseShellExecute = true });
                        return true;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Unter Linux ist 'steam' normalerweise im Systempfad
                    Process.Start(new ProcessStartInfo("steam") { UseShellExecute = true });
                    return true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Unter macOS ist 'open -a' der Standardweg, um Apps zu starten
                    Process.Start("open", "-a Steam");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not start Steam: {ex.Message}");
                return false;
            }
            return false;
        }

        public static string GetDefaultSystemPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Gibt das Systemlaufwerk zurück, z.B. "C:\"
                return Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Gibt das Home-Verzeichnis des Benutzers zurück (z.B. /home/user oder /Users/user)
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            // Ein neutraler Fallback
            return "/";
        }

        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { /*...*/ }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { /*...*/ }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) { /*...*/ }
                else { throw; }
            }
        }

        public static string GetValheimDataPath(string valheimBasePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { return Path.Combine(valheimBasePath, "valheim_Data"); }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) { return Path.Combine(valheimBasePath, "Valheim.app", "Contents", "Resources", "Data"); }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { return Path.Combine(valheimBasePath, "valheim_Data"); }
            else { throw new PlatformNotSupportedException("Unbekanntes Betriebssystem erkannt."); }
        }

        // DIESE METHODE IST NEU UND ZENTRALISIERT DIE LOGIK
        private static string? FindSteamappsFolderForGame(string valheimInstallPath)
        {
            // Versuch 1: Die schnelle "Rate"-Methode
            if (valheimInstallPath.Contains(Path.Combine("steamapps", "common"), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var commonDir = new DirectoryInfo(Path.GetDirectoryName(valheimInstallPath));
                    if (commonDir.Name.Equals("common", StringComparison.OrdinalIgnoreCase) && commonDir.Parent != null)
                    {
                        return commonDir.Parent.FullName; // Das sollte der steamapps-Ordner sein
                    }
                }
                catch { /* Ignoriere Fehler, falls Pfad ungültig */ }
            }

            // Versuch 2: Die zuverlässige "Lese"-Methode als Fallback
            var libraryPaths = GetSteamLibraryFolders();
            foreach (var path in libraryPaths)
            {
                if (valheimInstallPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(path, "steamapps");
                }
            }

            return null; // Kein passender Ordner gefunden
        }

        // DEINE ALTE GetValheimInstallPath IST JETZT AUFGETEILT UND WIEDERVERWENDBAR
        public static string? GetValheimInstallPath()
        {
            var libraryPaths = GetSteamLibraryFolders();
            foreach (var libraryPath in libraryPaths)
            {
                string valheimPath = Path.Combine(libraryPath, "steamapps", "common", "Valheim");
                if (Directory.Exists(valheimPath))
                {
                    return valheimPath;
                }
            }
            return null;
        }

        private static List<string> GetSteamLibraryFolders()
        {
            var libraryPaths = new List<string>();
            string? steamPath = GetSteamInstallPath();

            if (string.IsNullOrEmpty(steamPath)) return libraryPaths;

            // Haupt-Bibliothek hinzufügen
            libraryPaths.Add(steamPath);

            // Weitere Bibliotheken aus libraryfolders.vdf lesen
            string libraryFoldersVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFoldersVdf))
            {
                string content = File.ReadAllText(libraryFoldersVdf);
                var matches = Regex.Matches(content, @"^\s*""path""\s+""(.+)""\s*$", RegexOptions.Multiline);
                foreach (Match match in matches.Cast<Match>())
                {
                    string path = match.Groups[1].Value.Replace("\\\\", "\\");
                    libraryPaths.Add(path);
                }
            }
            return libraryPaths;
        }

        public static void ModifyBootConfig(string valheimInstallPath)
        {
            try
            {
                // 1. Finde den korrekten Pfad zur boot.config (plattformunabhängig)
                string dataPath = PlatformUtils.GetValheimDataPath(valheimInstallPath);
                string bootConfigPath = "";
                // Prüfe, ob der Code auf einem macOS-System läuft
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // macOS-spezifischer Pfad
                    bootConfigPath = Path.Combine(dataPath, "Valheim.app", "Contents", "Resources", "Data", "boot.config");
                }
                else
                {
                    // Der gemeinsame Pfad für Windows und Linux
                    bootConfigPath = Path.Combine(dataPath, "valheim_data", "boot.config");
                }

                if (!File.Exists(bootConfigPath))
                {
                    Console.WriteLine($"Fehler: boot.config nicht gefunden unter {bootConfigPath}");
                    return;
                }

                // 2. Definiere alle Ziel-Einstellungen in einem Dictionary
                var targetConfig = new Dictionary<string, string>
                {
                    ["wait-for-native-debugger"] = "0",
                    ["hdr-display-enabled"] = "0",
                    ["gc-max-time-slice"] = "11",
                    ["build-guid"] = "15a68b650d674563a51a9eedd7c525ca",
                    ["gfx-enable-gfx-jobs"] = "1",
                    ["gfx-enable-native-gfx-jobs"] = "1",
                    ["vr-enabled"] = "0",
                    ["scripting-runtime-version"] = "latest"
                    // Die Job-Worker-Counts werden dynamisch hinzugefügt
                };

                // 3. Ermittle die Anzahl der Prozessorkerne und füge sie zur Konfiguration hinzu
                // Environment.ProcessorCount gibt die Anzahl der logischen Kerne zurück, was Unity erwartet.
                int coreCount = Environment.ProcessorCount;
                targetConfig["job-worker-maximum-count"] = coreCount.ToString();
                targetConfig["job-worker-count"] = coreCount.ToString();

                // 4. Lese die existierende Konfigurationsdatei
                List<string> lines = File.ReadAllLines(bootConfigPath).ToList();

                // 5. Aktualisiere oder füge jede Zeile aus der Ziel-Konfiguration hinzu
                foreach (var configEntry in targetConfig)
                {
                    string key = configEntry.Key;
                    string value = configEntry.Value;
                    string newLine = $"{key}={value}";

                    // Finde den Index der Zeile, die mit unserem Key beginnt
                    int existingLineIndex = lines.FindIndex(line => line.Trim().StartsWith(key + "="));

                    if (existingLineIndex != -1)
                    {
                        // Wenn die Zeile existiert, ersetze sie
                        lines[existingLineIndex] = newLine;
                        Console.WriteLine($"'{key}' aktualisiert auf '{value}'.");
                    }
                    else
                    {
                        // Wenn die Zeile nicht existiert, füge sie hinzu
                        lines.Add(newLine);
                        Console.WriteLine($"'{key}={value}' hinzugefügt.");
                    }
                }

                // 6. Schreibe die aktualisierte Konfiguration zurück in die Datei
                File.WriteAllLines(bootConfigPath, lines);
                Console.WriteLine("boot.config wurde erfolgreich für optimale Performance konfiguriert.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ein Fehler ist beim Bearbeiten der boot.config aufgetreten: {ex.Message}");
            }
        }

        public static string? GetSteamInstallPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return (string?)Microsoft.Win32.Registry.GetValue("HKEY_CURRENT_USER\\Software\\Valve\\Steam", "SteamPath", null)?.ToString()?.Replace("/", "\\");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string steamRoot = Path.Combine(home, ".local", "share", "Steam");
                if (Directory.Exists(steamRoot)) return steamRoot;
                steamRoot = Path.Combine(home, ".steam", "steam"); // Fallback für ältere Installationen
                if (Directory.Exists(steamRoot)) return steamRoot;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "Steam");
            }
            return null;
        }
    }
}