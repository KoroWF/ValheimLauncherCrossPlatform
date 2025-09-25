using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ValheimLauncher2.Models.Settings
{
    public class LauncherSettings
    {
        public bool VulkanEnabled { get; set; }
        public ModpackState Modpack { get; set; }
        public string ValheimInstallPath { get; set; }
        public LaunchOptions LaunchOptions { get; set; } // Neue Eigenschaft

        public LauncherSettings()
        {
            VulkanEnabled = false;
            Modpack = new ModpackState();
            ValheimInstallPath = "notgiven";
            LaunchOptions = new LaunchOptions(); // Initialisiere die neue Klasse
        }
    }

    public class ModpackState
    {
        public string CurrentLocalVersion { get; set; }
        public JObject LastFetchedThunderstoreApiResponse { get; set; }
        public List<string> ExpectedModFiles { get; set; }

        public ModpackState()
        {
            CurrentLocalVersion = "0.0.0";
            ExpectedModFiles = new List<string>();
            LastFetchedThunderstoreApiResponse = null;
        }
    }

    public class LaunchOptions
    {
        public bool UseVulkan { get; set; }
        public bool UseForceD3D11 { get; set; }
        public bool UseExclusiveFullscreen { get; set; }
        public List<string> AdditionalArguments { get; set; } // Für benutzerdefinierte Argumente

        public LaunchOptions()
        {
            UseVulkan = false;
            UseForceD3D11 = false;
            UseExclusiveFullscreen = false;
            AdditionalArguments = new List<string>();
        }
    }
}