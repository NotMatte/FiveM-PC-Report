namespace FivemPcReport.Collect;

// Everything the scan collects. Plain mutable classes: filled step by step by the collectors,
// serialized to data.json and read by the rules and the report writers.

public sealed class ReportData
{
    public string ToolVersion { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public UserNotes Notes { get; set; } = new();
    public MachineInfo Machine { get; set; } = new();
    public CpuInfo Cpu { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = [];
    public List<DisplayInfo> Displays { get; set; } = [];
    public List<DiskInfo> Disks { get; set; } = [];
    public WindowsInfo Windows { get; set; } = new();
    public ProcessesInfo Processes { get; set; } = new();
    public GtaInfo Gta { get; set; } = new();
    public FivemInfo Fivem { get; set; } = new();
    public LiveInfo? Live { get; set; }
    public List<string> CollectorErrors { get; set; } = [];
}

public sealed class UserNotes
{
    public string Name { get; set; } = "";
    public string Problem { get; set; } = "";
    public string Fps { get; set; } = "";
}

public sealed class MachineInfo
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public bool IsLaptop { get; set; }
    public bool HasBattery { get; set; }
    public bool? OnAcPower { get; set; }
    public int? BatteryPercent { get; set; }
    public string Motherboard { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public DateTime? BiosDate { get; set; }
}

public sealed class CpuInfo
{
    public string Name { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public int MaxClockMhz { get; set; }
    public int L3CacheKb { get; set; }
    public uint? Microcode { get; set; }
}

public sealed class MemoryInfo
{
    public long TotalBytes { get; set; }
    public long AvailableBytes { get; set; }
    public List<RamModule> Modules { get; set; } = [];
    public bool PagefileAutomatic { get; set; }
    public long PagefileBytes { get; set; }
    public bool PagefilePresent { get; set; } = true;
}

public sealed class RamModule
{
    public string Slot { get; set; } = "";
    public long CapacityBytes { get; set; }
    public int SpeedMts { get; set; }
    public int ConfiguredMts { get; set; }
    public string Manufacturer { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Type { get; set; } = "";
    public int? RatedMtsFromPartNumber { get; set; }
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public long? VramBytes { get; set; }
    public string DriverVersion { get; set; } = "";
    public string DriverFriendly { get; set; } = "";
    public DateTime? DriverDate { get; set; }
    public bool IsIntegrated { get; set; }
}

public sealed class DisplayInfo
{
    public string Device { get; set; } = "";
    public string Monitor { get; set; } = "";
    public bool Primary { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    public int MaxRefreshHzAtThisResolution { get; set; }
    public int NativeWidth { get; set; }
    public int NativeHeight { get; set; }
}

public sealed class DiskInfo
{
    public string Letter { get; set; } = "";
    public string Label { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public string MediaType { get; set; } = "";
    public string BusType { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class WindowsInfo
{
    public string Caption { get; set; } = "";
    public string Version { get; set; } = "";
    public int Build { get; set; }
    public int Ubr { get; set; }
    public DateTime? LastBoot { get; set; }
    public string PowerPlan { get; set; } = "";
    public string PowerPlanGuid { get; set; } = "";
    public string PowerModeOverlay { get; set; } = "";
    public bool? GameMode { get; set; }
    public bool? GameDvrBackgroundRecording { get; set; }
    public bool? GameDvrEnabled { get; set; }
    public bool? HardwareGpuScheduling { get; set; }
    public bool? VbsRunning { get; set; }
    public bool? MemoryIntegrity { get; set; }
    public List<string> Antivirus { get; set; } = [];
    public List<string> StartupApps { get; set; } = [];
    public Dictionary<string, string> GpuPreferences { get; set; } = [];
}

public sealed class ProcessesInfo
{
    public int Count { get; set; }
    public List<ProcessUsage> TopCpu { get; set; } = [];
    public List<ProcessUsage> TopMemory { get; set; } = [];
    public List<ProcessUsage> Notable { get; set; } = [];
}

public sealed class ProcessUsage
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public int Instances { get; set; }
    public double CpuPercentOfOneCore { get; set; }
    public long MemoryBytes { get; set; }
}

public sealed class GtaInfo
{
    public string Path { get; set; } = "";
    public bool Found { get; set; }
    public string Edition { get; set; } = "";
    public string ExeVersion { get; set; } = "";
    public string Store { get; set; } = "";
    public string DriveLetter { get; set; } = "";
    public List<string> RootMods { get; set; } = [];
    public long ModsFolderBytes { get; set; }
    public int ModsFolderFiles { get; set; }
}

public sealed class FivemInfo
{
    public bool Installed { get; set; }
    public string AppPath { get; set; } = "";
    public string DriveLetter { get; set; } = "";
    public bool IsLinked { get; set; }
    public Dictionary<string, string> Ini { get; set; } = [];
    public string GameBuild { get; set; } = "";
    public Dictionary<string, string> GraphicsSettings { get; set; } = [];
    public bool GraphicsSettingsFound { get; set; }
    public Dictionary<string, string> Cfg { get; set; } = [];
    public List<FileEntry> Mods { get; set; } = [];
    public List<FileEntry> Plugins { get; set; } = [];
    public long CacheBytes { get; set; }
    public long ServerCacheBytes { get; set; }
    public long ServerCachePrivBytes { get; set; }
    public int CrashesLast30Days { get; set; }
    public List<string> RecentCrashes { get; set; } = [];
    public string LatestLogName { get; set; } = "";
    public DateTime? LatestLogDate { get; set; }
    public List<LogIssue> LogIssues { get; set; } = [];
}

public sealed class FileEntry
{
    public string Name { get; set; } = "";
    public long Bytes { get; set; }
    public string Detail { get; set; } = "";
    public bool Disabled { get; set; }
}

public sealed class LogIssue
{
    public string Text { get; set; } = "";
    public int Count { get; set; }
}

public sealed class LiveInfo
{
    public int Seconds { get; set; }
    public string GameProcess { get; set; } = "";
    public double GameCpuPercentOfOneCore { get; set; }
    public double SystemCpuPercent { get; set; }
    public double? CpuPerformancePercent { get; set; }
    public List<double> TopThreadsPercentOfOneCore { get; set; } = [];
    public bool ThreadsReadable { get; set; }
    public double? GpuBusyPercent { get; set; }
    public double? GameGpuPercent { get; set; }
    public long? GameVramBytes { get; set; }
    public long? GpuVramUsedBytes { get; set; }
    public long GameWorkingSetBytes { get; set; }
    public long AvailableRamBytes { get; set; }
    public int BrowserProcesses { get; set; }
    public double BrowserCpuPercentOfOneCore { get; set; }
    public string Bottleneck { get; set; } = "";
}
