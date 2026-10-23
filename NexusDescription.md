SHORT DESCRIPTION
---------------------------
Recolour any employee locker with a colour slider, brightness and hex code (press E while looking at one); colours are kept across saves. Also copies each locker's clipboard (photo and name) onto its left and right sides, so you can see whose locker is whose from a row.


FULL DESCRIPTION (same layout as the page: headings in [size=4][b]...[/b][/size])
----------------

[size=4][b]Description[/b][/size]
Adds two quality-of-life features to employee lockers: full colour customisation, and clipboards visible from both sides. Recolour lockers to match your property's colour scheme, and see whose locker is whose from either side of a row, without walking round to check the front of every locker.

[size=4][b]Installation instructions[/b][/size]
1. Install [url=https://melonwiki.xyz/]MelonLoader[/url] 0.7.x into Schedule I and start the game once so it sets itself up.
2. Put "Locker Customization.dll" into the game's Mods folder.
3. Start the game and load your save. MelonLoader loads the mod automatically.

[size=4][b]Main features[/b][/size]
- Look at any locker and press E to open a colour panel on the left of the screen: a colour slider, a brightness slider, or type a hex code.
- Click "Apply to Locker" to recolour it, or "Restore to Default" to bring back the game's own role-tinted colour.
- Locker colours are kept across saves. The game doesn't save them, so the mod keeps its own side file and puts them back after every load. It never edits your save.
- Name tags on three sides: each locker's clipboard (the employee's photo and name) is copied onto its left and right sides too, and the copies stay in step when you reassign the locker.
- It only touches lockers, not employees or their appearance. (For recolouring employees, see Employee Customization.)

[size=4][b]Requirements[/b][/size]
- Schedule I, IL2CPP version (the normal Steam branch). Not for the Mono ("alternate") branch.
- MelonLoader 0.7.x.

[size=4][b]Troubleshooting[/b][/size]
- Your locker colours are saved in "LockerColorMod_Lockers.json", next to the DLL in the Mods folder. Delete it to reset every locker.
- To uninstall, delete "Locker Customization.dll". Your save is never changed.
- If the panel doesn't open or colours don't come back after a load, post the lines from MelonLoader/Latest.log that mention the mod in the comments.


CHANGELOG 1.0.1
---------------
- Fixed a short hitch (about 80 ms) every 5 seconds in big saves. The mod looked for new lockers by walking every object in the world that often; it now only looks again when something is placed or picked up in a property you own (plus a slow check every two minutes).
