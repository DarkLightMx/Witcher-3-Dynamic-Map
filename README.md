# Witcher 3 Dynamic Map

A live companion map for **The Witcher 3: Wild Hunt (Next-Gen, 5.x)**.

A small WitcherScript mod (`modMapSync`) writes the player position, the map pins of the current game (with their
"discovered" flag) and the tracked quest into the game's own script log. A Windows Forms app (.NET 10, x64) reads that
log and draws everything on a full-size map: a second-screen map that follows Geralt on foot, on a horse and in a boat.

The app never reads the game's memory and never touches game files, so it does not depend on the exact game build.

## Features

- Live player marker with smooth movement, camera follows Geralt (also through fast travel)
- All map pins of the player's game (base game, Hearts of Stone, Blood and Wine) with the discovered state
- Discovered places can be faded or hidden; filter by place type, with icons
- Tracked quest title and quest markers
- World (Velen, Skellige, Kaer Morhen, Toussaint, White Orchard) detected automatically
- Dark Windows 11 style UI, hideable side panel (Ctrl+B), English and Russian interface (English by default)

## How it works

```
 Witcher 3 + modMapSync  --->  Documents\The Witcher 3\scriptslog.txt  --->  Witcher 3 Dynamic Map.exe
 (LogChannel('MapSync', ...))        lines starting with "@@MS|"                 (reads new lines only)
```

Line format written by the mod ([modMapSync.ws](GameMod/mods/modMapSync/content/scripts/local/modMapSync.ws)):

| Line | Meaning |
|---|---|
| `@@MS\|L\|x\|y\|z` | Player position, up to 10 times per second while moving, once per second when standing |
| `@@MS\|B\|worldPath\|questTitle` | Start of a pin snapshot |
| `@@MS\|P\|type\|x\|y\|z\|discovered\|known` | One map pin |
| `@@MS\|E\|count` | End of the snapshot (the app applies a snapshot only when it is complete) |

A snapshot is written only when something changes (new place discovered, quest or world changed).
The mod uses the script annotations of the Next-Gen update (`@wrapMethod`, `@addMethod`, `@addField`), so Script Merger
is not needed. The game only writes the log when it is started with the **`-debugscripts`** launch option.

## Requirements

- Windows 10 1809+ / Windows 11, x64
- The Witcher 3 Next-Gen (4.00 or newer), tested on 5.0
- To build: .NET 10 SDK. A published release is self-contained and needs no .NET installation.

## Install the mod

Copy `GameMod\mods\modMapSync` into the `mods` folder of the game and add `-debugscripts` to the game's launch options.

## Build

```bash
dotnet build "Witcher 3 Dynamic Map.csproj" -c Release
```

The repository does **not** contain the map tiles and the place icons (see "Assets" below); without them the app
starts with an empty map and generic circle icons.

`build-release.ps1` builds the release package (`publish\`: single-file self-contained exe, `Icons`, `Data\Maps`).
It copies the tiles from `%LocalAppData%\Witcher3DynamicMap\Data\Maps`.

### Assets (not in the repository)

The app looks for them next to the exe, then in the parent folders (up to four levels) and finally in
`%LocalAppData%\Witcher3DynamicMap\Data`. A `Data` folder is used only if it contains tiles.

- **Map tiles**: `Data\Maps\<world>\<zoom>\<x>\<y>.png`, TMS order (row 0 at the bottom), made with
  `gdal2tiles.py -p raster`. World folders: `hos_velen`, `skellige`, `kaer_morhen`, `toussaint`, `white_orchard`.
  They are available from [witcher3map-maps](https://github.com/witcher3map/witcher3map-maps).
- **Icons**: PNG files in an `Icons` folder next to the exe, named like in the witcher3map icon set
  (`signpost.png`, `boat.png`, ...). The mapping from game pin types to file names is `PoiIcons.Files` in
  [Form1.cs](Form1.cs). The set is in `files/images/icons` of [witcher3map](https://github.com/root-BB/witcher3map).

## Project structure

| File | Content |
|---|---|
| [Form1.cs](Form1.cs) | Main window, map control (tiles, pins, player marker), icon loader, tile cache, world bounds |
| [UiKit.cs](UiKit.cs) | Theme, cards, toggle switch, language switch, POI type list with icons |
| [GameSync.cs](GameSync.cs) | Reads and parses `scriptslog.txt` |
| [Loc.cs](Loc.cs) | English / Russian texts, remembered in `%LocalAppData%\Witcher3DynamicMap\settings.json` |
| [GameMod/](GameMod) | The `modMapSync` game mod |

### Map coordinates

Each world is a rectangle in game coordinates stretched over the tile image (`MapWorld` in [Form1.cs](Form1.cs)).
The Velen / Hearts of Stone rectangle was fitted by matching the game's own road sign pins against the signposts of the
`hos_velen` map (all 112 matched, RMS about 8 game units). The other worlds use nominal bounds and have not been
checked as thoroughly.

## Known limitations

- Only the title of the tracked quest is shown, not individual objectives.
- The game provides pin types, not names, so places are listed by type.
- Skellige, Kaer Morhen, Toussaint and White Orchard are less verified than Velen.

## Credits and legal

This is an unofficial, non-commercial fan project, not affiliated with CD PROJEKT RED.
The Witcher 3: Wild Hunt and its assets are the property of CD PROJEKT RED.

- Place icons: [witcher3map](https://github.com/untamed0/witcher3map) by untamed0, Hearts of Stone version by
  BaHTsIzBEdEvi ([root-BB](https://github.com/root-BB/witcher3map)), with help from mcarver, Gerignak, DesignGears and
  hhrhhr (CC BY-NC-SA 4.0).
- Map tiles: [witcher3map-maps](https://github.com/witcher3map/witcher3map-maps), extracted from the game by
  DesignGears and hhrhhr.

The source code in this repository is under the [MIT License](LICENSE). The MIT license does **not** cover the
assets listed above, which keep their own terms (personal, non-commercial use).
