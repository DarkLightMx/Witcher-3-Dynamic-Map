# Nexus page text (draft)

## Name
Witcher 3 Dynamic Map

## Short summary
A live second-screen map for The Witcher 3 Next-Gen: a small script mod reports your position, discovered map pins and tracked quest, and a Windows app shows them on a full-size map. Works on foot, on a horse and in a boat. No game memory reading, no .NET install.

## Description
**Witcher 3 Dynamic Map** puts a live map on your second monitor.

A tiny script mod writes your position, the map pins you have already discovered and your tracked quest to the game's own script log. The included Windows app reads that log and draws everything on a full-size map.

**Features**
- Live marker that follows Geralt on foot, on Roach, in a boat and through fast travel
- All map pins straight from your game (base game, Hearts of Stone, Blood and Wine)
- Discovered places shown faded or hidden - your choice
- Tracked quest in the side panel, quest markers on the map
- Filter places of interest by type, with icons
- World detected automatically (Velen, Skellige, Kaer Morhen, Toussaint, White Orchard)
- Dark Windows 11 style UI, hideable side panel (Ctrl+B)
- English and Russian interface (switch in the header, English by default)

**Requirements**
- The Witcher 3 Next-Gen update (4.00+), tested on 5.0 - uses the script annotations, no Script Merger needed
- Windows 10 1809+ / Windows 11, 64-bit - the app is self-contained, no .NET install needed
- Game launch option `-debugscripts` (needed so that the game writes its script log)

**How it works / safety**
The app only reads the text file `Documents\The Witcher 3\scriptslog.txt`. It does not read or write the game's memory and does not change any game file.

The app is fully offline: the map tiles are included in the archive (about 650 MB), nothing is downloaded.

See the README in the archive for installation steps.

## Requirements section (Nexus "Requirements" box)
- The Witcher 3: Wild Hunt Next-Gen (4.00+)

## Tags / category
Category: Utilities (or Gameplay / Miscellaneous). Tags: map, interactive map, second screen, quest tracker, utility.

## Permissions / credits (Nexus "Credits" box)
- CD PROJEKT RED - The Witcher 3: Wild Hunt
- untamed0 (witcher3map), BaHTsIzBEdEvi / root-BB (Hearts of Stone map) - interactive map project, place icons
- DesignGears, hhrhhr - extraction of the map tiles (witcher3map-maps)
