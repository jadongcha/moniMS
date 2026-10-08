using System.Windows;
using Microsoft.Extensions.Logging;
using MoniMS.Core.Presets;
using MoniMS.Core.Settings;
using WinForms = System.Windows.Forms;

namespace MoniMS.App.Services;

/// <summary>알림 영역(트레이) 아이콘과 메뉴. 메뉴는 열릴 때마다 최신 상태로 다시 만든다.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly PresetService _presets;
    private readonly WidgetController _widget;
    private readonly ISettingsStore _settings;
    private readonly WindowService _windows;
    private readonly ILogger<TrayIconService> _logger;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu = new();

    public TrayIconService(PresetService presets, WidgetController widget, ISettingsStore settings,
        WindowService windows, ILogger<TrayIconService> logger)
    {
        _presets = presets;
        _widget = widget;
        _settings = settings;
        _windows = windows;
        _logger = logger;

        _menu.Opening += (_, _) => BuildMenu();
        _icon = new WinForms.NotifyIcon
        {
            Text = "MoniMS",
            Icon = LoadIcon(),
            ContextMenuStrip = _menu,
            Visible = false,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                _windows.ShowManager();
        };
    }

    public void Show()
    {
        BuildMenu();
        _icon.Visible = true;
    }

    public void Notify(string title, string message, bool warning = false) =>
        _icon.ShowBalloonTip(4000, title, message, warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);

    private bool _applying;

    /// <summary>
    /// 프리셋 적용 + 결과 알림. 설정 창에서도 사용. 적용하는 동안 UI는 멈추지 않는다.
    /// 다른 프리셋을 적용하는 중이면 false (연달아 눌러서 두 프리셋이 섞이지 않도록).
    /// </summary>
    public async Task<bool> ApplyPresetAsync(Preset preset)
    {
        if (_applying)
            return false;
        _applying = true;
        try
        {
            var result = await _presets.ApplyAsync(preset);
            _settings.Current.LastAppliedPresetId = preset.Id;
            _settings.Save();
            if (result.Warnings.Count > 0)
                Notify($"Applied '{preset.Name}' with issues", string.Join("\n", result.Warnings), warning: true);
            else
                Notify("Preset applied", $"Applied '{preset.Name}'.");
            return true;
        }
        finally
        {
            _applying = false;
        }
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        var layout = _widget.Layout;

        var header = new WinForms.ToolStripMenuItem("MoniMS") { Enabled = false };
        header.Font = new System.Drawing.Font(header.Font, System.Drawing.FontStyle.Bold);
        _menu.Items.Add(header);
        _menu.Items.Add(new WinForms.ToolStripSeparator());

        // 프리셋 목록
        var presetMenu = new WinForms.ToolStripMenuItem(_applying ? "Applying preset..." : "Apply preset") { Enabled = !_applying };
        var all = _presets.GetAll();
        if (all.Count == 0)
            presetMenu.DropDownItems.Add(new WinForms.ToolStripMenuItem("(no saved presets)") { Enabled = false });
        foreach (var p in all)
        {
            var item = new WinForms.ToolStripMenuItem(p.Name)
            {
                Checked = p.Id == _settings.Current.LastAppliedPresetId,
            };
            var captured = p;
            item.Click += async (_, _) =>
            {
                try
                {
                    await ApplyPresetAsync(captured);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Applying preset from the tray failed");
                    Notify("Error", ex.Message, warning: true);
                }
            };
            presetMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(presetMenu);
        _menu.Items.Add("Save current desktop as preset", null, (_, _) => Run(() =>
        {
            var name = $"Preset {DateTime.Now:MM-dd HH:mm}";
            _presets.Capture(name);
            Notify("Preset saved", $"Saved as '{name}'. You can rename it in Settings.");
        }));
        _menu.Items.Add(new WinForms.ToolStripSeparator());

        // 위젯
        _menu.Items.Add(Toggle("Show widget", layout.Visible, v => _widget.Update(l => l.Visible = v)));
        _menu.Items.Add(Toggle("Edit widget position", _widget.IsEditMode, v =>
        {
            if (v && !layout.Visible)
                _widget.Update(l => l.Visible = true);
            _widget.SetEditMode(v);
        }));
        _menu.Items.Add(new WinForms.ToolStripSeparator());

        _menu.Items.Add("Settings && presets...", null, (_, _) => _windows.ShowManager());
        _menu.Items.Add("Exit", null, (_, _) => Application.Current.Shutdown());
    }

    private WinForms.ToolStripMenuItem Toggle(string text, bool isChecked, Action<bool> onChange)
    {
        var item = new WinForms.ToolStripMenuItem(text) { Checked = isChecked };
        item.Click += (_, _) => Run(() => onChange(!isChecked));
        return item;
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray menu action failed");
            Notify("Error", ex.Message, warning: true);
        }
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/moniMS.ico"));
        return info is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(info.Stream);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
