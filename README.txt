SWATCH SHEET
============
Pick colors out of your images and build a color sheet you can copy and paste anywhere.
Free, offline, no install. Just double-click SwatchSheet.exe.


FIRST TIME OPENING IT
---------------------
Windows may show a blue "Windows protected your PC" box. That's normal for small free
programs that aren't from a big company. It doesn't mean anything is wrong with the file.
- Click "More info", then "Run anyway". You only need to do this once per version.
- Or, before unzipping: right-click the zip, choose Properties, tick "Unblock", click OK,
  then unzip. The box won't show up at all.


GETTING IMAGES IN
-----------------
- Click "Add images" (Ctrl+O), or drag image files or a whole folder onto the window.
- Ctrl+V pastes a screenshot (Win+Shift+S) or an image you copied from a browser.
- You can also drag images onto SwatchSheet.exe itself.
- Open as many images as you like. They're listed on the left; click one to switch.
  The x on a thumbnail only closes it in the app. Your file is never touched.

PICKING COLORS
--------------
- Hover over the image: a magnifier shows the pixels up close. The boxed square in the
  middle is the exact pixel you'll get.
- Click to add that color to the sheet. Clicking a color that's already there won't add it twice.
- Scroll wheel zooms in and out at the cursor. Drag (left or right mouse) to move around.
  F = fit to window, 1 = real size.

THE SHEET
---------
- Click "Untitled sheet" at the top right to give your sheet a title, and the line under it
  to add a description. Enter finishes typing; in the description, Shift+Enter starts a new line.
- The date line under the title starts as today's date. Type anything you like there, click
  "Today" to reset it, or untick the box to leave the date off.
- Title, date and description show on the sheet image and when you paste into OneNote or Word.
- To stop typing, press Enter or Esc, or just click anywhere else (the image area, the image
  list...). The shortcut keys work again straight away.
- Rainbow sort (R) puts the colors in rainbow order: red, orange, yellow, green, blue, indigo,
  violet, pinks. Whites, greys and blacks go at the end, light to dark.
- Average (A) mixes every color on the sheet into one and adds it at the end. Handy for
  finding the "overall" color of a set of similar shades. (Mixing a whole rainbow gives a
  muddy brown-grey, that's just how mixing works.)
- Drag a swatch onto another to move it there.
- Click a swatch to copy just that one code.
- Hover a swatch and click its x to remove it.
- Undo (Ctrl+Z) and Redo (Ctrl+Y) cover adding, removing, sorting, averaging, moving,
  clearing and opening a palette.
- Your sheet and open images are remembered when you close the app.
  (Pasted images aren't saved, since there's no file for them.)

PALETTES
--------
Keep as many sheets as you want:
- "Save palette" (Ctrl+S) saves the sheet into the Palettes folder, named after its title.
  If a palette with that name already exists, it asks before replacing it.
- "Palettes" lists everything you've saved, newest first, with a strip of its colors.
  Click one to open it. Your current sheet is replaced, and Ctrl+Z brings it back.
- "Open Palettes folder" (at the bottom of that list) shows the folder in File Explorer.
  Each palette is one small file you can copy, back up or send to a friend: they drop it in
  their own Palettes folder and it shows up in their list.

COPY AND PASTE
--------------
Pick a style (Hex, CSS, RGB). The box shows exactly what you'll copy, with a chip of each
color. Then:
- "Copy colors" (Ctrl+C): in OneNote, Word, Google Docs or email it pastes your title, date
  and description, then a colored block in front of each code. In Notepad, Discord or code
  editors it pastes just the plain codes.
- "Copy sheet image" (Ctrl+Shift+C): the whole sheet as a picture, every swatch with its
  codes. Paste it into OneNote, Discord, Paint, anywhere.
- You can also paste hex codes INTO Swatch Sheet: copy text with codes like #FF5A5F or
  rgb(255, 90, 95) from anywhere, press Ctrl+V, and they're added to the sheet.

UPDATES
-------
Swatch Sheet can update itself from its GitHub page.
- When it opens (at most once a day) it asks GitHub for the newest version number.
  Nothing about you is sent. If there's a newer version, a bar appears at the top:
  "Update" downloads and installs it, "Not now" hides the bar.
- Before installing, it checks the download against a fingerprint published with the
  release. If they don't match exactly, nothing is installed.
- It restarts by itself. Your sheet, palettes and settings are never touched.
- Click the version number in the bottom-left corner to check by hand, or to switch the
  automatic check off.
- If the program is in a folder Windows won't let it write to (like Program Files), it
  can't update itself; it offers to open the download page instead.
- If an update ever misbehaves: the previous version is kept as "SwatchSheet.exe.old" until
  the new one has started properly. Rename it back to SwatchSheet.exe to return to it.
- Download page: https://github.com/ItsHazardd/SwatchSheet/releases

WHERE THINGS ARE SAVED
----------------------
Everything stays next to the program, so you can move the folder to another PC:
- Palettes\               your saved palettes
- data\sheet.json         the current sheet and open images (sheet.json.bak is the previous save)
- data\settings.json      whether to check for updates
- data\swatch-sheet.log   the log file. Send this if something goes wrong.
If that folder can't be written to, they go to %LOCALAPPDATA%\Swatch Sheet instead.
Sending the program to someone? Send just SwatchSheet.exe and this README. Keep your own
data and Palettes folders, unless you want to share them.
