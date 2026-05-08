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
        // Reverse lookup keyed by the StatsManager dict field name. Used by the
        // DictionaryUpdateValue prefix to clamp shared/RPC writes (e.g. from
        // SharedUpgradesPlus's distribution path which bypasses PunManager).
        internal static readonly Dictionary<string, UpgradeEntry> ByDictName = new(StringComparer.Ordinal);

        // Base-game upgrades. Methods live on PunManager with signature
        // `int UpgradePlayer*(string _steamID, int value = 1)`; the matching
        // count dict lives on StatsManager. Names don't follow a derivable
        // pattern (Energy↔Stamina, SprintSpeed↔Speed, GrabStrength↔Strength,
        // GrabRange↔Range, TumbleLaunch↔Launch, ThrowStrength↔Throw), so the
        // mapping is hardcoded.
        private static readonly (string Name, string MethodName, string DictField)[] BaseMap =
        {
            ("Health",            "UpgradePlayerHealth",         "playerUpgradeHealth"),
            ("Energy",            "UpgradePlayerEnergy",         "playerUpgradeStamina"),
            ("ExtraJump",         "UpgradePlayerExtraJump",      "playerUpgradeExtraJump"),
            ("TumbleLaunch",      "UpgradePlayerTumbleLaunch",   "playerUpgradeLaunch"),
            ("TumbleClimb",       "UpgradePlayerTumbleClimb",    "playerUpgradeTumbleClimb"),
            ("TumbleWings",       "UpgradePlayerTumbleWings",    "playerUpgradeTumbleWings"),
            ("SprintSpeed",       "UpgradePlayerSprintSpeed",    "playerUpgradeSpeed"),
            ("CrouchRest",        "UpgradePlayerCrouchRest",     "playerUpgradeCrouchRest"),
            ("GrabStrength",      "UpgradePlayerGrabStrength",   "playerUpgradeStrength"),
            ("ThrowStrength",     "UpgradePlayerThrowStrength",  "playerUpgradeThrow"),
            ("GrabRange",         "UpgradePlayerGrabRange",      "playerUpgradeRange"),
            // Two upgrades on PunManager that drop the "Player" infix.
            ("DeathHeadBattery",  "UpgradeDeathHeadBattery",     "playerUpgradeDeathHeadBattery"),
            ("MapPlayerCount",    "UpgradeMapPlayerCount",       "playerUpgradeMapPlayerCount"),
        };

        public static void Discover()
        {
            Entries.Clear();
            ByMethod.Clear();
            ByDictName.Clear();

            var punManager = AccessTools.TypeByName("PunManager");
            var statsManager = AccessTools.TypeByName("StatsManager");

            if (punManager == null) Plugin.Log.LogError("[Discover] PunManager not found — base entries unenforceable.");
            if (statsManager == null) Plugin.Log.LogError("[Discover] StatsManager not found — base entries unenforceable.");

            var seen = new HashSet<MethodInfo>();

            foreach (var (name, methodName, fieldName) in BaseMap)
            {
                MethodInfo? method = null;
                FieldInfo? field = null;
                if (punManager != null)
                    method = AccessTools.Method(punManager, methodName, new[] { typeof(string), typeof(int) });
                if (statsManager != null)
                    field = AccessTools.Field(statsManager, fieldName);

                if (method == null) Plugin.Log.LogWarning($"[Discover] {methodName} not on PunManager — {name} cap won't enforce.");
                if (field == null) Plugin.Log.LogWarning($"[Discover] {fieldName} not on StatsManager — {name} cap won't enforce.");

                var entry = new UpgradeEntry { Name = name, Method = method, CountField = field };
                if (method != null && field != null)
                {
                    ByMethod[method] = entry;
                    seen.Add(method);
                    Plugin.Log.LogInfo($"[Discover] {methodName} ↔ {fieldName}");
                }
                if (field != null) ByDictName[field.Name] = entry;
                Entries.Add(entry);
            }

            if (statsManager != null)
                ScanModded(statsManager, seen);

            Plugin.Log.LogInfo($"[Discover] {Entries.Count} entries total ({ByMethod.Count} enforceable).");
        }

        // Walk loaded assemblies for additional `int UpgradePlayer*(string, int)`
        // methods (e.g. modded upgrades that follow the base-game convention).
        // For each, IL-scan the body to find which Dictionary<string,int> field
        // on StatsManager it touches — that's the count we'll cap against.
        private static void ScanModded(Type statsManager, HashSet<MethodInfo> seen)
        {
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types ?? Array.Empty<Type>(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null) continue;
                    MethodInfo[] methods;
                    try { methods = type.GetMethods(BF); }
                    catch { continue; }

                    foreach (var m in methods)
                    {
                        if (seen.Contains(m)) continue;
                        if (!m.Name.StartsWith("Upgrade", StringComparison.Ordinal)) continue;
                        if (m.ReturnType != typeof(int)) continue;
                        var ps = m.GetParameters();
                        if (ps.Length != 2) continue;
                        if (ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(int)) continue;

                        var dict = FindDictField(m, statsManager);
                        if (dict == null)
                        {
                            // No upgrade dict touched — not an upgrade method, skip silently.
                            continue;
                        }

                        // Strip the longer prefix when present so the friendly name stays clean.
                        var stripped = m.Name.StartsWith("UpgradePlayer", StringComparison.Ordinal)
                            ? m.Name.Substring("UpgradePlayer".Length)
                            : m.Name.Substring("Upgrade".Length);
                        var name = stripped;
                        // Avoid colliding with a base-map entry name.
                        if (Entries.Exists(e => e.Name == name)) name = type.Name + "_" + name;

                        var entry = new UpgradeEntry { Name = name, Method = m, CountField = dict };
                        ByMethod[m] = entry;
                        ByDictName[dict.Name] = entry;
                        seen.Add(m);
                        Entries.Add(entry);
                        Plugin.Log.LogInfo($"[Discover] Modded {type.FullName}.{m.Name} ↔ {dict.Name} as {name}");
                    }
                }
            }
        }

        // Scan a method's IL for ldfld/stfld instructions referencing a
        // Dictionary<string,int> field on StatsManager. Returns the first one.
        // We scan every byte position rather than walking opcodes properly —
        // ResolveField validates the token, and our field-type/declaring-type
        // predicate filters out stray matches.
        private static FieldInfo? FindDictField(MethodInfo method, Type statsManager)
        {
            MethodBody? body;
            try { body = method.GetMethodBody(); }
            catch { return null; }
            if (body == null) return null;
            var il = body.GetILAsByteArray();
            if (il == null || il.Length < 5) return null;

            var module = method.Module;
            var dictType = typeof(Dictionary<string, int>);
            Type[]? typeArgs = null;
            Type[]? methArgs = null;
            try
            {
                if (method.DeclaringType?.IsGenericType == true)
                    typeArgs = method.DeclaringType.GetGenericArguments();
                if (method.IsGenericMethod)
                    methArgs = method.GetGenericArguments();
            }
            catch { }

            for (int i = 0; i <= il.Length - 5; i++)
            {
                byte op = il[i];
                // ldfld 0x7B, ldflda 0x7C, stfld 0x7D, ldsfld 0x7E, stsfld 0x80
                if (op != 0x7B && op != 0x7C && op != 0x7D && op != 0x7E && op != 0x80) continue;
                int token = BitConverter.ToInt32(il, i + 1);
                FieldInfo? field;
                try { field = module.ResolveField(token, typeArgs, methArgs); }
                catch { continue; }
                if (field == null) continue;
                if (field.DeclaringType != statsManager) continue;
                if (field.FieldType != dictType) continue;
                return field;
            }
            return null;
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

            // Second-line cap: clamp direct dict writes (e.g. SharedUpgradesPlus's
            // modded distribution path, which goes through UpdateStatRPC → DictionaryUpdateValue
            // and bypasses our PunManager.UpgradePlayer* prefix).
            var smType = AccessTools.TypeByName("StatsManager");
            var dictUpdate = smType != null
                ? AccessTools.Method(smType, "DictionaryUpdateValue", new[] { typeof(string), typeof(string), typeof(int) })
                : null;
            if (dictUpdate != null)
            {
                try
                {
                    var clamp = new HarmonyMethod(typeof(DictUpdateClampPrefix).GetMethod(nameof(DictUpdateClampPrefix.Prefix)));
                    harmony.Patch(dictUpdate, prefix: clamp);
                    Log.LogInfo("[Patch] Installed clamp prefix on StatsManager.DictionaryUpdateValue");
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[Patch] Failed to patch DictionaryUpdateValue: {ex.GetType().Name} {ex.Message}");
                }
            }
            else
            {
                Log.LogWarning("[Patch] StatsManager.DictionaryUpdateValue not found — shared/RPC clamp disabled.");
            }

            Log.LogInfo($"UpgradeLimiter v{PluginInfo.PLUGIN_VERSION} loaded.");
        }

        private void BindUpgradeConfigs()
        {
            var range = new AcceptableValueRange<int>(0, 99);
            foreach (var e in UpgradeRegistry.Entries)
            {
                string section = e.Name;
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
                    // While we're a client in a room, ActiveEnabled is host-owned via
                    // Photon sync — don't let local config changes (or REPOConfig
                    // autosave spam, which fires SettingChanged repeatedly) clobber
                    // the synced value. ResetActiveToLocal restores on room-leave.
                    if (Photon.Pun.PhotonNetwork.InRoom && !Photon.Pun.PhotonNetwork.IsMasterClient) return;
                    entry.ActiveEnabled = entry.Enabled.Value;
                    if (Photon.Pun.PhotonNetwork.InRoom && Photon.Pun.PhotonNetwork.IsMasterClient)
                        SettingsSyncer.Instance?.PushHostSettingsExternal();
                };
                entry.MaxStacks.SettingChanged += (_, _) =>
                {
                    if (Photon.Pun.PhotonNetwork.InRoom && !Photon.Pun.PhotonNetwork.IsMasterClient) return;
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

    // Catches paths that bypass PunManager.UpgradePlayer* and write directly
    // to a StatsManager dict — most importantly SharedUpgradesPlus's modded
    // upgrade distribution (UpdateStatRPC → DictionaryUpdateValue), but also
    // any future mod or game code that calls UpdateStat/DictionaryUpdateValue
    // for an upgrade dict. Clamps the absolute value to ActiveMax.
    internal static class DictUpdateClampPrefix
    {
        public static void Prefix(string dictionaryName, string key, ref int value)
        {
            if (!UpgradeRegistry.ByDictName.TryGetValue(dictionaryName, out var entry)) return;
            if (!entry.ActiveEnabled) return;
            if (value <= entry.ActiveMax) return;
            Plugin.Log.LogDebug($"[Cap] {entry.Name} for {key} clamped {value} → {entry.ActiveMax} (DictionaryUpdateValue)");
            value = entry.ActiveMax;
        }
    }

    internal static class CapPrefix
    {
        // Harmony binds prefix params by name, so `_steamID` and `value` must
        // match the original (PunManager.UpgradePlayer*(string _steamID, int value = 1)).
        // Returning false skips the original increment.
        public static bool Prefix(string _steamID, int value, MethodBase __originalMethod)
        {
            if (!UpgradeRegistry.ByMethod.TryGetValue(__originalMethod, out var entry)) return true;
            if (!entry.ActiveEnabled) return true;
            if (entry.CountField == null) return true;
            // Only cap upward stacking — let decrements / resets through.
            if (value <= 0) return true;

            var smType = AccessTools.TypeByName("StatsManager");
            var smInstanceField = smType != null ? AccessTools.Field(smType, "instance") : null;
            var sm = smInstanceField?.GetValue(null);
            if (sm == null) return true;

            var dict = entry.CountField.GetValue(sm) as IDictionary<string, int>;
            if (dict == null) return true;

            if (!dict.TryGetValue(_steamID, out int current)) current = 0;
            if (current + value > entry.ActiveMax)
            {
                Plugin.Log.LogDebug($"[Cap] {entry.Name} for {_steamID} blocked: {current}+{value} > {entry.ActiveMax}");
                return false;
            }
            return true;
        }
    }
}
