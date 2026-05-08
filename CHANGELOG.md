# Changelog

## 0.2.0

- Prepopulate config sections for all 13 base-game upgrades, including ones not yet auto-discoverable via `StatsManager` reflection (CrouchRest, GrabThrow, TumbleClimb, TumbleWings, MapPlayerCount).
- Unpaired canonical entries appear in the config but log a warning that the cap won't enforce until the game exposes a matching method/dict.

## 0.1.0

- Initial release.
- Per-upgrade `Enabled` and `MaxStacks` config, defaults `false` / `5`.
- Reflection-based discovery of `StatsManager` upgrade methods.
- Host-to-client sync via Photon room properties (`Sync.SyncToClients`).
- Past-cap consumption is a no-op on the stat; the upgrade item is still destroyed.
