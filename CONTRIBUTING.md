# Building & Contributing

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download)
- A R.E.P.O. install (for game DLLs — BepInEx is pulled via NuGet automatically)

## Building

**Windows:**
```bash
dotnet build UpgradeLimiter.csproj --configuration Release
# Override game path if not the default Steam location:
dotnet build UpgradeLimiter.csproj --configuration Release /p:GameDir="D:\Games\REPO"
```

**Linux (Steam):**
```bash
./package.sh
# Override game path:
GAME_DIR="/path/to/steamapps/common/REPO" ./package.sh
```

Output DLL: `bin/Release/netstandard2.1/UpgradeLimiter.dll`

## Packaging for Thunderstore

A 256×256 `icon.png` must exist at the repo root before packaging (not committed — create your own).

```bash
./package.sh    # builds and zips in one step
```

Outputs `UpgradeLimiter-<version>.zip` ready for Thunderstore upload.

**Manual zip** (no subdirectory):
- `manifest.json`
- `icon.png`
- `README.md`
- `bin/Release/netstandard2.1/UpgradeLimiter.dll`

## Releasing

1. Bump `version_number` in `manifest.json` (`package.sh` reads it automatically)
2. Add an entry to `CHANGELOG.md`
3. Run `./package.sh` to build and package
4. Upload the zip to [Thunderstore](https://thunderstore.io/c/repo/) — each release requires a new version number

## How discovery works

On startup the plugin discovers cap-able upgrades in two passes:

1. **Base game** — a hardcoded table in `src/Plugin.cs` maps the 13 vanilla `PunManager` upgrade methods to their corresponding `Dictionary<string, int>` field on `StatsManager`. The mapping is hardcoded because the method/dict names don't follow a derivable rule (e.g. `UpgradePlayerSprintSpeed` writes to `playerUpgradeSpeed`, `UpgradePlayerEnergy` writes to `playerUpgradeStamina`, and `UpgradeDeathHeadBattery` / `UpgradeMapPlayerCount` drop the `Player` infix entirely).
2. **Modded** — every loaded assembly is scanned for methods matching `int Upgrade*(string, int)`. For each match, the method body's IL is scanned for the first `ldfld`/`stfld` referencing a `Dictionary<string, int>` field on `StatsManager`. If found, that field becomes the count source for the cap; if not, the method is skipped silently (it's just some unrelated `Upgrade*` method).

If you're adding a modded upgrade and want UpgradeLimiter to pick it up automatically, follow the base-game pattern: name the method `Upgrade<Something>`, give it the `(string steamID, int value = 1)` signature, and have it touch a `Dictionary<string, int>` field on `StatsManager` before doing anything else.
