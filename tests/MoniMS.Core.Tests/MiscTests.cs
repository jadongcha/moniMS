using MoniMS.Core.Presets;
using MoniMS.Core.SystemInfo;

namespace MoniMS.Core.Tests;

public class MiscTests
{
    [Theory]
    [InlineData(22631, "Professional", "Windows 11 Pro")]
    [InlineData(26100, "Core", "Windows 11 Home")]
    [InlineData(19045, "Professional", "Windows 10 Pro")]
    [InlineData(22000, "", "Windows 11")]
    public void OsName_uses_build_number_not_product_name(int build, string edition, string expected) =>
        Assert.Equal(expected, StaticInfoReader.BuildOsName(build, edition));

    [Fact]
    public void GpuEngineKey_groups_processes_on_same_engine()
    {
        var a = GpuUsageProvider.EngineKey("pid_1200_luid_0x00000000_0x0000D1B6_phys_0_eng_0_engtype_3D");
        var b = GpuUsageProvider.EngineKey("pid_8812_luid_0x00000000_0x0000D1B6_phys_0_eng_0_engtype_3D");
        var c = GpuUsageProvider.EngineKey("pid_8812_luid_0x00000000_0x0000D1B6_phys_0_eng_3_engtype_VideoDecode");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(17179869184d, "16.0 GB")]
    public void Format_bytes(double bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));

    [Fact]
    public void WidgetLayout_clone_is_deep_for_sections()
    {
        var a = new WidgetLayout();
        var b = a.Clone();
        b.Sections.Clear();
        Assert.NotEmpty(a.Sections);
    }
}

public class SettingsMigrationTests
{
    [Fact]
    public void Old_settings_get_new_default_widget_size()
    {
        var s = new MoniMS.Core.Settings.AppSettings { Widget = new WidgetLayout { Width = 320, Height = 300 } };
        Assert.True(MoniMS.Core.Settings.SettingsStore.Migrate(s));
        Assert.Equal((550d, 650d), (s.Widget.Width, s.Widget.Height));
        Assert.False(MoniMS.Core.Settings.SettingsStore.Migrate(s)); // 한 번만
    }

    [Fact]
    public void Defaults_are_550_by_650() => Assert.Equal((550d, 650d), (new WidgetLayout().Width, new WidgetLayout().Height));
}
