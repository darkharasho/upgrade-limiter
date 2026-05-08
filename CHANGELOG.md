# Changelog

## 0.3.1

- Config sections renamed from `Limits.Player*` to bare upgrade names (`Health`, `SprintSpeed`, etc.) so the in-game REPOConfig header doesn't truncate. Existing 0.3.0 settings will be orphaned — re-toggle once.

## 0.3.0

- Fix: caps were never enforced. Discovery scanned `StatsManager` for `void M(string)` methods, but the upgrade methods are `int UpgradePlayer*(string, int)` on `PunManager`. Replaced with a hardcoded base-game table (11 upgrades) plus an IL-scanning fallback that picks up modded `UpgradePlayer*` methods by tracing which `StatsManager` `Dictionary<string,int>` field they touch.
- Cap math now considers the increment value (`current + value > MaxStacks`) and only blocks positive increments, so decrements / resets pass through.
- Removed entries that don't exist in the game (`PlayerSpeed`, `PlayerGrabThrow`, `PlayerMapPlayerCount`); added `PlayerThrowStrength`. Existing 0.2.0 config sections for the removed names will be orphaned.

## 0.2.0

- Prepopulate config sections for all 13 base-game upgrades, including ones not yet auto-discoverable via `StatsManager` reflection (CrouchRest, GrabThrow, TumbleClimb, TumbleWings, MapPlayerCount).
- Unpaired canonical entries appear in the config but log a warning that the cap won't enforce until the game exposes a matching method/dict.

## 0.1.0

- Initial release.
- Per-upgrade `Enabled` and `MaxStacks` config, defaults `false` / `5`.
- Reflection-based discovery of `StatsManager` upgrade methods.
- Host-to-client sync via Photon room properties (`Sync.SyncToClients`).
- Past-cap consumption is a no-op on the stat; the upgrade item is still destroyed.
