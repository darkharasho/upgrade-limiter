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
