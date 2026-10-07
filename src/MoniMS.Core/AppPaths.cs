namespace MoniMS.Core;

public static class AppPaths
{
    /// <summary>%APPDATA%\MoniMS</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoniMS");
}
