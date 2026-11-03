# SchalldaempferFuerAlle (suppressors for all)

Adjusts the values of **all** suppressors at once – including those that other mods (WTT-Armory & co.) only add to the database later. For every value you choose **how** it is changed: not at all, set to a fixed number, added to, or by percent.

## How this mod was made (AI disclosure)

This mod was **programmed by Claude**, an AI assistant made by Anthropic.
**Kaan was the tester**: Kaan has no programming background, played with the mod
on SPT 4.1.6, reported what went wrong, and Claude fixed it. No human programmer
has reviewed the code. It is open source so anyone can check it — if you find a
bug, please open an [issue](https://github.com/Kaanori/spt-schalldaempferfueralle/issues).

## Requirements

- SPT 4.1.6 (server).
- Replaces BetterSuppressors (no longer needed). Covers WTT-Armory and other mods' suppressors too.

## Installation

1. Download `SchalldaempferFuerAlle-1.1.0.zip` from the [Releases](https://github.com/Kaanori/spt-schalldaempferfueralle/releases) page.
2. Extract it **into your SPT game folder** (the folder that contains `EscapeFromTarkov.exe`).
   The ZIP already has the right layout; the mod ends up in `SPT_Runtime/user/mods/SchalldaempferFuerAlle/`.
   On Windows, make sure "Extract All" does not add an extra subfolder.
3. Restart the SPT server.

## Uninstall

Delete the mod's files from `SPT_Runtime/user/mods/SchalldaempferFuerAlle/` (settings: `BepInEx/config/` for client mods) and restart the SPT server.

## Why this is needed

**BetterSuppressors** only catches part of them – measured on 2026-09-20: **76 of 112** suppressors; the missing ones come from **WTT-Armory**, which loads later. It can also only set fixed values. Since 1.1.0 this mod does everything itself; **BetterSuppressors is no longer needed** (if it is still installed, its numbers are taken back).

## Settings: `config.json` next to the DLL

    SPT_Runtime/user/mods/SchalldaempferFuerAlle/config.json

The file is created on the first server start and contains a short guide in English and German. One block per value:

    "Recoil": { "Modus": "Prozent", "Zahl": 50, "Min": -50, "Max": 50 }

| Mode (`Modus`) | Result |
|---|---|
| `aus` / `off` | original value |
| `Wert` / `set` | `Zahl` as a fixed value |
| `Plus` / `add` | original + `Zahl` |
| `Prozent` / `percent` | original × (1 + `Zahl`/100) |

`Min`/`Max` limit the result. Percent keeps the sign: ergonomics −20 with −50 % gives −10 (better); recoil −6 with +50 % gives −9 (better).
`Ausnahmen` (exceptions): template IDs that keep their original values. `NurDiese` (only these): if not empty, **only** these IDs are changed.
Restart the server after editing.

**Original** = the value a suppressor would have without this mod, including other mods' changes (e.g. SVM halving weight). Plus/percent are always calculated from that original, never twice.

| Value | better is | base game | effect in game |
|---|---|---|---|
| `Ergonomics` | higher | −30 … −2 | sum over all attachments |
| `Recoil` | lower | −15 … −1 % | sum, never below −100 |
| `Accuracy` | higher | −5 … +1 % | sum, whole number in the client |
| `Velocity` | higher | 0.2 … 1.2 % | muzzle velocity |
| `Loudness` | lower | −39 … 0 | only with SAIN: bots' hearing range |
| `DurabilityBurn` | lower | 1.2 … 2.15 | factor, wear per shot |
| `HeatFactor` | lower | 1.05 … 1.34 | factor, heat per shot |
| `CoolFactor` | **higher** | 1.0 … 1.2 | factor, cooling (below 1 = slower!) |
| `Weight` | lower | 0.13 … 1.1 kg | weight |

The exact ranges of all suppressors (191 with WTT-Armory) are written to the server log after every start (`Original … -> now …`).

## Upgrading from 1.0.0

Without a `config.json` the mod reads BetterSuppressors' `config.json` (if present), then `schalldaempfer.json`, then built-in defaults (Ergonomics 0, DurabilityBurn 1.0, HeatFactor 0.90, CoolFactor 0.92, Accuracy 6, Velocity 6 – mode `set`; Recoil, Loudness, Weight `off`) and creates `config.json` **once** from that – the numbers stay exactly the same as in 1.0.0. A broken `config.json` is reported loudly in the log and never overwritten.

## How it works

`IOnLoad` passes: **999** save all suppressor values · **1001** take back BetterSuppressors' fixed numbers (WTT creates many suppressors as copies of base-game ones) · **last** apply the settings · **first client request** once more for anything added even later.

## Building from source

Needs the .NET 10 SDK and an SPT 4.1.6 installation to build against.

```bash
./build.sh             # Linux: build only
./build.sh --install   # build and copy into the game folder
```
```powershell
.\build.ps1            # Windows
.\build.ps1 -Install
```

The game folder is set in `Directory.Build.props` (Windows default `C:/SPT`)
or via the environment variable `SPT_DIR`.

## Support

Bugs and questions: [GitHub issues](https://github.com/Kaanori/spt-schalldaempferfueralle/issues).

## License

MIT — see [LICENSE](LICENSE).
