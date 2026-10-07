# Changelog

All notable changes to MoniMS are listed here.
The release workflow copies the section for each version into the GitHub Release notes.
Write upcoming changes under **[Unreleased]**; `release.ps1` moves them into the new version.

## [Unreleased]

## [0.1.0] - 2026-10-08

First public release.

### Added
- **Info widget** pinned to the desktop: CPU / RAM / GPU usage with 60-second graphs, drives, local IP and network speed, Windows version, hardware names, uptime.
  - Always click-through. Use "Edit position" to move it.
  - Styles: Dark, Light, Glass. You can change the background opacity (text stays sharp), the font and the graph color, and choose which sections to show and in what order.
- **Presets**: save and switch wallpaper (per monitor), theme colors, desktop icon layout and widget layout. The Presets tab shows a preview of each preset.
- **Taskbar & Start themes**: import Windhawk theme packages from a GitHub link, a .zip link or a local file, then apply them. Your previous settings are backed up first, so you can press Restore to undo.
- Settings window with light / dark mode, tray menu, and an option to start with Windows.
- Installer with automatic updates from GitHub Releases.
