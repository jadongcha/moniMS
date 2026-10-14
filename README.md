# MoniMS

**A desktop companion for Windows 11:** switchable desktop presets, a live system-info widget, a picture/GIF widget, a file widget and a media widget for Spotify and YouTube.

[한국어 안내 →](README.ko.md)

## Download

Get **`MoniMS-win-Setup.exe`** from the [latest release](../../releases/latest) and run it.

- **Install:** takes a few seconds, needs no administrator rights, and adds MoniMS to the Start menu.
- **.NET 10:** if the .NET 10 Desktop Runtime is missing, the setup installs it for you.
- **Updates:** MoniMS updates itself. You get a notification when a new version is out.

> **"Windows protected your PC"?** MoniMS is not code-signed yet, so SmartScreen may warn you the first time. Click **More info → Run anyway**.

**Requirements:** Windows 11 (x64).

## Features

### 🖥 Info widget
A clean widget that sits on your desktop, behind your windows.

- **What it shows:** CPU, RAM and GPU usage with 60-second graphs, used and free space per drive, local IP and download/upload speed, Windows version and build, CPU/GPU/board names, and uptime.
- **Same size on every screen:** the widget takes up the same share of the screen on any resolution or scaling.
- **Always click-through:** icons and files behind the widget stay clickable. Turn on **Edit position** (tray menu or Settings) to drag it somewhere else.
- **Styles:** Dark, Light, Glass.
- **Customizable:** background opacity (text stays sharp), font (JetBrains Mono built in), graph color, and which sections to show and in what order.

### 🖼 Image widget
Put a picture or an animated GIF on your desktop, behind your windows.

- **Pick a picture:** in **Settings → Image widget**, press **Choose image…** (PNG, JPG, GIF, BMP, WebP). MoniMS keeps its own copy, so you can move or delete the original. Phone photos are turned the right way up.
- **Resize by dragging:** turn on **Move & resize** (or **Edit widgets** in the tray menu), then drag the picture to move it or drag an edge or corner to resize it. The picture grows and shrinks with the widget and keeps its shape.
- **Also adjustable:** size, opacity and rounded corners.
- **Light on CPU:** like the info widget it is click-through. GIFs pause while a window fully covers them or the PC is locked.

### 📁 File widget
A small File Explorer on your desktop, behind your windows.

- **Pick folders:** in **Settings → File widget**, press **Add folder…**. They are listed on the left of the widget. Click one to see what's inside.
- **Looks like File Explorer's Details view:** each file shows its name and its extension. Click a column header to sort, or drag the line between the headers to resize.
- **Works like File Explorer:** double-click to open files and folders, use Back and Up (or Backspace, Alt+←/→/↑), drag files in and out, copy/cut/paste (Ctrl+C/X/V), rename (F2), delete to the Recycle Bin (Del) and make a new folder (Ctrl+Shift+N). Right-click for more. **Show more options** (or Shift+right-click) opens the full Windows menu.
- **Stays up to date:** the list updates by itself when files change. Hidden files follow your File Explorer setting.
- **Move & resize:** turn on **Move & resize** (or **Edit widgets** in the tray menu), then drag it or drag an edge or corner.
- **Styles:** Dark, Light, Glass, with adjustable background opacity.

### 🎵 Media widget
See what's playing and control it from your desktop.

- **Works with:** Spotify, YouTube and YouTube Music in your browser, the YouTube Music app, and other players that show up in the Windows volume flyout.
- **Shows:** album art, title, artist, the app and how far into the song you are. Click or drag the time bar to jump to any point.
- **Controls:** previous, play/pause, next, and a volume slider (click the speaker to mute, or scroll over the widget). By default the slider changes only the playing app's volume, so other sounds stay as they are. In **Settings → Media widget** you can make it change the Windows volume instead.
- **More than one player open?** The widget shows the one you started last. Click the app name on the widget to switch.
- **Turn it on** in **Settings → Media widget** or with **Show media widget** in the tray menu. Move & resize and styles work like the other widgets, and you can pick the color of the play button and bars there.

### 🎨 Presets
Save your whole desktop look and switch between looks in one click.

- **What a preset saves:** wallpaper (per monitor, copied so it survives deletion), dark/light mode and accent color, desktop icon positions, the info widget layout, the image widget (its picture is copied into the preset too), the file widget (folders, position and size) and the media widget (position, size, style and color).
- **Switching:** apply a preset from the tray menu or from **Settings → Presets**. Click a preset to preview it first.

> Icon layouts restore **positions only**. Files or shortcuts you deleted from the desktop are not recreated.
> For icon layouts to stick, turn off desktop right-click → **View → Auto arrange icons**.

## Using MoniMS

- **Tray icon:** MoniMS lives in the notification area. Right-click the icon for presets, widget options and settings. Double-click it to open Settings.
- **Settings window:** has a light/dark switch in the top-right corner.
- **Start with Windows:** turn it on in **Settings → General**.

## Your data & privacy

- **Data:** everything stays on your PC, in `%APPDATA%\MoniMS` (presets, settings, logs).
- **Network use:** MoniMS connects to the internet only to check GitHub for updates.
- **No tracking:** no telemetry, no accounts, no ads.
- **Uninstall:** use **Settings → Apps**. Your data folder is kept. Delete it yourself if you want a clean slate.

## Troubleshooting

| Problem | Try this |
|---|---|
| Widget disappeared after Explorer crashed | It comes back within a few seconds. If not, toggle **Show widget** in the tray menu. |
| Icon layout didn't restore | Turn off **Auto arrange icons**, then apply the preset again. |
| Something else | Check `%APPDATA%\MoniMS\logs\moniMS.log` and [open an issue](../../issues). |

## Building from source

See **[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)** (Korean) for build steps, architecture and the release process.

## License

MoniMS is released under the [MIT License](LICENSE). Bundled third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
MoniMS is not affiliated with Microsoft.
