# Putting out a new version

The app's updater reads the newest release on GitHub, so every release needs these exact pieces.

1. Make sure `VERSION` has the new number (for example `1.1.5`).
2. Double-click `build.cmd`. It runs the tests and stops if any fail. It makes:
   - `SwatchSheet-1.1.5.zip`: the program and README, ready to send.
   - `SwatchSheet-1.1.5.zip.sha256`: the fingerprint the updater checks.
3. On GitHub, open **Releases**, then **Draft a new release**.
4. **Tag:** `v1.1.5` (a v, then the exact number from `VERSION`). Target: `main`.
5. **Title:** `Swatch Sheet 1.1.5`. In the description, write a few lines on what changed.
6. **Attach both files** from step 2: the zip AND the .sha256. Without the fingerprint file, the
   updater ignores the release on purpose.
7. Leave "Set as a pre-release" unticked, then click **Publish release**.

Within a day, every copy that has update checks on sees the new version. Anyone can also click
the version number in the bottom-left corner, then "Check for updates", to see it straight away.

Don't re-upload a different zip to an existing release: its fingerprint would no longer match,
and updates would be refused. Make a new version number instead.
