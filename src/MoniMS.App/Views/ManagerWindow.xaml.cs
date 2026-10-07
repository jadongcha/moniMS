using System.Windows;
using System.Windows.Input;
using MoniMS.App.ViewModels;

namespace MoniMS.App.Views;

public partial class ManagerWindow : Window
{
    public ManagerWindow(ManagerViewModel vm)
    {
        InitializeComponent();
        Services.UiThemeManager.Attach(this);
        DataContext = vm;
        Closed += (_, _) => vm.Detach();
    }

    private void OnPresetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ManagerViewModel vm && vm.ApplyCommand.CanExecute(null))
            vm.ApplyCommand.Execute(null);
    }
}
