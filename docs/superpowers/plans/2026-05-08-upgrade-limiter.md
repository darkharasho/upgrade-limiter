# UpgradeLimiter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a R.E.P.O. BepInEx mod that caps the number of stat upgrades each player can stack, configurable per upgrade with optional host→client sync.

**Architecture:** Single-assembly BepInEx 5 plugin using Harmony. At `Awake`, reflectively discover `StatsManager`'s upgrade methods and their corresponding count dictionaries, bind one config section per upgrade, and patch each method with a shared prefix that no-ops the increment when the cap is hit. A `SettingsSyncer` MonoBehaviour pushes/pulls limits via `PhotonNetwork.CurrentRoom.CustomProperties`, mirroring the proven mini-eepo pattern.

**Tech Stack:** C# / netstandard2.1, BepInEx 5.4.21, HarmonyLib (via BepInEx.Core NuGet), PhotonUnityNetworking (game DLL ref), Unity (game DLL refs).

**Note on testing:** This mod runs inside R.E.P.O. via BepInEx. There is no practical unit test harness — verification is manual, by launching the game with the mod loaded. Each task that produces user-visible behavior includes a manual verification checklist instead of automated tests. The reference mod `../mini-eepo/` follows the same approach.

---

## File Structure

```
upgrade-limiter/
├── manifest.json                          # Thunderstore manifest, version source of truth
├── UpgradeLimiter.csproj                  # netstandard2.1, manifest-driven version
├── package.sh                             # build + deploy to r2modman + zip
├── README.md                              # user-facing docs
├── CHANGELOG.md                           # version notes
├── .gitignore                             # bin/obj/*.zip
└── src/
    └── Plugin.cs                          # entire plugin (mini-eepo precedent)
```

Single source file by design — small enough to keep coherent, splits if it exceeds ~400 lines later.

---

## Task 1: Project scaffolding

**Files:**
- Create: `manifest.json`
- Create: `UpgradeLimiter.csproj`
- Create: `src/Plugin.cs`
- Modify: `.gitignore`

- [ ] **Step 1: Update `.gitignore`**

Replace the file contents with:

```gitignore
# Build output
bin/
obj/
*.zip

# Rider / VS
.idea/
.vs/
*.user

# Environment
.env

# Thunderstore (icon.png must be user-supplied, not versioned)
*.png

# Superpowers
.superpowers
```

- [ ] **Step 2: Create `manifest.json`**

```json
{
    "name": "UpgradeLimiter",
    "version_number": "0.1.0",
    "website_url": "https://github.com/darkharasho/upgrade-limiter",
    "description": "Caps how many of each player upgrade can be stacked. Configurable per upgrade.",
    "dependencies": [
        "BepInEx-BepInExPack-5.4.2100"
    ]
}
```

- [ ] **Step 3: Create `UpgradeLimiter.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>UpgradeLimiter</AssemblyName>
    <RootNamespace>UpgradeLimiter</RootNamespace>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ManifestJson>$([System.IO.File]::ReadAllText('$(MSBuildProjectDirectory)/manifest.json'))</ManifestJson>
    <Version>$([System.Text.RegularExpressions.Regex]::Match($(ManifestJson), '"version_number"\s*:\s*"([^"]+)"').Groups[1].Value)</Version>
    <BepInExPluginGuid>darkharasho.UpgradeLimiter</BepInExPluginGuid>
    <BepInExPluginName>UpgradeLimiter</BepInExPluginName>
    <BepInExPluginVersion>$(Version)</BepInExPluginVersion>
  </PropertyGroup>

  <PropertyGroup Condition="'$(GameDir)' == ''">
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\REPO</GameDir>
  </PropertyGroup>
  <PropertyGroup>
    <ManagedDir>$(GameDir)/REPO_Data/Managed</ManagedDir>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.4.21.0">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="BepInEx.PluginInfoProps" Version="1.1.0" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="UnityEngine">
      <HintPath>$(ManagedDir)/UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(ManagedDir)/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ManagedDir)/Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="PhotonUnityNetworking">
      <HintPath>$(ManagedDir)/PhotonUnityNetworking.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="PhotonRealtime">
      <HintPath>$(ManagedDir)/PhotonRealtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Photon3Unity3D">
      <HintPath>$(ManagedDir)/Photon3Unity3D.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Create `src/Plugin.cs` minimal stub that loads**

```csharp
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
```

- [ ] **Step 5: Build to confirm scaffolding works**

Run:
```bash
GAME_DIR="$(ls -d $HOME/.steam/steam/steamapps/common/REPO $HOME/.local/share/Steam/steamapps/common/REPO 2>/dev/null | head -1)"
dotnet build UpgradeLimiter.csproj --configuration Release /p:GameDir="$GAME_DIR"
```

Expected: `Build succeeded.` with `bin/Release/netstandard2.1/UpgradeLimiter.dll` produced. If the game dir cannot be auto-located, set `GAME_DIR` manually to wherever R.E.P.O. is installed.

- [ ] **Step 6: Commit**

```bash
git add .gitignore manifest.json UpgradeLimiter.csproj src/Plugin.cs
git commit -m "Scaffold UpgradeLimiter BepInEx plugin"
```

---

## Task 2: Discover upgrade methods and dictionaries on StatsManager

The plan is to inspect `StatsManager` reflectively for instance methods named `UpgradePlayer<Name>` or `PlayerUpgrade<Name>` taking a single `string` argument, and pair each with a `Dictionary<string,int>` field whose name matches `playerUpgrade<Name>` (case-insensitive). Verify the actual naming on first run by logging discovery results — adjust the matcher if the game uses a different convention.

**Files:**
- Modify: `src/Plugin.cs`

- [ ] **Step 1: Add the registry types and discovery code to `Plugin.cs`**

Replace the contents of `src/Plugin.cs` with:

```csharp
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

        // Discover at Awake. Two candidate naming conventions are accepted; whichever REPO
        // actually uses, we use. If neither matches, log and load passively.
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

            // Build a map of all candidate Dictionary<string,int> fields by lowercased name.
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

                // Skip helpers like UpgradePlayerSet / UpgradePlayerLoad — only match names that
                // look like a stat (heuristic: must NOT contain "Set"/"Load"/"Get"/"Apply"/"Sync"
                // and must not be a generic helper). Adjust if discovery logs reveal false positives.
                if (upgradeName.Contains("Set") || upgradeName.Contains("Load") ||
                    upgradeName.Contains("Get") || upgradeName.Contains("Apply") ||
                    upgradeName.Contains("Sync"))
                    continue;

                // Find dict by name match: "playerUpgrade<Name>"
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
```

- [ ] **Step 2: Build**

```bash
dotnet build UpgradeLimiter.csproj --configuration Release /p:GameDir="$GAME_DIR"
```

Expected: `Build succeeded.`

- [ ] **Step 3: Manual verification — confirm discovery logs**

Copy the built DLL to the BepInEx plugins folder and launch R.E.P.O. (or rely on `package.sh` from Task 7 once that exists; for now do it manually):

```bash
R2_PROFILE="${R2_PROFILE:-Default}"
R2_PLUGINS="$HOME/.config/r2modmanPlus-local/REPO/profiles/$R2_PROFILE/BepInEx/plugins/UpgradeLimiter"
mkdir -p "$R2_PLUGINS"
cp bin/Release/netstandard2.1/UpgradeLimiter.dll "$R2_PLUGINS/"
```

Launch the game and check `BepInEx/LogOutput.log`. Expected: a sequence of `[Discover] UpgradePlayerHealth ↔ playerUpgradeHealth` (or `[Discover] PlayerUpgradeHealth ↔ playerUpgradeHealth`) log lines, one per upgrade, then `Found N upgrade methods.` where N is at least 5.

If the log shows `No upgrade methods discovered` or only one or two: open `Assembly-CSharp.dll` in dnSpy/ILSpy, look at `StatsManager`, note the actual method/field names, and update the matcher in `UpgradeRegistry.Discover` accordingly. Common failure modes: methods take a `PlayerAvatar` instead of `string`, or dictionaries are nested in a sub-class. Adjust the matcher rather than working around it.

- [ ] **Step 4: Commit**

```bash
git add src/Plugin.cs
git commit -m "Discover StatsManager upgrade methods via reflection"
```

---

## Task 3: Bind config sections per discovered upgrade

**Files:**
- Modify: `src/Plugin.cs`

- [ ] **Step 1: Add config fields to `UpgradeEntry` and bind in `Plugin.Awake`**

Update `UpgradeEntry` (in `src/Plugin.cs`) to add config + active mirror fields. Add this `using`:

```csharp
using BepInEx.Configuration;
```

Replace the `UpgradeEntry` class with:

```csharp
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
```

Add a `Sync` config section + per-upgrade binding to `Plugin`. Replace the `Plugin` class with:

```csharp
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
```

- [ ] **Step 2: Build**

```bash
dotnet build UpgradeLimiter.csproj --configuration Release /p:GameDir="$GAME_DIR"
```

Expected: `Build succeeded.`

- [ ] **Step 3: Manual verification**

Deploy the DLL (same `cp` command as Task 2 Step 3), launch R.E.P.O. once to generate the config, and inspect `BepInEx/config/darkharasho.UpgradeLimiter.cfg`. Expected: a `[Sync]` section with `SyncToClients = true`, then one `[Limits.<Name>]` section per discovered upgrade with `Enabled = false` and `MaxStacks = 5`.

- [ ] **Step 4: Commit**

```bash
git add src/Plugin.cs
git commit -m "Bind config sections per discovered upgrade"
```

---

## Task 4: Implement the cap prefix and patch discovered methods

**Files:**
- Modify: `src/Plugin.cs`

- [ ] **Step 1: Add the cap prefix and register patches manually**

Append a new static class to `src/Plugin.cs` (inside the namespace, after `Plugin`):

```csharp
internal static class CapPrefix
{
    // Harmony invokes this with __originalMethod and the steamID arg of the patched method.
    // Returning false skips the original increment (the cap is hit). Returning true lets it run.
    public static bool Prefix(string steamID, MethodBase __originalMethod)
    {
        if (!UpgradeRegistry.ByMethod.TryGetValue(__originalMethod, out var entry)) return true;
        if (!entry.ActiveEnabled) return true;

        var sm = StatsManager.instance;
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
```

Note: `StatsManager.instance` is referenced by name. If the symbol isn't accessible from this assembly (private singleton, or the field is named differently), replace with `AccessTools.Field(typeof(StatsManager), "instance")?.GetValue(null)` and cast. Verify on first build.

Then update `Plugin.Awake` to register patches manually after `harmony.PatchAll()`. Replace the line:

```csharp
        var harmony = new Harmony("darkharasho.UpgradeLimiter");
        harmony.PatchAll();
```

with:

```csharp
        var harmony = new Harmony("darkharasho.UpgradeLimiter");
        harmony.PatchAll();
        var prefix = new HarmonyMethod(typeof(CapPrefix).GetMethod(nameof(CapPrefix.Prefix)));
        foreach (var e in UpgradeRegistry.Entries)
        {
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
```

- [ ] **Step 2: Build**

```bash
dotnet build UpgradeLimiter.csproj --configuration Release /p:GameDir="$GAME_DIR"
```

If the build fails on `StatsManager.instance`, switch to the reflection fallback (see note above) and rebuild.

Expected: `Build succeeded.`

- [ ] **Step 3: Manual verification (singleplayer)**

Deploy DLL, launch R.E.P.O., and edit `BepInEx/config/darkharasho.UpgradeLimiter.cfg` to set:

```
[Limits.Health]
Enabled = true
MaxStacks = 2
```

Start a singleplayer run with debug/cheat mode or a level that spawns Health upgrades. Confirm:

1. First two Health upgrades increase the stat as normal.
2. The third Health upgrade is consumed (crystal disappears, sound plays) but the player's max health does not increase.
3. `BepInEx/LogOutput.log` contains `[Cap] Health for <steamID> blocked at 2/2`.

If step 2 fails (the crystal stays on the ground or no sound plays), the destroy is happening *inside* the StatsManager method rather than upstream. Adjust: leave the prefix in place, but also patch the upgrade item's `Trigger` / `Use` method to manually destroy the GameObject when the cap blocks it. Document the adjustment in `CHANGELOG.md` (Task 8).

- [ ] **Step 4: Commit**

```bash
git add src/Plugin.cs
git commit -m "Cap upgrade increments via Harmony prefix on discovered methods"
```

---

## Task 5: Photon room-properties sync (host → clients)

**Files:**
- Modify: `src/Plugin.cs`

- [ ] **Step 1: Add the `SettingsSyncer` MonoBehaviour**

Append to `src/Plugin.cs` (inside namespace):

```csharp
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
            Plugin.ResetActiveToLocal();
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

        Plugin.ResetActiveToLocal();
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
            if (props.ContainsKey(ke)) { e.ActiveEnabled = (bool)props[ke]; any = true; }
            if (props.ContainsKey(km)) { e.ActiveMax = (int)props[km]; any = true; }
        }
        if (any) Plugin.Log.LogInfo("[Sync] Pulled host upgrade-limit settings from room properties");
    }
}
```

- [ ] **Step 2: Wire the syncer into `Plugin.Awake` and add re-push hooks**

In `Plugin.Awake`, after `ResetActiveToLocal()` and before `harmony.PatchAll()`, add:

```csharp
        gameObject.AddComponent<SettingsSyncer>();

        // Host-side: when SyncToClients toggles or any limit changes mid-game, re-push.
        SyncToClients.SettingChanged += (_, _) => SettingsSyncer.Instance?.PushHostSettingsExternal();
```

In `BindUpgradeConfigs`, extend the per-entry `SettingChanged` hooks so host config edits re-push to clients. Replace the two existing handler lines with:

```csharp
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
```

- [ ] **Step 3: Build**

```bash
dotnet build UpgradeLimiter.csproj --configuration Release /p:GameDir="$GAME_DIR"
```

Expected: `Build succeeded.`

- [ ] **Step 4: Manual verification (multiplayer)**

Two machines (or two R.E.P.O. instances on the same machine via separate r2modman profiles), both with the mod. Host has `Limits.Health.Enabled = true`, `MaxStacks = 2`. Client has `Limits.Health.Enabled = false`.

1. Host creates a room, client joins.
2. Confirm in client's `LogOutput.log`: `[Sync] Pulled host upgrade-limit settings from room properties`.
3. In-game, the client's third Health upgrade should also no-op — the host's cap applies even though the client's local config has it disabled.
4. Host changes `MaxStacks` to 5 via REPOConfig (or by editing the cfg file). Within ~1s, the client should log a fresh pull and the new cap should apply.
5. Host sets `SyncToClients = false`, restarts the room. Client's local config now applies again (cap disabled on client).

- [ ] **Step 5: Commit**

```bash
git add src/Plugin.cs
git commit -m "Sync upgrade limits host→clients via Photon room properties"
```

---

## Task 6: Packaging script

**Files:**
- Create: `package.sh`

- [ ] **Step 1: Create `package.sh`**

```bash
#!/usr/bin/env bash
set -euo pipefail

VERSION="$(python3 -c "import json; print(json.load(open('manifest.json'))['version_number'])")"
DLL="bin/Release/netstandard2.1/UpgradeLimiter.dll"
OUT="UpgradeLimiter-${VERSION}.zip"
R2_PROFILE="${R2_PROFILE:-Default}"
R2_PLUGINS="$HOME/.config/r2modmanPlus-local/REPO/profiles/$R2_PROFILE/BepInEx/plugins/UpgradeLimiter"

if [ -z "${GAME_DIR:-}" ]; then
    for candidate in \
        "$HOME/.steam/steam/steamapps/common/REPO" \
        "$HOME/.local/share/Steam/steamapps/common/REPO"
    do
        if [ -d "$candidate" ]; then
            GAME_DIR="$candidate"
            break
        fi
    done
fi

if [ -z "${GAME_DIR:-}" ]; then
    echo "ERROR: Could not find R.E.P.O. install. Set GAME_DIR manually:"
    echo "  GAME_DIR=\"/path/to/REPO\" ./package.sh"
    exit 1
fi

echo "Using game dir: $GAME_DIR"

dotnet build UpgradeLimiter.csproj --configuration Release /p:GameDir="$GAME_DIR"

mkdir -p "$R2_PLUGINS"
cp "$DLL" "$R2_PLUGINS/"
echo "Deployed to r2modman profile: $R2_PROFILE"

if [ ! -f "icon.png" ]; then
    echo "WARNING: icon.png not found — skipping Thunderstore zip."
    exit 0
fi

rm -f "$OUT"
zip -j "$OUT" manifest.json icon.png README.md "$DLL"
echo "Packaged: $OUT"
```

- [ ] **Step 2: Make it executable and run it**

```bash
chmod +x package.sh
./package.sh
```

Expected: build succeeds, DLL is copied into the r2modman profile path. If `icon.png` is absent the script exits 0 with a warning — that's fine for now; an icon can be added later.

- [ ] **Step 3: Commit**

```bash
git add package.sh
git commit -m "Add package script (build, deploy, zip)"
```

---

## Task 7: README and CHANGELOG

**Files:**
- Modify: `README.md`
- Create: `CHANGELOG.md`

- [ ] **Step 1: Replace `README.md`**

```markdown
# UpgradeLimiter

A R.E.P.O. mod that caps how many of each player upgrade can be stacked. Each upgrade has its own enable toggle and max-stacks value.

When a player tries to consume an upgrade past the cap, the upgrade item is consumed normally (the crystal disappears) but the stat does not increase.

## Configuration

The config file is generated at `BepInEx/config/darkharasho.UpgradeLimiter.cfg` on first launch. It contains one section per upgrade discovered on `StatsManager` plus a sync section.

```
[Sync]
SyncToClients = true   # Host-only: push limits to all clients via Photon room properties.

[Limits.Health]
Enabled = false
MaxStacks = 5
```

`MaxStacks = 0` with `Enabled = true` blocks every increment for that upgrade — equivalent to disabling the upgrade entirely.

## Multiplayer

When the host has `SyncToClients = true`, every client in the room uses the host's caps regardless of their own config. When the host has `SyncToClients = false`, each client uses its own local config.

## Building

```bash
GAME_DIR="/path/to/REPO" ./package.sh
```

This builds the DLL, deploys it into your r2modman profile (`R2_PROFILE` env var, default `Default`), and produces a Thunderstore-ready zip if `icon.png` is present.
```

- [ ] **Step 2: Create `CHANGELOG.md`**

```markdown
# Changelog

## 0.1.0

- Initial release.
- Per-upgrade `Enabled` and `MaxStacks` config, defaults `false` / `5`.
- Reflection-based discovery of `StatsManager` upgrade methods.
- Host-to-client sync via Photon room properties (`Sync.SyncToClients`).
- Past-cap consumption is a no-op on the stat; the upgrade item is still destroyed.
```

- [ ] **Step 3: Commit**

```bash
git add README.md CHANGELOG.md
git commit -m "Add README and CHANGELOG for 0.1.0"
```

---

## Task 8: End-to-end verification pass

This task runs the full manual test matrix from the spec one more time on a clean build, before declaring the mod releasable.

- [ ] **Step 1: Clean build and deploy**

```bash
rm -rf bin obj
./package.sh
```

Expected: clean build succeeds, DLL is in the r2modman profile.

- [ ] **Step 2: Singleplayer cap verification**

Set `[Limits.Stamina]` `Enabled = true`, `MaxStacks = 3`. Start a singleplayer run, pick up 5 Stamina upgrades. Confirm:

- First three Stamina pickups raise the stat.
- Fourth and fifth pickups consume the crystal but do not raise the stat.
- LogOutput shows two `[Cap] Stamina ... blocked at 3/3` lines.

- [ ] **Step 3: Singleplayer disabled verification**

Set `[Limits.Stamina]` `Enabled = false`. Pick up 5 Stamina upgrades. Confirm: all five raise the stat (vanilla behavior).

- [ ] **Step 4: Multiplayer host→client sync**

Host has `Limits.Speed` `Enabled = true`, `MaxStacks = 2`. Client has `Enabled = false`. Client joins. Pick up 4 Speed upgrades on the client. Confirm: only the first 2 raise the stat (host's cap applied to client).

- [ ] **Step 5: Multiplayer SyncToClients = false**

Host sets `SyncToClients = false` and restarts the room. Repeat Step 4. Confirm: all 4 pickups raise the client's stat (host's cap is not applied).

- [ ] **Step 6: Pre-existing overage**

Set `MaxStacks = 5` for Health, pick up 5. Then mid-run, edit the config to `MaxStacks = 3`. Confirm: stat stays at 5 (not rolled back). The 6th pickup is blocked.

- [ ] **Step 7: Tag the release**

If everything passes:

```bash
git tag v0.1.0
echo "Ready to upload UpgradeLimiter-0.1.0.zip to Thunderstore."
```

If any step fails, do not tag; file the failure as a follow-up task.

---

## Self-review notes

- **Spec coverage check:** Goals (per-upgrade Enabled/MaxStacks, no-op past cap, optional host sync, reflection-based discovery) → Tasks 2/3/4/5. Non-goals correctly absent. Stack/architecture/config layout/sync behavior/error handling/edge cases all map to specific task steps.
- **Placeholder scan:** No `TBD`/`TODO`/`add error handling` entries; each step has either a code block, an exact command, or a concrete verification checklist.
- **Type/name consistency:** `UpgradeEntry`, `UpgradeRegistry`, `CapPrefix`, `SettingsSyncer`, `Plugin.SyncToClients`, `Plugin.ResetActiveToLocal`, `PushHostSettingsExternal` are referenced consistently across tasks.
- **One known unknown** (already flagged in spec and Task 2 Step 3): exact `StatsManager` method/field naming convention. The plan instructs the implementer to verify via dnSpy/ILSpy on first run and adjust the discovery matcher; this is acknowledged honest deferral, not a placeholder.
