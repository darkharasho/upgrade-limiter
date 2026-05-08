# UpgradeLimiter — Design

A R.E.P.O. BepInEx mod that caps how many of each upgrade a player can stack. Per-upgrade enable toggle and per-upgrade max-stacks value. Consuming an upgrade past the cap destroys the crystal but does not increment the stat (no-op).

## Goals

- Per-upgrade `Enabled` toggle (default off) and `MaxStacks` value (default 5).
- Past the cap: the upgrade item is consumed normally, but the stat increment is skipped.
- Optional host-to-client sync (single host-side toggle).
- Resilient to REPO's upgrade roster shifting between game versions: discover upgrade methods via reflection rather than hard-coding them.

## Non-goals

- Refunding or rolling back stats already above the cap.
- Per-client opt-in to host sync, or per-client overrides while in a hosted room.
- Limiting anything other than the standard `StatsManager` player upgrades (no item upgrades, no held-item caps, no map upgrades beyond what `StatsManager` exposes).
- Rejecting/preserving the upgrade item on the ground.

## Stack

- BepInEx 5 plugin, Harmony patches.
- Target framework `netstandard2.1`.
- Plugin GUID: `darkharasho.UpgradeLimiter`. Plugin name: `UpgradeLimiter`.
- `manifest.json` is the single source of truth for version (read at build time, mirroring mini-eepo's csproj pattern).
- Game DLL hint paths into `$(GameDir)/REPO_Data/Managed`, overridable via `/p:GameDir=...`.
- No ScalerCore dependency.
- BepInEx and Harmony pulled from NuGet; game DLLs referenced with `<Private>false</Private>`.

## Architecture

Single-assembly plugin with three pieces:

1. **`Plugin`** — `BaseUnityPlugin`. Owns config bindings, the registry of discovered upgrades, and the Harmony patch registration. Exposes `Active*` runtime mirrors that the patch reads.
2. **`UpgradeRegistry`** — built once in `Awake`. For each discovered upgrade, holds: the upgrade name (e.g. `Health`), the `MethodInfo` for the increment method, the `FieldInfo` for the corresponding count dictionary, and its `ConfigEntry<bool>` / `ConfigEntry<int>` pair.
3. **`SettingsSyncer`** — `MonoBehaviour` attached to the plugin GameObject. Polls Photon room state, pushes/pulls limits via `PhotonNetwork.CurrentRoom.CustomProperties`. Mirrors mini-eepo's polling-based syncer (no `MonoBehaviourPunCallbacks` — those didn't fire reliably in mini-eepo, the same lesson applies here).

### Discovery

At `Awake`, scan `typeof(StatsManager)` for instance methods matching one of these shapes (whichever REPO actually exposes — verify during implementation):

- `void UpgradePlayer<Name>(string steamID)`
- `void PlayerUpgrade<Name>(string steamID)`

The implementation should pick the form REPO actually uses and document it. Whichever matches, the `<Name>` becomes the config section suffix. For each method, look up the dictionary `playerUpgrade<Name>` (or the equivalent `Dictionary<string, int>` field) on `StatsManager`. Pair them by name.

If a method has no matching dictionary, log a warning and skip patching it (fail-open: that upgrade is uncappable but doesn't break the rest of the mod).

If discovery finds zero upgrades, log an error and load passively.

### Harmony prefix

A single shared prefix method, registered manually per discovered method (not via `[HarmonyPatch]` attributes — the targets aren't known at compile time):

```csharp
static bool Prefix(string steamID, MethodBase __originalMethod) {
    var entry = Plugin.Registry.LookupByMethod(__originalMethod);
    if (entry == null || !entry.ActiveEnabled) return true;
    var dict = entry.CountField.GetValue(StatsManager.instance) as Dictionary<string, int>;
    if (dict == null) return true;
    if (!dict.TryGetValue(steamID, out var current)) current = 0;
    if (current >= entry.ActiveMax) return false; // no-op the increment
    return true;
}
```

Returning `false` only skips the stat increment. The upgrade item's own `Trigger`/`Use` flow destroys the crystal independently — implementation must verify this assumption against REPO's actual call order. If destruction is *inside* the StatsManager method (unlikely but possible), the hook needs to move to the item's `Trigger` method instead and replicate the destroy. Treat that as a design adjustment to be flagged during implementation, not a silent change.

## Config layout

```
[Sync]
# Host-only: when true, host pushes limits to all clients via Photon room properties.
# When false, host never publishes; each client uses its own local config.
SyncToClients = true

[Limits.Health]
Enabled = false
MaxStacks = 5

[Limits.Stamina]
Enabled = false
MaxStacks = 5

# ... one [Limits.<Name>] section per discovered upgrade
```

- All sections are generated on first run by the discovery pass; the user does not edit code to add new ones.
- `MaxStacks` uses `AcceptableValueRange<int>(0, 99)`. `0` means "no further upgrades allowed" — useful as a hard disable.
- `SettingChanged` handlers update `Active*` mirrors; on the host, they additionally re-push to room properties when in a room.

## Multiplayer sync

Same shape as mini-eepo's `SettingsSyncer`:

- Host on join (or on becoming master via host migration): publishes all `Enabled`/`MaxStacks` values into `PhotonNetwork.CurrentRoom.CustomProperties`. Keys are short — `UL_<Name>_E` (bool) and `UL_<Name>_M` (int).
- Non-host on join: pulls all `UL_*` keys; updates `Active*` mirrors. Polls every 1s as a backstop in case the room properties update arrived before our join finished processing.
- Leave room: `Active*` mirrors reset to local config values.
- Host with `SyncToClients = false`: never publishes. Clients in that room see no `UL_*` keys and fall back to their local config.
- Push deduplication: cache last-pushed values; skip the broadcast if nothing changed (mini-eepo learned this — REPOConfig autosave fires `SettingChanged` repeatedly with identical values).

## Error handling

- Reflection failures during discovery: log and skip the affected upgrade. Never throw out of `Awake`.
- `StatsManager.instance == null` during the prefix: return `true` (let vanilla run).
- Dictionary lookup failures: return `true` (fail-open — better to allow an upgrade than to silently break gameplay).
- Photon edge cases: room null, properties missing — silently no-op the sync, mirror mini-eepo's defensive null checks.

## Edge cases

- **Pre-existing overage:** if a player already has more stacks than the new cap (e.g. host lowered the cap mid-run), they keep what they have. The cap only blocks *new* increments. Documented behavior, not a bug.
- **MaxStacks = 0 with Enabled = true:** the prefix sees `0 >= 0` and blocks every increment. Functions as a per-upgrade off switch via config.
- **Host migration:** when the new master client has the mod, they publish their settings on becoming master (mirrors mini-eepo). When they don't have the mod, the room properties become stale and clients keep using whatever they last pulled — acceptable degradation.
- **Singleplayer:** REPO 0.4.0+ runs Photon even in solo. `PhotonNetwork.InRoom` may still be true; the sync code handles that path correctly because the local player is also master, so it just pushes its own settings to a room only it occupies.

## Testing

Manual test matrix (no automated tests — Unity/BepInEx mods don't have a practical unit test harness for game-side code):

- Singleplayer: enable Health limit at 3, pick up 5 Health upgrades, confirm stat caps at 3 and remaining 2 crystals are consumed.
- Singleplayer: limit disabled, confirm vanilla behavior (no cap).
- Multiplayer host with `SyncToClients = true`: client should see same caps as host.
- Multiplayer host with `SyncToClients = false`: client uses its own local config.
- Host migration: confirm new host pushes their settings on becoming master.
- REPOConfig runtime change: host changes `MaxStacks` mid-run, confirm clients see the new value within ~1s.

## Project structure

```
upgrade-limiter/
├── manifest.json              # version source of truth
├── UpgradeLimiter.csproj      # netstandard2.1, manifest-driven version
├── icon.png                   # for Thunderstore
├── README.md
├── CHANGELOG.md
├── package.sh                 # produce Thunderstore zip (mini-eepo style)
├── src/
│   └── Plugin.cs              # everything in one file (mini-eepo precedent — fine at this size)
└── docs/
    └── superpowers/specs/...
```

One source file is consistent with mini-eepo. If `Plugin.cs` grows past ~400 lines or the syncer warrants isolation, split then — not preemptively.
