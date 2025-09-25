using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValheimLauncher2.Models.Utils;

namespace ValheimLauncher2.Models.PerformanceGame
{
    public class BootConfigModifier
    {
        public readonly string bootConfigPath;

        // The constructor now uses PlatformUtils to determine the correct data path for any OS.
        public BootConfigModifier(string valheimBasePath)
        {
            // Use PlatformUtils to get the correct data path for the current OS
            string valheimDataPath = PlatformUtils.GetValheimDataPath(valheimBasePath);

            if (string.IsNullOrEmpty(valheimDataPath) || !Directory.Exists(valheimDataPath))
            {
                // If the path is not found, we can't proceed with the modification.
                throw new DirectoryNotFoundException($"Valheim data path could not be determined or found from base path: {valheimBasePath}");
            }
            bootConfigPath = Path.Combine(valheimDataPath, "boot.config");
        }

        public void ApplyPerformanceSettings()
        {
            // These are the desired settings that should be in the file.
            var desiredSettings = new Dictionary<string, string>
            {
                { "gfx-enable-gfx-jobs", "1" },
                { "gfx-enable-native-gfx-jobs", "1" },
                { "gc-max-time-slice", "11" },
                { "vr-enabled", "0" },
                { "scripting-runtime-version", "latest" }
            };

            try
            {
                // Safely get the number of logical processors.
                int logicalProcessors = Environment.ProcessorCount;

                // Calculate the optimal number of worker threads (N-1 rule, but at least 1).
                int workerCount = Math.Max(1, logicalProcessors - 1);

                Console.WriteLine($"System has {logicalProcessors} logical processors. Setting worker threads to {workerCount}.");

                // Add the dynamic worker count to the desired settings.
                desiredSettings["job-worker-maximum-count"] = workerCount.ToString();
                desiredSettings["job-worker-count"] = workerCount.ToString();
            }
            catch (Exception ex)
            {
                // Catch rare errors if the processor count cannot be determined.
                Console.WriteLine($"Could not determine CPU data, using default values. Error: {ex.Message}");
            }

            // Edit or create the BootConfig.
            try
            {
                // If boot.config does not exist, create it with the desired settings.
                if (!File.Exists(bootConfigPath))
                {
                    Console.WriteLine("boot.config not found! Creating new file...");
                    File.WriteAllLines(bootConfigPath, desiredSettings.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    Console.WriteLine("New boot.config created successfully.");
                    return;
                }

                // Read the existing boot.config.
                var lines = File.ReadAllLines(bootConfigPath).ToList();
                var existingSettings = new Dictionary<string, string>();
                bool needsUpdate = false;

                var linesToKeep = new List<string>();
                foreach (var line in lines)
                {
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        existingSettings[parts[0].Trim()] = parts[1].Trim();
                    }
                    else
                    {
                        linesToKeep.Add(line); // Keep empty lines or comments.
                    }
                }

                // Compare and update the settings.
                foreach (var setting in desiredSettings)
                {
                    if (!existingSettings.ContainsKey(setting.Key) || existingSettings[setting.Key] != setting.Value)
                    {
                        existingSettings[setting.Key] = setting.Value;
                        needsUpdate = true;
                    }
                }

                // Write the file only if there were changes.
                if (needsUpdate)
                {
                    Console.WriteLine("Updating boot.config...");
                    linesToKeep.AddRange(existingSettings.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    File.WriteAllLines(bootConfigPath, linesToKeep);
                    Console.WriteLine("boot.config updated successfully.");
                }
                else
                {
                    Console.WriteLine("boot.config is already configured correctly.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while editing boot.config: {ex.Message}");
                // Optionally, you can re-throw the exception to cancel the startup process.
                // throw;
            }
        }
    }
}
