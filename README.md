# MoniMS

**A desktop companion for Windows 11:** switchable desktop presets, a live system-info widget, and one-click themes for the taskbar, Start menu, Windows Terminal and Discord.

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

### 🎨 Presets
Save your whole desktop look and switch between looks in one click.

- **What a preset saves:** wallpaper (per monitor, copied so it survives deletion), dark/light mode and accent color, desktop icon positions, and the widget layout.
- **Switching:** apply a preset from the tray menu or from **Settings → Presets**. Click a preset to preview it first.

> Icon layouts restore **positions only**. Files or shortcuts you deleted from the desktop are not recreated.
> For icon layouts to stick, turn off desktop right-click → **View → Auto arrange icons**.

### 🧩 Themes: taskbar, Start menu, Terminal & Discord
Restyle Windows with themes shared online, for example *Gruvbox Material*. One theme package can restyle several things at once:

| Part | What MoniMS changes | You need |
|---|---|---|
| Taskbar, Start menu, notification center | Windhawk mod settings | **[Windhawk](https://windhawk.net)** and the mods the theme uses |
| Windows Terminal | Adds the color schemes and look (font, opacity, padding) to your settings. Your profiles and key bindings are kept. | Windows Terminal |
| Discord | Puts the `.theme.css` into your client mod's theme folder and turns it on | **Vencord**, Vesktop, Equicord or BetterDiscord (plain Discord can't load themes) |

1. In **Settings → Themes**, paste a GitHub link, a `.zip` link, or browse to a local file (`.zip`, `.json`, `.css` or a folder), then press **Import**.
2. Tick the parts you want. You can also pick one of the theme's wallpapers.
3. Press **Apply**. Windows asks for permission once if Windhawk settings change.

- **Missing pieces:** if a part needs something you don't have, MoniMS shows a link to get it (**Install mod**, **Get Terminal**, **Get Vencord**).
- **Fonts:** themes often use a specific font (Gruvbox uses *JetBrainsMono Nerd Font* and *DM Mono*). If it isn't installed, MoniMS keeps your current font and tells you.
- **Discord:** fully quit Discord (also from the tray) and open it again after applying.
- **Undo:** everything MoniMS changes is backed up first. Press **Restore** to undo the last apply, or **Reset** (next to ↻ above the library) to go back to how Windows looked before you applied any theme, wallpaper included.
- **Custom scripts:** themes that contain custom scripts are applied without them, unless you allow the scripts.

> Discord client mods are not allowed by Discord's Terms of Service. MoniMS never installs them; it only uses one you already have.

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
| Terminal colors didn't change in one profile | That profile has its own color scheme. Clear it in Terminal → Settings → that profile → Appearance. |
| Discord theme not showing | Fully quit Discord and reopen it. In Vencord, check **Settings → Themes**. |
| Something else | Check `%APPDATA%\MoniMS\logs\moniMS.log` and [open an issue](../../issues). |

## Building from source

See **[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)** (Korean) for build steps, architecture and the release process.

## License

MoniMS is released under the [MIT License](LICENSE). Bundled third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
MoniMS is not affiliated with Microsoft or Windhawk.
