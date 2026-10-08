# Changelog

All notable changes to MoniMS are listed here.
The release workflow copies the section for each version into the GitHub Release notes.
Write upcoming changes under **[Unreleased]**; `release.ps1` moves them into the new version.

## [Unreleased]

## [0.2.2] - 2026-10-08

### Added
- Themes can now restyle **Komorebi**: the color theme, borders, padding, transparency and animation are added to your `komorebi.json` (your workspaces and rules are kept). A running komorebi loads the new settings right away.
- Themes can now restyle **YASB**: the theme's `config.yaml` and `styles.css` are put into your YASB folder, and YASB reloads by itself.
- If a theme has more than one theme for the same app (for example two Discord themes or two YASB bars), you can pick which one to apply.

### Changed
- Applying a Discord theme turns off the Discord theme you applied earlier from the library, so the two don't mix. Themes you added yourself are left alone.
- Applying a preset no longer freezes MoniMS. Parts that are already as saved (theme colors, icon positions) are skipped, so switching presets is also faster.
- Importing a theme from a file or folder no longer freezes the Settings window.

### Fixed
- Discord themes shared without a `.css` extension (for example "Discord/Current Theme" in dotfiles repos) were not found.
- After importing from a file or folder, the status stayed on "Analyzing...".

## [0.2.1] - 2026-10-08

### Added
- Themes can now restyle **Windows Terminal** (color schemes, font, opacity, padding; your profiles and key bindings are kept).
- Themes can now restyle **Discord** through Vencord, Vesktop, Equicord or BetterDiscord.
- Import a single `.css` Discord theme file.
- Warnings when a theme's font is not installed.
- **Reset** button next to the library's refresh button: goes back to how Windows looked before any library theme was applied (taskbar, Start menu, Terminal, Discord and wallpaper) in one click.
- ⓘ next to "Import a theme": hover to see which files can be imported.
- Importing a file that is not a theme now shows an **Invalid file format** warning.

### Changed
- The info widget now takes up the same share of the screen on every resolution and scaling (based on how it looks on a 2880×1800 screen at 200%).
- The info widget is narrower by default: 33% of the screen width instead of 38%.
- The info widget is taller by default: 77% of the screen height instead of 72%.
- The "Taskbar & Start" tab is now called **Themes**.
- **Restore** also undoes Terminal and Discord changes.
- Themes imported with an older version are re-analyzed automatically.

### Fixed
- The info widget could stay too small after changing the screen resolution or scaling.
- **Install mod** opened an empty editor window on Windhawk 1.x. It now opens the mod's page on windhawk.net and tells you what to search for in Windhawk.
- MoniMS used a lot of CPU in the background (up to half a CPU core when many apps use the GPU), and the widget then updated only every 2 seconds. It now uses well under 1% CPU and about 100 MB less memory, and updates every second again.
- The local IP in the widget did not update when it changed on the same network adapter.

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
