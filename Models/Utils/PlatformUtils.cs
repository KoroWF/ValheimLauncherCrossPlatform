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
            string configBasePath; // Renamed for clarity

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: C:\Users\USER\AppData\Local
                configBasePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: /home/USER/.config
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configBasePath = Path.Combine(home, "VIConfig");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: /Users/USER/Library/Application Support
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configBasePath = Path.Combine(home, "Library", "Application Support");
            }
            else
            {
                // Fallback for other systems
                configBasePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            // Append the app folder ONCE at the end of the respective base path.
            string launcherFolderPath = Path.Combine(configBasePath, "ValheimImmerndar");

            return launcherFolderPath;
        }
        public static bool TryStartSteam()
        {

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    string? steamPath = GetSteamInstallPath(); // Gets the folder
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
                    // On Linux, 'steam' is usually in the system path
                    Process.Start(new ProcessStartInfo("steam") { UseShellExecute = true });
                    return true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // On macOS, 'open -a' is the standard way to start apps
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
                // Returns the system drive, e.g. "C:\"
                return Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Returns the user's home directory (e.g. /home/user or /Users/user)
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            // A neutral fallback
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
            else { throw new PlatformNotSupportedException("Unknown operating system detected."); }
        }

        public static void ModifyBootConfig(string valheimInstallPath)
        {
            try
            {
                //1. Find the correct path to boot.config (platform independent)
                string dataPath = PlatformUtils.GetValheimDataPath(valheimInstallPath);
                string bootConfigPath = "";
                // Check if the code is running on a macOS system
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // macOS-specific path
                    bootConfigPath = Path.Combine(dataPath, "Valheim.app", "Contents", "Resources", "Data", "boot.config");
                }
                else
                {
                    // The common path for Windows and Linux
                    bootConfigPath = Path.Combine(dataPath, "valheim_data", "boot.config");
                }

                if (!File.Exists(bootConfigPath))
                {
                    Console.WriteLine($"Error: boot.config not found at {bootConfigPath}");
                    return;
                }

                //2. Define all target settings in a dictionary
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
                    // The job-worker-counts are added dynamically
                };

                //3. Determine the number of processor cores and add them to the configuration
                // Environment.ProcessorCount returns the number of logical cores, which Unity expects.
                int coreCount = Environment.ProcessorCount;
                targetConfig["job-worker-maximum-count"] = coreCount.ToString();
                targetConfig["job-worker-count"] = coreCount.ToString();

                //4. Read the existing configuration file
                List<string> lines = File.ReadAllLines(bootConfigPath).ToList();

                //5. Update or add each line from the target configuration
                foreach (var configEntry in targetConfig)
                {
                    string key = configEntry.Key;
                    string value = configEntry.Value;
                    string newLine = $"{key}={value}";

                    // Find the index of the line that starts with our key
                    int existingLineIndex = lines.FindIndex(line => line.Trim().StartsWith(key + "="));

                    if (existingLineIndex != -1)
                    {
                        // If the line exists, replace it
                        lines[existingLineIndex] = newLine;
                        Console.WriteLine($"'{key}' updated to '{value}'.");
                    }
                    else
                    {
                        // If the line does not exist, add it
                        lines.Add(newLine);
                        Console.WriteLine($"'{key}={value}' added.");
                    }
                }

                //6. Write the updated configuration back to the file
                File.WriteAllLines(bootConfigPath, lines);
                Console.WriteLine("boot.config was successfully configured for optimal performance.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while editing boot.config: {ex.Message}");
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
                steamRoot = Path.Combine(home, ".steam", "steam"); // Fallback for older installations
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