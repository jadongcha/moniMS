using System.Windows;

namespace MoniMS.App.Views;

public partial class InputDialog : Window
{
    private InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Services.UiThemeManager.Attach(this);
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    /// <summary>취소하면 null.</summary>
    public static string? Ask(string title, string prompt, string initial = "")
    {
        var dialog = new InputDialog(title, prompt, initial)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };
        return dialog.ShowDialog() == true ? dialog.Input.Text : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
