using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ValheimLauncher2.Models.Utils
{
    /// <summary>
    /// Provides platform-specific utility methods for file paths, launching applications, and configuration management.
    /// </summary>
    public static class PlatformUtils
    {
        /// <summary>
        /// Gets the application configuration folder path for the current platform.
        /// </summary>
        /// <returns>The configuration folder path.</returns>
        public static string GetAppConfigFolderPath()
        {
            string configBasePath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                configBasePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configBasePath = Path.Combine(home, "VIConfig");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configBasePath = Path.Combine(home, "Library", "Application Support");
            }
            else
            {
                configBasePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            string launcherFolderPath = Path.Combine(configBasePath, "ValheimImmerndar");
            return launcherFolderPath;
        }

        /// <summary>
        /// Attempts to start the Steam client on the current platform.
        /// </summary>
        /// <returns>True if Steam was started successfully; otherwise, false.</returns>
        public static bool TryStartSteam()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    string? steamPath = GetSteamInstallPath();
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
                    Process.Start(new ProcessStartInfo("steam") { UseShellExecute = true });
                    return true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
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

        /// <summary>
        /// Gets the default system path for the current platform.
        /// </summary>
        /// <returns>The default system path.</returns>
        public static string GetDefaultSystemPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            return "/";
        }

        /// <summary>
        /// Opens the specified URL in the default browser.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) { }
                else { throw; }
            }
        }

        /// <summary>
        /// Gets the Valheim data path for the current platform.
        /// </summary>
        /// <param name="valheimBasePath">The base installation path of Valheim.</param>
        /// <returns>The data path for Valheim.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown if the operating system is not supported.</exception>
        public static string GetValheimDataPath(string valheimBasePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { return Path.Combine(valheimBasePath, "valheim_Data"); }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) { return Path.Combine(valheimBasePath, "Valheim.app", "Contents", "Resources", "Data"); }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { return Path.Combine(valheimBasePath, "valheim_Data"); }
            else { throw new PlatformNotSupportedException("Unknown operating system detected."); }
        }

        /// <summary>
        /// Modifies the boot.config file for optimal performance settings.
        /// </summary>
        /// <param name="valheimInstallPath">The installation path of Valheim.</param>
        public static void ModifyBootConfig(string valheimInstallPath)
        {
            try
            {
                string dataPath = PlatformUtils.GetValheimDataPath(valheimInstallPath);
                string bootConfigPath = "";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    bootConfigPath = Path.Combine(dataPath, "Valheim.app", "Contents", "Resources", "Data", "boot.config");
                }
                else
                {
                    bootConfigPath = Path.Combine(dataPath, "valheim_data", "boot.config");
                }
                if (!File.Exists(bootConfigPath))
                {
                    Console.WriteLine($"Error: boot.config not found at {bootConfigPath}");
                    return;
                }
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
                };
                int coreCount = Environment.ProcessorCount;
                targetConfig["job-worker-maximum-count"] = coreCount.ToString();
                targetConfig["job-worker-count"] = coreCount.ToString();
                List<string> lines = File.ReadAllLines(bootConfigPath).ToList();
                foreach (var configEntry in targetConfig)
                {
                    string key = configEntry.Key;
                    string value = configEntry.Value;
                    string newLine = $"{key}={value}";
                    int existingLineIndex = lines.FindIndex(line => line.Trim().StartsWith(key + "="));
                    if (existingLineIndex != -1)
                    {
                        lines[existingLineIndex] = newLine;
                        Console.WriteLine($"'{key}' updated to '{value}'.");
                    }
                    else
                    {
                        lines.Add(newLine);
                        Console.WriteLine($"'{key}={value}' added.");
                    }
                }
                File.WriteAllLines(bootConfigPath, lines);
                Console.WriteLine("boot.config was successfully configured for optimal performance.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while editing boot.config: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the Steam installation path for the current platform.
        /// </summary>
        /// <returns>The Steam installation path, or null if not found.</returns>
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
                steamRoot = Path.Combine(home, ".steam", "steam");
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