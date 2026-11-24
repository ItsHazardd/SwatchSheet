# Swatch Sheet 1.1.4: testing checklist

Run `SwatchSheet.exe` from the 1.1.4 release folder.

## New in 1.1.4: updates
A. **Version button.** Bottom-left shows `v1.1.4`. Click it: a small panel with *Check for updates* and a tick box.
B. **Check by hand.** Click *Check for updates*. Until there's a release on GitHub it says you have the newest version, or that it couldn't reach GitHub if you're offline. Neither should crash or nag.
C. **Switch off.** Untick the box, close and reopen, click the version: the box is still unticked. Tick it again.
D. **Real update (after your first two releases).** Publish 1.1.4 on GitHub (see RELEASING.md), then later 1.1.5. Open 1.1.4: the bar "Swatch Sheet 1.1.5 is available" appears. Click *Update*: it downloads, restarts, and says "Updated to 1.1.5". Your colors and palettes are still there, and there's no `SwatchSheet.exe.old` left in the folder.
E. **What's new** on the bar opens the release page in your browser.

## From 1.1.3
1. **Old colors still there.** Open the app. Your sheet from 1.1.2 loads, and the date line under the title shows today's date.
2. **Logo gone.** The toolbar starts with "+ Add images". No icon or name in front of it.
3. **No Sample.** The Sample 1px/3x3/5x5 switch is gone. Hover an image: the magnifier has a small box around the middle pixel, and clicking picks exactly that pixel.
4. **Copy styles.** The copy box offers only Hex, CSS and RGB.
5. **Date.** Type something into the date line. Click *Today* and it goes back to today. Untick the box and the date fades. It disappears from the copy preview, and it's left out of pastes and the sheet image.
6. **Average.** Pick 2+ colors. The little dot on *Average* shows the mix. Click it (or press A): the mixed color is added at the end. Ctrl+Z removes it.
7. **Clicking away from typing.** Click into the title, then click the "Drop images here" area, the image list, or empty space in the sheet. Press R, and it should rainbow sort (not type an R).
8. **Ctrl+O while typing.** Click into the title and press Ctrl+O. The Add images window opens.
9. **Save palette.** Give the sheet a title and click *Save palette* (or Ctrl+S). Look next to SwatchSheet.exe: a `Palettes` folder holds a file with your title as its name.
10. **Replace prompt.** Click *Save palette* again with the same title. It asks before replacing. Say No, and nothing changes.
11. **Palettes list.** Change the title, remove a color, save a second palette. Click *Palettes*: both show, newest first, with color strips.
12. **Open a palette.** Click the first one. Its colors, title, date and description come back. Ctrl+Z brings back the sheet you had before.
13. **Open Palettes folder** (bottom of the list) opens the folder in File Explorer.
14. **Log moved.** The log is now `data\swatch-sheet.log`. There's no new `logs` folder.
15. **Zip.** `SwatchSheet-1.1.4.zip` (next to the release folder) holds only SwatchSheet.exe and README.txt, and `SwatchSheet-1.1.4.zip.sha256` sits beside it.

## Quick re-check of older stuff
16. Add images, hover (magnifier), click to pick, R to rainbow sort, drag a swatch to move it, Ctrl+Z.
17. *Copy colors*, then paste into OneNote: bold title, date, description, then colored codes. Paste into Notepad: codes only.
18. *Copy sheet image*, then paste into OneNote: title, then "date · N colors", then the description, then the swatches.
19. Clear, answer Yes, then Ctrl+Z brings everything back.

Automatic checks: `build.cmd` runs 80 tests (colors, copy formats, average, dates, palettes, saving, old-save loading, and updates: version compare, fingerprint checks, refusing bad downloads, swap and put-back) and won't build if any fail. `SwatchSheet.exe --self-test-update http://localhost:PORT/latest` runs a real update against a fake release server on this PC (it refuses any other address). `SwatchSheet.exe --self-test <folder> <stripes.png>` drives the real window (picking, sorting, average, date, click-away, palettes, undo, copying) and writes `report.txt`. It replaces whatever is on your clipboard.
