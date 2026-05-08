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
        public MethodInfo? Method;
        public FieldInfo? CountField;
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

        // Canonical base-game upgrades — names match the post-prefix-strip output of discovery
        // (so they line up with config sections when discovery succeeds). These are bound in
        // the config regardless of whether discovery pairs them with an enforceable method+dict;
        // for unpaired entries the section still appears so the user can see + configure them,
        // but the cap won't enforce until a future game version exposes the matching method/dict.
        internal static readonly string[] CanonicalUpgrades =
        {
            "PlayerHealth",
            "PlayerEnergy",
            "PlayerSpeed",
            "PlayerSprintSpeed",
            "PlayerExtraJump",
            "PlayerGrabStrength",
            "PlayerGrabRange",
            "PlayerGrabThrow",
            "PlayerTumbleLaunch",
            "PlayerTumbleClimb",
            "PlayerTumbleWings",
            "PlayerCrouchRest",
            "PlayerMapPlayerCount",
        };

        public static void Discover()
        {
            Entries.Clear();
            ByMethod.Clear();

            // Step 1: reflection scan → local dict keyed by upgrade name
            var discovered = new Dictionary<string, (MethodInfo Method, FieldInfo CountField)>(StringComparer.Ordinal);

            var sm = AccessTools.TypeByName("StatsManager");
            if (sm == null)
            {
                Plugin.Log.LogError("[Discover] StatsManager type not found — all canonical entries unenforceable.");
            }
            else
            {
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

                    var key = ("playerUpgrade" + upgradeName).ToLowerInvariant();
                    if (!dictFields.TryGetValue(key, out var dictField))
                    {
                        Plugin.Log.LogWarning($"[Discover] {m.Name} has no matching dictionary {key} — skipping.");
                        continue;
                    }

                    discovered[upgradeName] = (m, dictField);
                    Plugin.Log.LogInfo($"[Discover] {m.Name} ↔ {dictField.Name}");
                }
            }

            // Step 2: emit canonical entries in canonical order
            var canonicalSet = new HashSet<string>(CanonicalUpgrades, StringComparer.Ordinal);
            foreach (var name in CanonicalUpgrades)
            {
                UpgradeEntry entry;
                if (discovered.TryGetValue(name, out var pair))
                {
                    entry = new UpgradeEntry { Name = name, Method = pair.Method, CountField = pair.CountField };
                    ByMethod[pair.Method] = entry;
                }
                else
                {
                    entry = new UpgradeEntry { Name = name };
                    Plugin.Log.LogWarning($"[Discover] Canonical upgrade {name} not found — config bound but cap won't enforce.");
                }
                Entries.Add(entry);
            }

            // Step 3: add any discovered upgrades not in the canonical list
            foreach (var kvp in discovered)
            {
                if (canonicalSet.Contains(kvp.Key)) continue;
                var entry = new UpgradeEntry { Name = kvp.Key, Method = kvp.Value.Method, CountField = kvp.Value.CountField };
                ByMethod[kvp.Value.Method] = entry;
                Entries.Add(entry);
                Plugin.Log.LogInfo($"[Discover] Non-canonical {kvp.Key} added to config.");
            }

            Plugin.Log.LogInfo($"[Discover] {Entries.Count} entries total ({ByMethod.Count} enforceable).");
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

            gameObject.AddComponent<SettingsSyncer>();

            // Host-side: when SyncToClients toggles or any limit changes mid-game, re-push.
            SyncToClients.SettingChanged += (_, _) => SettingsSyncer.Instance?.PushHostSettingsExternal();

            var harmony = new Harmony("darkharasho.UpgradeLimiter");
            harmony.PatchAll();
            var prefix = new HarmonyMethod(typeof(CapPrefix).GetMethod(nameof(CapPrefix.Prefix)));
            foreach (var e in UpgradeRegistry.Entries)
            {
                if (e.Method == null) continue;
                try
                {
                    harmony.Patch(e.Method, prefix: prefix);
                    Log.LogInfo($"[Patch] Installed cap prefix on {e.Method.Name}");
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[Patch] Failed to patch {e.Method.Name}: {ex.GetType().Name} {ex.Message}");
                }
            }

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
                entry.Enabled.SettingChanged += (_, _) =>
                {
                    entry.ActiveEnabled = entry.Enabled.Value;
                    if (Photon.Pun.PhotonNetwork.InRoom && Photon.Pun.PhotonNetwork.IsMasterClient)
                        SettingsSyncer.Instance?.PushHostSettingsExternal();
                };
                entry.MaxStacks.SettingChanged += (_, _) =>
                {
                    entry.ActiveMax = entry.MaxStacks.Value;
                    if (Photon.Pun.PhotonNetwork.InRoom && Photon.Pun.PhotonNetwork.IsMasterClient)
                        SettingsSyncer.Instance?.PushHostSettingsExternal();
                };
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

    internal class SettingsSyncer : UnityEngine.MonoBehaviour
    {
        internal static SettingsSyncer? Instance;

        private bool _wasInRoom;
        private bool _wasMaster;
        private float _pollDelay;

        // Cache last-pushed values so SettingChanged spam (REPOConfig autosave fires identical
        // change events repeatedly) doesn't broadcast Photon updates every few seconds.
        private readonly Dictionary<string, (bool en, int max)> _lastPushed = new();

        private void Awake() => Instance = this;
        private void Start() => Plugin.Log.LogInfo("[Sync] SettingsSyncer ready (polling mode)");

        private void Update()
        {
            bool inRoom = Photon.Pun.PhotonNetwork.InRoom;
            bool master = inRoom && Photon.Pun.PhotonNetwork.IsMasterClient;

            if (inRoom && !_wasInRoom)
            {
                if (master) PushHostSettings();
                else        PullHostSettings();
            }
            else if (!inRoom && _wasInRoom)
            {
                Plugin.ResetActiveToLocal();
                Plugin.Log.LogInfo("[Sync] Left room — reset to local config");
            }
            else if (inRoom && master && !_wasMaster)
            {
                PushHostSettings();
            }
            else if (inRoom && !master)
            {
                _pollDelay -= UnityEngine.Time.unscaledDeltaTime;
                if (_pollDelay <= 0f) { _pollDelay = 1f; PullHostSettings(); }
            }

            _wasInRoom = inRoom;
            _wasMaster = master;
        }

        internal void PushHostSettingsExternal()
        {
            if (!Photon.Pun.PhotonNetwork.InRoom || !Photon.Pun.PhotonNetwork.IsMasterClient) return;
            PushHostSettings();
        }

        private void PushHostSettings()
        {
            if (Photon.Pun.PhotonNetwork.CurrentRoom == null) return;
            if (!Plugin.SyncToClients.Value)
            {
                // Host opted out — make sure we don't leave stale keys behind from a prior session.
                // Photon doesn't expose a clean "delete key" API short of setting null, which most
                // SDK versions accept. If a stale key persists, clients will keep using their last
                // pull; acceptable degradation.
                return;
            }

            var props = new ExitGames.Client.Photon.Hashtable();
            bool changed = false;
            foreach (var e in UpgradeRegistry.Entries)
            {
                bool en = e.Enabled.Value;
                int max = e.MaxStacks.Value;
                if (_lastPushed.TryGetValue(e.Name, out var last) && last.en == en && last.max == max)
                    continue;
                _lastPushed[e.Name] = (en, max);
                props["UL_" + e.Name + "_E"] = en;
                props["UL_" + e.Name + "_M"] = max;
                changed = true;
            }

            if (!changed) return;

            Photon.Pun.PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Plugin.Log.LogInfo($"[Sync] Host pushed {props.Count / 2} upgrade limit settings");
        }

        private void PullHostSettings()
        {
            var props = Photon.Pun.PhotonNetwork.CurrentRoom?.CustomProperties;
            if (props == null) return;

            bool any = false;
            foreach (var e in UpgradeRegistry.Entries)
            {
                string ke = "UL_" + e.Name + "_E";
                string km = "UL_" + e.Name + "_M";
                if (props.ContainsKey(ke) && props[ke] is bool be) { e.ActiveEnabled = be; any = true; }
                if (props.ContainsKey(km) && props[km] is int mi) { e.ActiveMax = mi; any = true; }
            }
            if (any) Plugin.Log.LogInfo("[Sync] Pulled host upgrade-limit settings from room properties");
        }
    }

    internal static class CapPrefix
    {
        // Harmony invokes this with __originalMethod and the steamID arg of the patched method.
        // Returning false skips the original increment (the cap is hit). Returning true lets it run.
        public static bool Prefix(string steamID, MethodBase __originalMethod)
        {
            if (!UpgradeRegistry.ByMethod.TryGetValue(__originalMethod, out var entry)) return true;
            if (!entry.ActiveEnabled) return true;
            if (entry.CountField == null) return true;

            var smType = AccessTools.TypeByName("StatsManager");
            var smInstanceField = smType != null ? AccessTools.Field(smType, "instance") : null;
            var sm = smInstanceField?.GetValue(null);
            if (sm == null) return true;

            var dict = entry.CountField.GetValue(sm) as System.Collections.Generic.IDictionary<string, int>;
            if (dict == null) return true;

            if (!dict.TryGetValue(steamID, out int current)) current = 0;
            if (current >= entry.ActiveMax)
            {
                Plugin.Log.LogDebug($"[Cap] {entry.Name} for {steamID} blocked at {current}/{entry.ActiveMax}");
                return false;
            }
            return true;
        }
    }
}
