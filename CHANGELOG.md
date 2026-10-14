# Changelog

All notable changes to MoniMS are listed here.
The release workflow copies the section for each version into the GitHub Release notes.
Write upcoming changes under **[Unreleased]**; `release.ps1` moves them into the new version.

## [Unreleased]

## [0.5.2] - 2026-10-14

### Changed
- Widgets that snap next to each other now sit half as far apart.

## [0.5.1] - 2026-10-14

### Added
- **Widgets snap into place** when you move them: next to another widget they keep a small gap and line up their top or bottom edges, below one they line up their left or right edges, and near the screen edge they keep a neat margin. Drag a little further to pull a widget away.

## [0.5.0] - 2026-10-14

### Added
- **Media widget**: see what's playing in Spotify, YouTube or YouTube Music (in your browser or the app) and control it from your desktop: previous, play/pause, next and volume. It shows the album art, title, artist and how far into the song you are.
  - Click or drag the time bar to jump to any point in the song or video (when the app allows it, as YouTube and Spotify do).
  - Pick the color of the play button, time bar and volume bar from a color table in **Settings → Media widget**, or keep the Windows accent color.
  - The volume slider changes only the playing app's volume, so other sounds stay as they are. You can switch it to the Windows volume in **Settings → Media widget**. Click the speaker to mute, or scroll over the widget.
  - With more than one player open, it shows the one you started last. Click the app name on the widget to switch.
  - Turn it on in the new **Settings → Media widget** tab or with **Show media widget** in the tray menu. Move & resize and the Dark, Light and Glass styles work like the other widgets.
- Presets can save the media widget too (shown or hidden, position, size, style, color). Overwriting a preset made with an earlier version adds the media widget to it.

### Removed
- The **Themes** tab, which imported themes and applied them to the taskbar, Start menu, Windows Terminal, Discord, Komorebi and YASB. Anything a theme already changed stays as it is. To undo it, press **Reset** in **Settings → Themes** before you update, or change it back in each app. Imported themes are left in `%APPDATA%\MoniMS\themes`, and you can delete that folder.

## [0.4.2] - 2026-10-13

### Changes
- Maintenance update

## [0.4.1] - 2026-10-13

### Changed
- MoniMS starts faster. It reads the motherboard name directly instead of asking WMI, which could take several seconds right after Windows starts, and the app now comes precompiled.

### Fixed
- Theme images such as the Gruvbox Start button icon were loaded from GitHub every time, so they stayed blank when the internet or GitHub was unreachable. MoniMS now uses the copy that comes with the theme. Apply the theme again to get the fix.
- If the file widget had a folder on a network drive that wasn't connected, MoniMS could freeze for up to a minute when it started, when you opened Settings, or when you applied a preset.
- Renaming a file by changing only upper or lower case (for example `a.txt` → `A.txt`) didn't show in the file widget.
- The update check right after Windows starts often ran before the internet was ready and then didn't try again. It now tries again a few times.
- On PCs with only a built-in GPU (Intel, AMD), the info widget always showed "VRAM 0 B". It now shows the shared GPU memory in use, like Task Manager.
- Uptime said "1 days" or "1 hours". It now says "1 day", "1 hour".

## [0.4.0] - 2026-10-12

### Added
- **File widget**: a small File Explorer on your desktop. Choose which folders it shows in the new **Settings → File widget** tab, then click one on the left of the widget to see what's inside.
  - Looks like File Explorer's **Details** view with **Name** and **Extension** columns. Click a column header to sort, or drag the line between the headers to resize.
  - Use it like File Explorer: double-click to open, go back or up, drag files in and out, copy, cut, paste, rename (F2), delete to the Recycle Bin and make new folders. Right-click for more, or choose **Show more options** for the full Windows menu.
  - Hidden files follow your File Explorer setting, and the list updates by itself when files change.
  - Turn on **Move & resize** (or **Edit widgets** in the tray menu) to move it or drag an edge to resize it. Styles: Dark, Light, Glass.
- Presets can save the file widget too (folders, position and size). Overwriting a preset made with an earlier version adds the file widget to it.
- **Show file widget** in the tray menu.

### Changed
- The info widget's **Graph color** is now picked from a color table (12 hues × 6 shades plus grays) instead of typing a `#RRGGBB` code. Tick **Use Windows accent color** to follow the Windows accent color again. Colors you typed before are kept.

## [0.3.0] - 2026-10-09

### Added
- **Image widget**: show a picture or an animated GIF on your desktop. Choose it in the new **Settings → Image widget** tab.
  - Turn on **Move & resize** (or **Edit widgets** in the tray menu), then drag it to move it or drag an edge or corner to resize it. The picture keeps its shape.
  - Change its size, opacity and rounded corners. Phone photos are turned the right way up.
  - MoniMS keeps its own copy of the picture, so you can move or delete the original.
  - Animated GIFs pause while a window fully covers them or the PC is locked, so they use no CPU then.
- Presets can save the image widget too (picture, position and size). The preset preview shows it. Overwriting a preset made with an earlier version adds the image widget to it.
- **Show image widget** in the tray menu.

### Changed
- The tray menu's **Edit widget position** is now **Edit widgets (move & resize)** and unlocks both widgets.

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
