# Changelog

## 0.1.0

- Initial release.
- Per-upgrade `Enabled` and `MaxStacks` config, defaults `false` / `5`.
- Reflection-based discovery of `StatsManager` upgrade methods.
- Host-to-client sync via Photon room properties (`Sync.SyncToClients`).
- Past-cap consumption is a no-op on the stat; the upgrade item is still destroyed.
