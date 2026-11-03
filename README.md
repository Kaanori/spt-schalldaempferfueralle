# SchalldaempferFuerAlle (suppressors for all)

Applies the six suppressor values to **every** suppressor, including those
that other mods only add to the database later.

## How this mod was made (AI disclosure)

This mod was **programmed by Claude**, an AI assistant made by Anthropic.
**Kaan was the tester**: Kaan has no programming background, played with the mod
on SPT 4.1.6, reported what went wrong, and Claude fixed it. No human programmer
has reviewed the code. It is open source so anyone can check it — if you find a
bug, please open an [issue](https://github.com/Kaanori/spt-schalldaempferfueralle/issues).

## Requirements

- SPT 4.1.6 (server).
- Optional: BetterSuppressors (its `config.json` is used if present). Made for WTT-Armory, whose suppressors BetterSuppressors misses.

## Installation

1. Download `SchalldaempferFuerAlle-1.0.0.zip` from the [Releases](https://github.com/Kaanori/spt-schalldaempferfueralle/releases) page.
2. Extract it **into your SPT game folder** (the folder that contains `EscapeFromTarkov.exe`).
   The ZIP already has the right layout; the mod ends up in `SPT_Runtime/user/mods/SchalldaempferFuerAlle/`.
   On Windows, make sure "Extract All" does not add an extra subfolder.
3. Restart the SPT server.

## Uninstall

Delete the mod's files from `SPT_Runtime/user/mods/SchalldaempferFuerAlle/` (settings: `BepInEx/config/` for client mods) and restart the SPT server.

## Why this is needed

**BetterSuppressors** only catches part of them. Measured on 2026-09-20:
**76 of 112** suppressors. The missing 36 come from **WTT-Armory**.

The reason is load order: BetterSuppressors works in `IOnLoad` and is number
**30** in the sequence, while WTT-Armory only comes as number **34**. When
BetterSuppressors runs, the WTT suppressors simply do not exist yet.

The server has no later entry point; only `IOnLoad` and `IOnDIConstruct`
exist (checked in `SPTarkov.Server.Core.dll`).

## How it is solved

Two passes:

1. **On server start** (`IOnLoad`): catches everything that exists at that point.
2. **Once, on the first client request** (`IHttpListener`): by then **all**
   mods are guaranteed to have finished loading.

The second pass is independent of load order. It reports in the log how many
suppressors were only added afterwards.

`CanHandle` **always returns `false`**, so the mod never answers a request
itself; it only uses the call as a point in time. The counter uses
`Interlocked` because the server may check several requests at the same time.

## Settings

It stays at **one** file:

    SPT_Runtime/user/mods/BetterSuppressors/config.json

This mod reads it as well. If it is missing, `schalldaempfer.json` next to
the mod's own DLL is used, otherwise the built-in defaults.

| Key | Default | Original in the game |
|---|---|---|
| `Ergonomics` | 0 | −30 to −2 (mostly −21) |
| `DurabilityBurn` | 1.0 | 1.2 to 2.15 (mostly 1.75) |
| `HeatFactor` | 0.90 | 1.05 to 1.34 (mostly 1.14) |
| `CoolFactor` | 0.92 | 1.0 to 1.2 (mostly 1.0) |
| `Accuracy` | 6 | −5 to +1 (mostly 0) |
| `Velocity` | 6 | 0.2 to 1.2 (mostly 0.7) |

The values are set **absolutely**, not added on top, so afterwards every
suppressor has the same numbers.

Unlike BetterSuppressors, this mod reports an unreadable settings file
**loudly in the log** instead of silently falling back to defaults.

## Can BetterSuppressors go?

Yes. This mod does the same job and more. If you remove BetterSuppressors,
take its `config.json` with you first and place it as
`schalldaempfer.json` next to this DLL.

## Test status

Builds with 0 errors, 0 warnings. Whether all 112 are really set is shown
after server start by the log line "Nachgezogen: …" ("caught up"), and by the
cross-check on the running server:

    curl -sk -H "Cookie: PHPSESSID=<profile ID>" https://127.0.0.1:6969/client/items

(The response is zlib-compressed; suppressors are the templates with
`_parent` = `550aa4cd4bdc2dd8348b456c`.)

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
