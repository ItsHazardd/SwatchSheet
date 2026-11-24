# Putting out a new version

Releases are made automatically by GitHub (the "Release" workflow in `.github/workflows`).

1. Change `VERSION` to the new number (for example `1.1.5`).
2. Add a `## 1.1.5` section at the top of `CHANGES.md` saying what changed. That text becomes
   the release notes.
3. Commit and push to `main`.

GitHub then checks whether `v1.1.5` already exists. If not, it runs the tests, builds the
program, zips it, makes its fingerprint file and publishes the release with both attached.
It takes a few minutes. Watch it in the **Actions** tab: a green tick means it's out, a red
cross means something failed and nothing was released (GitHub also emails you).

Pushes that don't change `VERSION` don't make a release, so small fixes to docs are safe.

Within a day, every copy that has update checks on sees the new version. Anyone can also click
the version number in the bottom-left corner, then "Check for updates", to see it straight away.

## Doing it by hand (only if the workflow is broken)
Run `build.cmd`, then on GitHub: Releases, Draft a new release, tag `v1.1.5`, and attach BOTH
`SwatchSheet-1.1.5.zip` and `SwatchSheet-1.1.5.zip.sha256`. Without the fingerprint file the
updater ignores the release on purpose.

Never replace the zip on an existing release: its fingerprint would no longer match, and
updates would be refused. Make a new version number instead.
