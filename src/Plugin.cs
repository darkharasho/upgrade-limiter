using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace UpgradeLimiter
{
    internal class UpgradeEntry
    {
        public string Name = "";              // e.g. "Health"
        public MethodInfo Method = null!;     // StatsManager.UpgradePlayerHealth(string)
        public FieldInfo CountField = null!;  // StatsManager.playerUpgradeHealth (Dictionary<string,int>)
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

        private void Awake()
        {
            Log = Logger;
            UpgradeRegistry.Discover();
            var harmony = new Harmony("darkharasho.UpgradeLimiter");
            harmony.PatchAll();
            Log.LogInfo($"UpgradeLimiter v{PluginInfo.PLUGIN_VERSION} loaded.");
        }
    }
}
