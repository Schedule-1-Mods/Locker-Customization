# Locker Customization

A MelonLoader mod for Schedule I that lets you recolour your employees' lockers.

- **Recolour a locker:** look at one and press **E**. A panel on the left lets you pick its colour with a colour slider, brightness and a hex code.
- **Kept across saves:** the game doesn't save locker colours, so the mod keeps its own side file and puts them back after every load. It never edits your save.
- **Name tags on three sides:** each locker's clipboard (the employee's photo and name) is copied onto its left and right sides too, and the copies stay in step when you reassign the locker.

It only touches lockers, not employees or their appearance.

## Install

1. Install MelonLoader 0.7.x for Schedule I.
2. Put `✽ Locker Customization.dll` in the game's `Mods` folder.

## Building

`dotnet build` builds straight into the game's `Mods` folder. If the game isn't installed at
`C:\Games 2\Steam Library\steamapps\common\Schedule I`, pass your path:

```
dotnet build -p:GameDir="D:\Games\Schedule I"
```
