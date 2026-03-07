# VRC JPEG Auto Generator

Windows desktop app to convert game screenshots from PNG to JPEG with tray-resident monitoring.

## Current behavior
- PNG -> JPEG conversion
- PNG post action:
  - `Keep` (do nothing)
  - `Delete` (delete source PNG after successful JPEG save)
- Duplicate JPEG handling is fixed to `Overwrite`
- DryRun is supported (history is not saved in DryRun)
- Game-exit trigger monitoring (tray resident)

## Runtime data location
Application data is stored here (portable distribution does not change this):

`%LOCALAPPDATA%\VRCJpegAutoGenerator`

Includes:
- settings
- processing history (SQLite)
- logs

## Build and portable package
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Configuration Release
```

Artifacts:
- folder: `publish\win-x64\`
- zip: `dist\VRCJpegAutoGenerator-portable.zip`

Zip layout:
- one top-level folder: `VRCJpegAutoGenerator\...`
- excludes `*.pdb`
- includes `readme.txt` and `license.txt` from `distribution\`

## Repository layout
- `src/` application source
- `tests/` test projects
- `scripts/` build/package scripts
- `docs/` specs and internal design notes
  - `docs/spec.md`
  - `docs/implementation-tasks.md`
  - `docs/portable-distribution.md`
- `distribution/` release-facing text assets (`readme.txt`, `license.txt`, `booth_content.txt`)

## Notes
- No installer is required; distribution is portable zip.
- App can still run at Windows startup and remain tray-resident.

