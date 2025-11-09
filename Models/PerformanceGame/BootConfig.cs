using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValheimLauncher2.Models.Utils;

namespace ValheimLauncher2.Models.PerformanceGame
{
 /// <summary>
 /// Provides functionality to modify the Valheim boot.config file for optimal performance settings.
 /// </summary>
 public class BootConfigModifier
 {
 /// <summary>
 /// Gets the path to the boot.config file.
 /// </summary>
 public readonly string bootConfigPath;

 /// <summary>
 /// Initializes a new instance of the <see cref="BootConfigModifier"/> class using the specified Valheim base path.
 /// </summary>
 /// <param name="valheimBasePath">The base installation path of Valheim.</param>
 /// <exception cref="DirectoryNotFoundException">Thrown if the Valheim data path cannot be determined or found.</exception>
 public BootConfigModifier(string valheimBasePath)
 {
 string valheimDataPath = PlatformUtils.GetValheimDataPath(valheimBasePath);
 if (string.IsNullOrEmpty(valheimDataPath) || !Directory.Exists(valheimDataPath))
 {
 throw new DirectoryNotFoundException($"Valheim data path could not be determined or found from base path: {valheimBasePath}");
 }
 bootConfigPath = Path.Combine(valheimDataPath, "boot.config");
 }

 /// <summary>
 /// Applies optimal performance settings to the boot.config file. Creates or updates the file as necessary.
 /// </summary>
 public void ApplyPerformanceSettings()
 {
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
 int logicalProcessors = Environment.ProcessorCount;
 int workerCount = Math.Max(1, logicalProcessors -1);
 desiredSettings["job-worker-maximum-count"] = workerCount.ToString();
 desiredSettings["job-worker-count"] = workerCount.ToString();
 }
 catch (Exception)
 {

 }

 try
 {
 if (!File.Exists(bootConfigPath))
 {
 File.WriteAllLines(bootConfigPath, desiredSettings.Select(kvp => $"{kvp.Key}={kvp.Value}"));
 return;
 }

 var lines = File.ReadAllLines(bootConfigPath).ToList();
 var existingSettings = new Dictionary<string, string>();
 bool needsUpdate = false;
 var linesToKeep = new List<string>();
 foreach (var line in lines)
 {
 string[] parts = line.Split(new[] { '=' },2);
 if (parts.Length ==2)
 {
 existingSettings[parts[0].Trim()] = parts[1].Trim();
 }
 else
 {
 linesToKeep.Add(line);
 }
 }
 foreach (var setting in desiredSettings)
 {
 if (!existingSettings.ContainsKey(setting.Key) || existingSettings[setting.Key] != setting.Value)
 {
 existingSettings[setting.Key] = setting.Value;
 needsUpdate = true;
 }
 }
 if (needsUpdate)
 {
 linesToKeep.AddRange(existingSettings.Select(kvp => $"{kvp.Key}={kvp.Value}"));
 File.WriteAllLines(bootConfigPath, linesToKeep);
 }
 }
 catch (Exception)
 {

 }
 }
 }
}
