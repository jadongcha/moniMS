# Changelog

All notable changes to MoniMS are listed here.
The release workflow copies the section for each version into the GitHub Release notes.
Write upcoming changes under **[Unreleased]**; `release.ps1` moves them into the new version.

## [Unreleased]

## [0.2.0] - 2026-10-08

### Added
- Themes can now restyle **Windows Terminal** (color schemes, font, opacity, padding; your profiles and key bindings are kept).
- Themes can now restyle **Discord** through Vencord, Vesktop, Equicord or BetterDiscord.
- Import a single `.css` Discord theme file.
- Warnings when a theme's font is not installed.
- **Reset** button next to the library's refresh button: goes back to how Windows looked before any library theme was applied (taskbar, Start menu, Terminal, Discord and wallpaper) in one click.
- ⓘ next to "Import a theme": hover to see which files can be imported.
- Importing a file that is not a theme now shows an **Invalid file format** warning.

### Changed
- The "Taskbar & Start" tab is now called **Themes**.
- **Restore** also undoes Terminal and Discord changes.
- Themes imported with an older version are re-analyzed automatically.

### Fixed
- **Install mod** opened an empty editor window on Windhawk 1.x. It now opens the mod's page on windhawk.net and tells you what to search for in Windhawk.

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
