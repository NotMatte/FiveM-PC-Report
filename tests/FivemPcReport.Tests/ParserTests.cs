using FivemPcReport.Collect;
using FivemPcReport.Util;

namespace FivemPcReport.Tests;

public class ParserTests
{
    [Theory]
    [InlineData("CMK32GX5M2B6000C36", 6000)]
    [InlineData("CMK16GX4M2B3200C16", 3200)]
    [InlineData("F5-6000J3038F16G", 6000)]
    [InlineData("F4-3600C16-16GVKC", 3600)]
    [InlineData("KF560C36-16", 6000)]
    [InlineData("KF432C16BB/16", 3200)]
    [InlineData("CT16G56C46U5.M8A1", 5600)]
    [InlineData("BL16G32C16U4B.M16FE1", 3200)]
    public void RatedSpeed_IsReadFromKnownPartNumbers(string part, int expected) =>
        Assert.Equal(expected, SystemCollector.RatedSpeedFromPartNumber(part));

    [Theory]
    [InlineData("M425R2GA3BB0-CQKOD")]
    [InlineData("")]
    [InlineData("HMCG78AGBUA081N")]
    public void RatedSpeed_IsNullWhenTheCodeDoesNotSayIt(string part) =>
        Assert.Null(SystemCollector.RatedSpeedFromPartNumber(part));

    [Theory]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("31.0.15.3623", "536.23")]
    [InlineData("32.0.15.9144", "591.44")]
    public void NvidiaDriver_ShowsTheVersionNvidiaUses(string win, string expected) =>
        Assert.Equal(expected, SystemCollector.NvidiaDriver(win));

    [Theory]
    [InlineData("Intel(R) UHD Graphics 770", true)]
    [InlineData("AMD Radeon(TM) Graphics", true)]
    [InlineData("NVIDIA GeForce RTX 3070", false)]
    [InlineData("AMD Radeon RX 7800 XT", false)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", false)]
    public void IntegratedGpu_IsRecognised(string name, bool integrated) =>
        Assert.Equal(integrated, SystemCollector.IsIntegrated(name));

    [Fact]
    public void GtaSettings_AreReadBySectionAndName()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Settings>
              <version value="27" />
              <graphics>
                <Tessellation value="3" />
                <MaxLodScale value="0.500000" />
                <Shadow_LongShadows value="true" />
              </graphics>
              <video>
                <ScreenWidth value="3413" />
                <VSync value="1" />
              </video>
            </Settings>
            """;
        var s = FivemCollector.ParseGtaSettings(xml);
        Assert.Equal("3", s["Graphics.Tessellation"]);
        Assert.Equal("0.500000", s["Graphics.MaxLodScale"]);
        Assert.Equal("true", s["Graphics.Shadow_LongShadows"]);
        Assert.Equal("3413", s["Video.ScreenWidth"]);
    }

    [Fact]
    public void Cfg_KeepsSettingsAndDropsIdentities()
    {
        var c = FivemCollector.ParseCfg("""
            seta "vid_budgetScale" "18"
            seta cl_customCrosshair "1"
            seta "ui_streamerMode" "false"
            set "nucleus_token" "abc"
            seta profile_steamName "Mario"
            """);
        Assert.Equal("18", c["vid_budgetScale"]);
        Assert.False(c.ContainsKey("cl_customCrosshair"));
        Assert.False(c.ContainsKey("ui_streamerMode"));
        Assert.False(c.ContainsKey("nucleus_token"));
        Assert.False(c.ContainsKey("profile_steamName"));
    }

    [Fact]
    public void LogIssues_GroupSimilarLinesAndHideIdentities()
    {
        const string log = """
            [    123456] [b3258_GTAProcess]             MainThrd/ Failed to load resource car_12 from 192.168.1.20:30120
            [    123999] [b3258_GTAProcess]             MainThrd/ Failed to load resource car_13 from 192.168.1.20:30120
            [    124000] [b3258_GTAProcess]             MainThrd/ license:1234abcd5678ef90 timed out
            [    124001] [b3258_GTAProcess]             MainThrd/ all good here
            """;
        var issues = FivemCollector.LogIssues(log);
        var failed = Assert.Single(issues, i => i.Text.StartsWith("Failed to load"));
        Assert.Equal(2, failed.Count);
        Assert.Contains("<ip>", failed.Text);
        Assert.DoesNotContain(issues, i => i.Text.Contains("1234abcd"));
        Assert.Equal("3258", FivemCollector.GameBuild(log));
    }

    [Fact]
    public void Privacy_HidesIpsButKeepsVersions()
    {
        Assert.Equal("server <ip>:30120", Privacy.Mask("server 51.75.12.34:30120"));
        Assert.Equal("WebView2 1.0.2903.40", Privacy.Mask("WebView2 1.0.2903.40"));
        Assert.Equal("versione 10.0.26100.1", Privacy.Mask("versione 10.0.26100.1"));
    }
}
