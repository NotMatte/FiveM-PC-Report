using FivemPcReport.Analysis;
using FivemPcReport.Collect;

namespace FivemPcReport.Tests;

public class RulesTests
{
    private static ReportData Pc()
    {
        var r = new ReportData();
        r.Cpu = new CpuInfo { Name = "AMD Ryzen 7 5800X 8-Core Processor", Cores = 8, Threads = 16 };
        r.Memory = new MemoryInfo
        {
            TotalBytes = 32L << 30,
            Modules =
            [
                new RamModule { PartNumber = "CMK32GX4M2D3600C18", ConfiguredMts = 3600, SpeedMts = 2133, Type = "DDR4", RatedMtsFromPartNumber = 3600 },
                new RamModule { PartNumber = "CMK32GX4M2D3600C18", ConfiguredMts = 3600, SpeedMts = 2133, Type = "DDR4", RatedMtsFromPartNumber = 3600 },
            ],
        };
        r.Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 3070", Vendor = "NVIDIA", VramBytes = 8L << 30, DriverDate = DateTime.Now.AddMonths(-1) }];
        r.Displays = [new DisplayInfo { Primary = true, Width = 2560, Height = 1440, NativeWidth = 2560, NativeHeight = 1440, RefreshHz = 165, MaxRefreshHzAtThisResolution = 165 }];
        r.Windows = new WindowsInfo { Build = 26100, PowerPlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", PowerPlan = "Prestazioni elevate" };
        return r;
    }

    [Fact]
    public void HealthyPc_HasNoCriticalFindings()
    {
        var a = Rules.Evaluate(Pc());
        Assert.DoesNotContain(a.Findings, f => f.Severity == Severity.Critical);
        Assert.Contains(a.Findings, f => f.Title == "RAM alla sua velocità");
        Assert.Equal("static", a.Verdict.Kind);
    }

    [Fact]
    public void XmpOff_IsFound_AndCriticalWhenTheGameIsCpuBound()
    {
        var r = Pc();
        foreach (var m in r.Memory.Modules) m.ConfiguredMts = 2133;
        Assert.Contains(Rules.Evaluate(r).Findings, f => f.Title.StartsWith("XMP/EXPO spento") && f.Severity == Severity.Warning);

        r.Live = new LiveInfo { Bottleneck = "cpu-thread", ThreadsReadable = true, TopThreadsPercentOfOneCore = [97], GpuBusyPercent = 55 };
        var a = Rules.Evaluate(r);
        Assert.Contains(a.Findings, f => f.Title.StartsWith("XMP/EXPO spento") && f.Severity == Severity.Critical);
        Assert.Equal("cpu", a.Verdict.Kind);
    }

    [Fact]
    public void LaptopOnBattery_AndIntegratedGpuPreference_AreFound()
    {
        var r = Pc();
        r.Machine = new MachineInfo { IsLaptop = true, HasBattery = true, OnAcPower = false };
        r.Gpus.Add(new GpuInfo { Name = "Intel(R) UHD Graphics", IsIntegrated = true });
        var a = Rules.Evaluate(r);
        Assert.Contains(a.Findings, f => f.Title == "Il portatile va a batteria" && f.Severity == Severity.Critical);
        Assert.Contains(a.Findings, f => f.Title.StartsWith("Portatile con due schede video"));
    }

    [Fact]
    public void FullVram_DuringTheLiveScan_IsCritical()
    {
        var r = Pc();
        r.Live = new LiveInfo { Bottleneck = "gpu", GpuBusyPercent = 98, GpuVramUsedBytes = (long)(7.9 * (1L << 30)) };
        var a = Rules.Evaluate(r);
        Assert.Contains(a.Findings, f => f.Title == "Memoria video piena" && f.Severity == Severity.Critical);
        Assert.Equal("gpu", a.Verdict.Kind);
    }

    [Fact]
    public void ReShadeAndHeavyModPacks_AreFound()
    {
        var r = Pc();
        r.Fivem.Installed = true;
        r.Fivem.Plugins =
        [
            new FileEntry { Name = "dxgi.dll", Detail = "ReShade 6.3.0" },
            new FileEntry { Name = "NVE.asi.off", Disabled = true, Detail = "NaturalVision Evolved" },
        ];
        r.Fivem.Mods = [new FileEntry { Name = "p_NVE_Vegetation.rpf", Bytes = 3L << 30, Detail = "NaturalVision Evolved" }];
        var a = Rules.Evaluate(r);
        Assert.Contains(a.Findings, f => f.Title == "ReShade attivo");
        Assert.Contains(a.Findings, f => f.Title.StartsWith("Mod grafiche") && f.Detail.Contains("NaturalVision"));
        Assert.DoesNotContain(a.Findings, f => f.Title == "Plugin di NaturalVision attivo");
    }

    [Fact]
    public void Supersampling_AndLowRefresh_AreFound()
    {
        var r = Pc();
        r.Displays[0].RefreshHz = 60;
        r.Fivem.GraphicsSettingsFound = true;
        r.Fivem.GraphicsSettings = new()
        {
            ["Video.ScreenWidth"] = "3413",
            ["Video.ScreenHeight"] = "1920",
            ["Graphics.MaxLodScale"] = "0.5",
        };
        var a = Rules.Evaluate(r);
        Assert.Contains(a.Findings, f => f.Title.StartsWith("Monitor impostato a 60 Hz"));
        Assert.Contains(a.Findings, f => f.Title.StartsWith("Il gioco disegna a 3413x1920"));
        Assert.Contains(a.Findings, f => f.Title.Contains("distanza estesa"));
    }

    [Fact]
    public void Intel14thGen_WithOldMicrocode_AsksForBios()
    {
        var r = Pc();
        r.Cpu.Name = "Intel(R) Core(TM) i9-14900KF";
        r.Cpu.Microcode = 0x123;
        Assert.Contains(Rules.Evaluate(r).Findings, f => f.Severity == Severity.Critical && f.Title.StartsWith("BIOS da aggiornare"));
        r.Cpu.Microcode = 0x12F;
        Assert.DoesNotContain(Rules.Evaluate(r).Findings, f => f.Title.StartsWith("BIOS da aggiornare"));
    }

    [Theory]
    [InlineData(98, 99.0, "gpu")]
    [InlineData(55, 97.0, "cpu-thread")]
    [InlineData(40, 50.0, "cap")]
    [InlineData(80, 75.0, "mixed")]
    public void Bottleneck_Verdict(double gpu, double topThread, string expected)
    {
        var l = new LiveInfo { GpuBusyPercent = gpu, ThreadsReadable = true, TopThreadsPercentOfOneCore = [topThread], SystemCpuPercent = 30 };
        Assert.Equal(expected, LiveSampler.Verdict(l, 16));
    }
}
