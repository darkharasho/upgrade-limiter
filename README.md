# UpgradeLimiter

A R.E.P.O. mod that caps how many of each player upgrade can be stacked. Each upgrade has its own enable toggle and max-stacks value.

When a player tries to consume an upgrade past the cap, the upgrade crystal is consumed normally but the stat does not increase. Decrements and resets pass through unchanged.

## Features

- Per-upgrade `Enabled` toggle and `MaxStacks` value (0–99)
- Covers all 13 base-game player upgrades (Health, Energy, ExtraJump, TumbleLaunch, TumbleClimb, TumbleWings, SprintSpeed, CrouchRest, GrabStrength, ThrowStrength, GrabRange, DeathHeadBattery, MapPlayerCount)
- Modded `UpgradePlayer*` methods are auto-discovered via IL scanning and added to the config
- Host-to-client sync via Photon room properties — only the host needs to configure caps
- Live config reload: changing limits mid-run takes effect on the next pickup, no level reload needed
- Compatible with [REPOConfig](https://thunderstore.io/c/repo/p/nickklmao/REPOConfig/) — adjust settings in-game

## Configuration

Config file: `BepInEx/config/darkharasho.UpgradeLimiter.cfg`

Each upgrade gets its own section:

```
[Health]
Enabled = false
MaxStacks = 5

[SprintSpeed]
Enabled = true
MaxStacks = 2
```

| Key | Default | Range | Description |
|-----|---------|-------|-------------|
| `Enabled` | `false` | — | When true, the upgrade is capped. When false, it behaves vanilla. |
| `MaxStacks` | `5` | `0–99` | Maximum stacks of this upgrade per player. `0` blocks every pickup (effectively disables the upgrade). |

Plus a sync section:

```
[Sync]
SyncToClients = true
```

When `SyncToClients = true`, the host pushes its caps to every client via Photon room properties and clients ignore their own local caps while in the room. When `false`, each client uses its own local config.

## Multiplayer

The cap is enforced on the host's authoritative state, so toggling caps on a client without host sync will not actually limit the upgrade — only the host's settings matter for enforcement.

## Dependencies

- [BepInExPack](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/)

## Installation

Install via [Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager) / r2modman, or manually place `UpgradeLimiter.dll` in `BepInEx/plugins/UpgradeLimiter/`.

## Building

```bash
GAME_DIR="/path/to/REPO" ./package.sh
```

Builds the DLL and produces a Thunderstore-ready zip. Install the zip via r2modman.
