using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace UpgradeLimiter
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log = null!;

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony("darkharasho.UpgradeLimiter");
            harmony.PatchAll();
            Log.LogInfo($"UpgradeLimiter v{PluginInfo.PLUGIN_VERSION} loaded.");
        }
    }
}
