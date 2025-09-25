using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ValheimLauncher2.Models.Utils
{
    public static class PlatformUtils
    {
        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url.Replace("&", "^&")}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

        public static string GetValheimInstallPath()
        {
            string steamPath = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam");
            }

            if (steamPath == null)
            {
                return null;
            }

            string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

            if (File.Exists(libraryFoldersPath))
            {
                string content = File.ReadAllText(libraryFoldersPath);
                var match = Regex.Match(content, @"""path""\s+""([^""]+)""[^}]*""892970""");
                if (match.Success)
                {
                    string libraryPath = match.Groups[1].Value.Replace("\\\\", "\\");
                    return Path.Combine(libraryPath, "steamapps", "common", "Valheim");
                }
            }
            return null;
        }
        public static string GetValheimDataPath(string valheimBasePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(valheimBasePath, "valheim_Data");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(valheimBasePath, "Valheim.app", "Contents", "Resources", "Data");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(valheimBasePath, "valheim_Data");
            }
            else
            {
                throw new PlatformNotSupportedException("Unbekanntes Betriebssystem erkannt.");
            }
        }

        /// <summary>
        /// Sets or removes the read-only attribute on the Steam manifest file for Valheim to prevent updates.
        /// </summary>
        public static void SetSteamManifestProtection(string valheimInstallPath, bool protect)
        {
            // This feature is most relevant for Windows, but the path logic is cross-platform.
            if (!valheimInstallPath.Contains("steamapps", StringComparison.OrdinalIgnoreCase))
            {
                // Not a steam installation, nothing to do.
                return;
            }

            try
            {
                DirectoryInfo commonDir = new DirectoryInfo(Path.GetDirectoryName(valheimInstallPath));
                DirectoryInfo steamappsDir = commonDir.Parent; // This should be the 'steamapps' directory

                if (steamappsDir == null || !steamappsDir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("Could not find steamapps directory.");
                    return;
                }

                string manifestPath = Path.Combine(steamappsDir.FullName, "appmanifest_892970.acf");

                if (File.Exists(manifestPath))
                {
                    FileAttributes attributes = File.GetAttributes(manifestPath);
                    if (protect)
                    {
                        File.SetAttributes(manifestPath, attributes | FileAttributes.ReadOnly);
                        Debug.WriteLine("Steam manifest file has been write-protected.");
                    }
                    else
                    {
                        File.SetAttributes(manifestPath, attributes & ~FileAttributes.ReadOnly);
                        Debug.WriteLine("Steam manifest file write-protection removed.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't crash the application.
                Debug.WriteLine($"Error setting manifest protection: {ex.Message}");
            }
        }
    }
}