using System.Collections.Generic;

// You can adjust the namespace to your project
namespace ValheimLauncher2.Models.Settings
{
    /// <summary>
    /// Represents the entire structure of the launcher.settings.json file.
    /// </summary>
    public class LauncherSettings
    {
        /// <summary>
        /// Simple property for the Vulkan switch.
        /// </summary>
        public bool VulkanEnabled { get; set; }

        /// <summary>
        /// Contains all settings related to the modpack.
        /// </summary>
        public ModpackSettings Modpack { get; set; }

        /// <summary>
        /// The installation path of Valheim.
        /// </summary>
        public string ValheimInstallPath { get; set; }

        public LauncherSettings() // Constructor for default values
        {
            VulkanEnabled = false;
            Modpack = new ModpackSettings();
            ValheimInstallPath = "-"; // Default value for the installation path
        }

    }

    /// <summary>
    /// Represents the "Modpack" object within the JSON file.
    /// </summary>
    public class ModpackSettings
    {
        public string CurrentLocalVersion { get; set; }

        // 'object?' is flexible here in case the API response is more complex or can be null.
        public object? LastFetchedThunderstoreApiResponse { get; set; }

        public List<string> ExpectedModFiles { get; set; }
    }
}