# MoniMS

**A desktop companion for Windows 11:** switchable desktop presets, a live system-info widget, and one-click taskbar & Start menu themes.

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
- **Always click-through:** icons and files behind the widget stay clickable. Turn on **Edit position** (tray menu or Settings) to drag it somewhere else.
- **Styles:** Dark, Light, Glass.
- **Customizable:** background opacity (text stays sharp), font (JetBrains Mono built in), graph color, and which sections to show and in what order.

### 🎨 Presets
Save your whole desktop look and switch between looks in one click.

- **What a preset saves:** wallpaper (per monitor, copied so it survives deletion), dark/light mode and accent color, desktop icon positions, and the widget layout.
- **Switching:** apply a preset from the tray menu or from **Settings → Presets**. Click a preset to preview it first.

> Icon layouts restore **positions only**. Files or shortcuts you deleted from the desktop are not recreated.
> For icon layouts to stick, turn off desktop right-click → **View → Auto arrange icons**.

### 🧩 Taskbar & Start menu themes
Restyle the Windows 11 taskbar, Start menu and notification center with themes shared online, for example *Gruvbox Material*.

1. Install **[Windhawk](https://windhawk.net)**. It does the low-level work of restyling Windows.
2. In **Settings → Taskbar & Start**, paste a GitHub link, a `.zip` link, or browse to a local file, then press **Import**.
3. Choose which parts to apply. You can also pick one of the theme's wallpapers.
4. Press **Apply**. Windows asks for permission once.

- **Missing Windhawk mods:** if a part needs a Windhawk mod you don't have, MoniMS shows an **Install mod** link.
- **Undo:** your previous settings are backed up automatically. Press **Restore** to undo.
- **Custom scripts:** themes that contain custom scripts are applied without them, unless you allow the scripts.

## Using MoniMS

- **Tray icon:** MoniMS lives in the notification area. Right-click the icon for presets, widget options and settings. Double-click it to open Settings.
- **Settings window:** has a light/dark switch in the top-right corner.
- **Start with Windows:** turn it on in **Settings → General**.

## Your data & privacy

- **Data:** everything stays on your PC, in `%APPDATA%\MoniMS` (presets, imported themes, settings, logs).
- **Network use:** MoniMS connects to the internet only to download themes you import and to check GitHub for updates.
- **No tracking:** no telemetry, no accounts, no ads.
- **Uninstall:** use **Settings → Apps**. Your data folder is kept. Delete it yourself if you want a clean slate.

## Troubleshooting

| Problem | Try this |
|---|---|
| Widget disappeared after Explorer crashed | It comes back within a few seconds. If not, toggle **Show widget** in the tray menu. |
| Icon layout didn't restore | Turn off **Auto arrange icons**, then apply the preset again. |
| Theme applied but nothing changed | Make sure the matching mod is installed **and enabled** in Windhawk. |
| Something else | Check `%APPDATA%\MoniMS\logs\moniMS.log` and [open an issue](../../issues). |

## Building from source

See **[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)** (Korean) for build steps, architecture and the release process.

## License

MoniMS is released under the [MIT License](LICENSE). Bundled third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
MoniMS is not affiliated with Microsoft or Windhawk.
