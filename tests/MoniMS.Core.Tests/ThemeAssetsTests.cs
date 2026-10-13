using MoniMS.Core.Shell;

namespace MoniMS.Core.Tests;

public sealed class ThemeAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moniMS-assets-" + Guid.NewGuid().ToString("N"));

    public ThemeAssetsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private const string StartButtonUrl =
        "https://raw.githubusercontent.com/MrDLingters/Win11GruvboxMaterial/refs/heads/main/Windhawk/WinButton.png";

    [Fact]
    public void Remote_start_button_image_is_replaced_with_the_packaged_file()
    {
        var files = Path.Combine(_root, "files");
        Directory.CreateDirectory(Path.Combine(files, "Win11GruvboxMaterial-main", "Windhawk"));
        Directory.CreateDirectory(Path.Combine(files, "Win11GruvboxMaterial-main", "Other"));
        File.WriteAllText(Path.Combine(files, "Win11GruvboxMaterial-main", "Windhawk", "WinButton.png"), "right");
        File.WriteAllText(Path.Combine(files, "Win11GruvboxMaterial-main", "Other", "WinButton.png"), "wrong");
        var assets = Path.Combine(_root, "_assets", "gruvbox");

        var settings = new Dictionary<string, object>
        {
            ["controlStyles[43].styles[0]"] = $"Background:=<ImageBrush Stretch=\"none\" ImageSource=\"{StartButtonUrl}\"/>",
            ["controlStyles[44].styles[0]"] = "Visibility=Collapsed",
            ["controlStyles[45].styles[0]"] = "Background:=<ImageBrush ImageSource='https://example.com/not-in-package.png'/>",
            ["count"] = 3,
        };

        var result = ThemeAssets.LocalizeImages(settings, files, assets, out var localized);

        Assert.Equal(1, localized);
        var copy = Path.Combine(assets, "WinButton.png");
        Assert.Equal("right", File.ReadAllText(copy)); // 주소의 경로(Windhawk/)와 맞는 파일
        Assert.Equal($"Background:=<ImageBrush Stretch=\"none\" ImageSource=\"{copy}\"/>", result["controlStyles[43].styles[0]"]);
        Assert.Equal("Visibility=Collapsed", result["controlStyles[44].styles[0]"]);
        Assert.Equal(3, result["count"]);
        Assert.Equal(["https://example.com/not-in-package.png"], ThemeAssets.FindImageUrls(result));
    }

    [Fact]
    public void Settings_without_images_are_unchanged()
    {
        var settings = new Dictionary<string, object> { ["theme"] = "", ["a"] = "Fill=#32302F" };
        var result = ThemeAssets.LocalizeImages(settings, Path.Combine(_root, "none"), Path.Combine(_root, "a"), out var n);
        Assert.Equal(0, n);
        Assert.Equal(settings, result);
        Assert.False(Directory.Exists(Path.Combine(_root, "a")));
    }
}
