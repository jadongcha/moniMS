using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MoniMS.App.Views;

namespace MoniMS.App.Services;

/// <summary>설정 창은 한 번에 하나만 띄운다.</summary>
public sealed class WindowService(IServiceProvider services)
{
    private ManagerWindow? _manager;

    public void ShowManager()
    {
        if (_manager is null)
        {
            _manager = services.GetRequiredService<ManagerWindow>();
            _manager.Closed += (_, _) => _manager = null;
            _manager.Show();
        }
        if (_manager.WindowState == WindowState.Minimized)
            _manager.WindowState = WindowState.Normal;
        _manager.Activate();
    }
}
