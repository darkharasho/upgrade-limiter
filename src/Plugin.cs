using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace UpgradeLimiter
{
    internal class UpgradeEntry
    {
        public string Name = "";
        public MethodInfo Method = null!;
        public FieldInfo CountField = null!;
        public ConfigEntry<bool> Enabled = null!;
        public ConfigEntry<int> MaxStacks = null!;

        // Runtime-active values. Equal to local config when not synced; overwritten by host pull.
        public bool ActiveEnabled;
        public int ActiveMax;
    }

    internal static class UpgradeRegistry
    {
        internal static readonly List<UpgradeEntry> Entries = new();
        internal static readonly Dictionary<MethodBase, UpgradeEntry> ByMethod = new();

        public static void Discover()
        {
            Entries.Clear();
            ByMethod.Clear();

            var sm = AccessTools.TypeByName("StatsManager");
            if (sm == null)
            {
                Plugin.Log.LogError("[Discover] StatsManager type not found — mod will be inactive.");
                return;
            }

            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var dictFields = new Dictionary<string, FieldInfo>();
            foreach (var f in sm.GetFields(BF))
            {
                if (!typeof(System.Collections.IDictionary).IsAssignableFrom(f.FieldType)) continue;
                var args = f.FieldType.IsGenericType ? f.FieldType.GetGenericArguments() : null;
                if (args == null || args.Length != 2) continue;
                if (args[0] != typeof(string) || args[1] != typeof(int)) continue;
                dictFields[f.Name.ToLowerInvariant()] = f;
            }

            foreach (var m in sm.GetMethods(BF))
            {
                if (m.ReturnType != typeof(void)) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType != typeof(string)) continue;

                string? upgradeName = null;
                if (m.Name.StartsWith("UpgradePlayer", StringComparison.Ordinal))
                    upgradeName = m.Name.Substring("UpgradePlayer".Length);
                else if (m.Name.StartsWith("PlayerUpgrade", StringComparison.Ordinal))
                    upgradeName = m.Name.Substring("PlayerUpgrade".Length);
                if (string.IsNullOrEmpty(upgradeName)) continue;

                if (upgradeName.Contains("Set") || upgradeName.Contains("Load") ||
                    upgradeName.Contains("Get") || upgradeName.Contains("Apply") ||
                    upgradeName.Contains("Sync"))
                    continue;

                var key = ("playerUpgrade" + upgradeName).ToLowerInvariant();
                if (!dictFields.TryGetValue(key, out var dictField))
                {
                    Plugin.Log.LogWarning($"[Discover] {m.Name} has no matching dictionary {key} — skipping.");
                    continue;
                }

                var entry = new UpgradeEntry { Name = upgradeName, Method = m, CountField = dictField };
                Entries.Add(entry);
                ByMethod[m] = entry;
                Plugin.Log.LogInfo($"[Discover] {m.Name} ↔ {dictField.Name}");
            }

            if (Entries.Count == 0)
                Plugin.Log.LogError("[Discover] No upgrade methods discovered — mod will be inactive.");
            else
                Plugin.Log.LogInfo($"[Discover] Found {Entries.Count} upgrade methods.");
        }
    }

    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log = null!;
        internal static ConfigEntry<bool> SyncToClients = null!;

        private void Awake()
        {
            Log = Logger;

            SyncToClients = Config.Bind("Sync", "SyncToClients", true,
                "Host-only. When true, the host pushes its limits to every client via Photon room properties. " +
                "When false, the host never publishes; each client uses its own local config.");

            UpgradeRegistry.Discover();
            BindUpgradeConfigs();
            ResetActiveToLocal();

            var harmony = new Harmony("darkharasho.UpgradeLimiter");
            harmony.PatchAll();

            Log.LogInfo($"UpgradeLimiter v{PluginInfo.PLUGIN_VERSION} loaded.");
        }

        private void BindUpgradeConfigs()
        {
            var range = new AcceptableValueRange<int>(0, 99);
            foreach (var e in UpgradeRegistry.Entries)
            {
                string section = "Limits." + e.Name;
                e.Enabled = Config.Bind(section, "Enabled", false,
                    $"Enable the cap for the {e.Name} upgrade. When false, the upgrade behaves vanilla.");
                e.MaxStacks = Config.Bind(section, "MaxStacks", 5,
                    new ConfigDescription(
                        $"Maximum number of {e.Name} upgrades a single player may stack. " +
                        "0 means no further upgrades can be picked up.",
                        range));

                // Capture loop variable for the lambda.
                var entry = e;
                entry.Enabled.SettingChanged += (_, _) => entry.ActiveEnabled = entry.Enabled.Value;
                entry.MaxStacks.SettingChanged += (_, _) => entry.ActiveMax = entry.MaxStacks.Value;
            }
        }

        internal static void ResetActiveToLocal()
        {
            foreach (var e in UpgradeRegistry.Entries)
            {
                e.ActiveEnabled = e.Enabled.Value;
                e.ActiveMax = e.MaxStacks.Value;
            }
        }
    }
}
