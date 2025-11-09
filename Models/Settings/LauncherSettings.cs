using System.Collections.Generic;

namespace ValheimLauncher2.Models.Settings
{
    /// <summary>
    /// Represents the structure of the launcher settings configuration.
    /// </summary>
    public class LauncherSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether Vulkan is enabled.
        /// </summary>
        public bool VulkanEnabled { get; set; }

        /// <summary>
        /// Gets or sets the modpack-related settings.
        /// </summary>
        public ModpackSettings Modpack { get; set; }

        /// <summary>
        /// Gets or sets the installation path of Valheim.
        /// </summary>
        public string ValheimInstallPath { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="LauncherSettings"/> class with default values.
        /// </summary>
        public LauncherSettings()
        {
            VulkanEnabled = false;
            Modpack = new ModpackSettings();
            ValheimInstallPath = "-";
        }
    }

    /// <summary>
    /// Represents the modpack configuration within the launcher settings.
    /// </summary>
    public class ModpackSettings
    {
        /// <summary>
        /// Gets or sets the current local version of the modpack.
        /// </summary>
        public string CurrentLocalVersion { get; set; }

        /// <summary>
        /// Gets or sets the last fetched Thunderstore API response.
        /// </summary>
        public object? LastFetchedThunderstoreApiResponse { get; set; }

        /// <summary>
        /// Gets or sets the list of expected mod files.
        /// </summary>
        public List<string> ExpectedModFiles { get; set; }
    }
}