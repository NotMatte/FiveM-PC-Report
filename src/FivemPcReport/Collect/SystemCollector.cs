using System.Diagnostics;
using System.Text.RegularExpressions;
using FivemPcReport.Util;
using Microsoft.Win32;

namespace FivemPcReport.Collect;

/// <summary>Hardware and Windows: read-only, no admin rights needed. Every part fails on its own.</summary>
internal static partial class SystemCollector
{
    public static void Machine(ReportData r)
    {
        var m = r.Machine;
        foreach (var cs in Wmi.Query("SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem"))
        {
            m.Manufacturer = cs.Str("Manufacturer");
            m.Model = cs.Str("Model");
            if (cs.Long("PCSystemType") == 2) m.IsLaptop = true;
        }
        foreach (var en in Wmi.Query("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
        {
            if (en.TryGetValue("ChassisTypes", out var v) && v is ushort[] types &&
                types.Any(t => t is 8 or 9 or 10 or 14 or 30 or 31 or 32))
                m.IsLaptop = true;
        }
        foreach (var bb in Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            m.Motherboard = $"{bb.Str("Manufacturer")} {bb.Str("Product")}".Trim();
        foreach (var bios in Wmi.Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
        {
            m.BiosVersion = bios.Str("SMBIOSBIOSVersion");
            m.BiosDate = bios.Date("ReleaseDate");
        }
        var ps = SystemInformation.PowerStatus;
        m.HasBattery = ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;
        if (m.HasBattery)
        {
            m.OnAcPower = ps.PowerLineStatus == PowerLineStatus.Online;
            m.BatteryPercent = (int)Math.Round(ps.BatteryLifePercent * 100);
        }
    }

    public static void Cpu(ReportData r)
    {
        foreach (var p in Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L3CacheSize FROM Win32_Processor"))
        {
            r.Cpu.Name = Regex.Replace(p.Str("Name"), @"\s+", " ");
            r.Cpu.Cores += (int)p.Long("NumberOfCores");
            r.Cpu.Threads += (int)p.Long("NumberOfLogicalProcessors");
            r.Cpu.MaxClockMhz = (int)p.Long("MaxClockSpeed");
            r.Cpu.L3CacheKb = (int)p.Long("L3CacheSize");
        }
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        // 4 bytes on recent Windows (revision little-endian), 8 bytes on older ones (revision in the high DWORD).
        if (key?.GetValue("Update Revision") is byte[] rev && rev.Length >= 4)
        {
            var hi = rev.Length >= 8 ? BitConverter.ToUInt32(rev, 4) : 0;
            var lo = BitConverter.ToUInt32(rev, 0);
            r.Cpu.Microcode = hi != 0 ? hi : lo != 0 ? lo : null;
        }
    }

    public static void Memory(ReportData r)
    {
        var (total, available) = Native.Memory();
        r.Memory.TotalBytes = total;
        r.Memory.AvailableBytes = available;
        foreach (var mm in Wmi.Query("SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            var part = mm.Str("PartNumber");
            r.Memory.Modules.Add(new RamModule
            {
                Slot = mm.Str("DeviceLocator"),
                CapacityBytes = mm.Long("Capacity"),
                SpeedMts = (int)mm.Long("Speed"),
                ConfiguredMts = (int)mm.Long("ConfiguredClockSpeed"),
                Manufacturer = mm.Str("Manufacturer"),
                PartNumber = part,
                Type = mm.Long("SMBIOSMemoryType") switch
                {
                    24 => "DDR3", 26 => "DDR4", 34 => "DDR5", 30 => "LPDDR4", 35 => "LPDDR5", _ => "",
                },
                RatedMtsFromPartNumber = RatedSpeedFromPartNumber(part),
            });
        }
        try
        {
            var auto = Wmi.Query("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem");
            r.Memory.PagefileAutomatic = auto.Count > 0 && auto[0].TryGetValue("AutomaticManagedPagefile", out var a) && a is true;
            var files = Wmi.Query("SELECT AllocatedBaseSize FROM Win32_PageFileUsage");
            r.Memory.PagefilePresent = r.Memory.PagefileAutomatic || files.Count > 0;
            r.Memory.PagefileBytes = files.Sum(f => f.Long("AllocatedBaseSize")) * 1024 * 1024;
        }
        catch (Exception e) { r.CollectorErrors.Add("File di paging: " + e.Message); }
    }

    private static readonly int[] KnownSpeeds =
        [2133, 2400, 2666, 2933, 3000, 3200, 3466, 3600, 3733, 3866, 4000, 4133, 4266, 4400, 4600, 4800,
         5200, 5600, 6000, 6200, 6400, 6600, 6800, 7000, 7200, 7600, 8000, 8200, 8400];

    /// <summary>Rated (XMP/EXPO) speed read from the kit's part number, when the vendor writes it there.</summary>
    internal static int? RatedSpeedFromPartNumber(string part)
    {
        if (string.IsNullOrWhiteSpace(part)) return null;
        var p = part.Trim().ToUpperInvariant();
        Match m;
        if ((m = Regex.Match(p, @"^F[345]-(\d{4})")).Success) return Known(int.Parse(m.Groups[1].Value));      // G.Skill
        if ((m = Regex.Match(p, @"^KF[345](\d{2})C")).Success) return Known(int.Parse(m.Groups[1].Value) * 100); // Kingston Fury
        if ((m = Regex.Match(p, @"^(?:CT|BL)\d+G(\d{2})C")).Success) return Known(int.Parse(m.Groups[1].Value) * 100); // Crucial
        if ((m = Regex.Match(p, @"(?<!\d)(\d{4})[A-Z]\d{2}")).Success) return Known(int.Parse(m.Groups[1].Value)); // Corsair (C36, Z30), TeamGroup, Patriot
        return null;

        static int? Known(int v) => KnownSpeeds.Contains(v) ? v : null;
    }

    public static void Gpus(ReportData r)
    {
        var vram = VramFromRegistry();
        foreach (var g in Wmi.Query("SELECT Name, DriverVersion, DriverDate, AdapterRAM, PNPDeviceID FROM Win32_VideoController"))
        {
            var name = g.Str("Name");
            if (name.Length == 0 || name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || name.Contains("Parsec", StringComparison.OrdinalIgnoreCase))
                continue;
            var info = new GpuInfo
            {
                Name = name,
                Vendor = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                    : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                    : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "",
                DriverVersion = g.Str("DriverVersion"),
                DriverDate = g.Date("DriverDate"),
                VramBytes = vram.TryGetValue(name, out var v) ? v : g.Long("AdapterRAM") is var a and > 0 ? a : null,
            };
            info.IsIntegrated = IsIntegrated(name);
            info.DriverFriendly = info.Vendor == "NVIDIA" ? NvidiaDriver(info.DriverVersion) : info.DriverVersion;
            r.Gpus.Add(info);
        }
    }

    internal static bool IsIntegrated(string name) =>
        Regex.IsMatch(name, @"Intel\(R\) (UHD|HD|Iris)|Intel.*Graphics$|AMD Radeon\(TM\) Graphics|Radeon\(TM\) \d+M|Radeon Graphics|Vega \d+ Graphics", RegexOptions.IgnoreCase)
        && !Regex.IsMatch(name, @"\bArc\b|\bRX\b", RegexOptions.IgnoreCase);

    /// <summary>"32.0.15.6094" → "560.94" (the version NVIDIA shows to users).</summary>
    internal static string NvidiaDriver(string winVersion)
    {
        var parts = winVersion.Split('.');
        if (parts.Length != 4) return winVersion;
        var digits = parts[2] + parts[3];
        if (digits.Length < 5) return winVersion;
        var last5 = digits[^5..];
        return $"{int.Parse(last5[..3])}.{last5[3..]}";
    }

    private static Dictionary<string, long> VramFromRegistry()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return result;
        foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            try
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is not string desc) continue;
                long size = k.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => k.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int i => (uint)i,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0,
                    },
                };
                if (size > 0) result[desc] = size;
            }
            catch { /* key without access: skip */ }
        }
        return result;
    }

    /// <summary>
    /// The panel's real resolution. NVIDIA DSR/DLDSR and AMD VSR add bigger "virtual" modes, so the biggest mode is not
    /// the native one: the desktop resolution wins when the monitor's EDID prefers it; otherwise the biggest mode that is
    /// not a supersampling factor of the desktop.
    /// </summary>
    internal static (int W, int H) NativeResolution(int w, int h, List<(int W, int H)> modes, HashSet<(int, int)> edidPreferred)
    {
        if (edidPreferred.Contains((w, h)) || edidPreferred.Contains((h, w))) return (w, h);
        double desk = (double)w * h;
        double[] dsr = [1.2, 1.5, 1.78, 2.0, 2.25, 3.0, 4.0];
        var best = (w, h);
        foreach (var m in modes)
        {
            double area = (double)m.W * m.H;
            if (area <= (double)best.w * best.h) continue;
            var factor = area / desk;
            if (dsr.Any(d => Math.Abs(factor - d) < 0.03)) continue;
            best = (m.W, m.H);
        }
        return best;
    }

    private static HashSet<(int, int)> EdidPreferredModes()
    {
        var set = new HashSet<(int, int)>();
        try
        {
            foreach (var row in Wmi.Query("SELECT MonitorSourceModes, PreferredMonitorSourceModeIndex FROM WmiMonitorListedSupportedSourceModes", @"root\wmi"))
            {
                if (row["MonitorSourceModes"] is not System.Management.ManagementBaseObject[] modes) continue;
                var idx = (int)row.Long("PreferredMonitorSourceModeIndex");
                if (idx < 0 || idx >= modes.Length) continue;
                var mode = modes[idx];
                set.Add((Convert.ToInt32(mode["HorizontalActivePixels"]), Convert.ToInt32(mode["VerticalActivePixels"])));
            }
        }
        catch { /* not available: the heuristic alone decides */ }
        return set;
    }

    public static void Displays(ReportData r)
    {
        var preferred = EdidPreferredModes();
        for (int i = 0; ; i++)
        {
            var dd = new Native.DisplayDevice { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DisplayDevice>() };
            if (!Native.EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & Native.DisplayDeviceAttachedToDesktop) == 0) continue;

            var cur = NewDevMode();
            if (!Native.EnumDisplaySettings(dd.DeviceName, Native.EnumCurrentSettings, ref cur)) continue;
            var info = new DisplayInfo
            {
                Device = dd.DeviceString,
                Primary = (dd.StateFlags & Native.DisplayDevicePrimaryDevice) != 0,
                Width = cur.dmPelsWidth,
                Height = cur.dmPelsHeight,
                RefreshHz = cur.dmDisplayFrequency,
            };
            var mon = new Native.DisplayDevice { cb = dd.cb };
            if (Native.EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0)) info.Monitor = mon.DeviceString;

            var modes = new List<(int W, int H)>();
            for (int j = 0; j < 4000; j++)
            {
                var mode = NewDevMode();
                if (!Native.EnumDisplaySettings(dd.DeviceName, j, ref mode)) break;
                if (mode.dmPelsWidth == info.Width && mode.dmPelsHeight == info.Height)
                    info.MaxRefreshHzAtThisResolution = Math.Max(info.MaxRefreshHzAtThisResolution, mode.dmDisplayFrequency);
                modes.Add((mode.dmPelsWidth, mode.dmPelsHeight));
            }
            (info.NativeWidth, info.NativeHeight) = NativeResolution(info.Width, info.Height, modes, preferred);
            r.Displays.Add(info);
        }
    }

    private static Native.DevMode NewDevMode() =>
        new() { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DevMode>() };

    public static void Disks(ReportData r)
    {
        var media = new Dictionary<string, (string Media, string Bus, string Model)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            const string scope = @"root\Microsoft\Windows\Storage";
            var phys = Wmi.Query("SELECT DeviceId, MediaType, BusType, FriendlyName FROM MSFT_PhysicalDisk", scope)
                .ToDictionary(p => p.Str("DeviceId"), p => p);
            foreach (var part in Wmi.Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", scope))
            {
                if (!part.TryGetValue("DriveLetter", out var dl) || dl is null) continue;
                var letter = Convert.ToChar(dl).ToString();
                if (letter == "\0" || !phys.TryGetValue(part.Str("DiskNumber"), out var pd)) continue;
                media[letter] = (
                    pd.Long("MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "sconosciuto" },
                    pd.Long("BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", _ => "" },
                    pd.Str("FriendlyName"));
            }
        }
        catch (Exception e) { r.CollectorErrors.Add("Tipo dei dischi: " + e.Message); }

        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                var letter = d.Name[..1];
                var info = new DiskInfo
                {
                    Letter = letter,
                    Label = d.VolumeLabel,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.AvailableFreeSpace,
                };
                if (media.TryGetValue(letter, out var md))
                {
                    info.MediaType = md.Bus == "NVMe" && md.Media == "sconosciuto" ? "SSD" : md.Media;
                    info.BusType = md.Bus;
                    info.Model = md.Model;
                }
                r.Disks.Add(info);
            }
            catch { /* unreadable drive */ }
        }
    }

    public static void Windows(ReportData r)
    {
        var w = r.Windows;
        foreach (var os in Wmi.Query("SELECT Caption, Version, BuildNumber, LastBootUpTime FROM Win32_OperatingSystem"))
        {
            w.Caption = os.Str("Caption").Replace("Microsoft ", "");
            w.Version = os.Str("Version");
            w.Build = int.TryParse(os.Str("BuildNumber"), out var b) ? b : 0;
            w.LastBoot = os.Date("LastBootUpTime");
        }
        using (var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            if (nt?.GetValue("UBR") is int ubr) w.Ubr = ubr;
            if (nt?.GetValue("DisplayVersion") is string dv) w.Version = $"{dv} ({w.Version})";
        }

        PowerPlan(w);

        using (var gb = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"))
            w.GameMode = gb?.GetValue("AutoGameModeEnabled") is int gm ? gm != 0 : true;
        using (var gcs = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore"))
            if (gcs?.GetValue("GameDVR_Enabled") is int ge) w.GameDvrEnabled = ge != 0;
        using (var dvr = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR"))
        {
            if (dvr?.GetValue("AppCaptureEnabled") is int ac && ac == 0) w.GameDvrEnabled = false;
            w.GameDvrBackgroundRecording = dvr?.GetValue("HistoricalCaptureEnabled") is int hc ? hc != 0 : false;
        }
        using (var gd = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
            if (gd?.GetValue("HwSchMode") is int hs) w.HardwareGpuScheduling = hs == 2;

        try
        {
            foreach (var dg in Wmi.Query("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard"))
            {
                w.VbsRunning = dg.Long("VirtualizationBasedSecurityStatus") == 2;
                w.MemoryIntegrity = dg.TryGetValue("SecurityServicesRunning", out var s) && s is uint[] services && services.Contains(2u);
            }
        }
        catch
        {
            using var hvci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            if (hvci?.GetValue("Enabled") is int en) w.MemoryIntegrity = en == 1;
        }

        try
        {
            foreach (var av in Wmi.Query("SELECT displayName FROM AntiVirusProduct", @"root\SecurityCenter2"))
                if (av.Str("displayName") is { Length: > 0 } n && !w.Antivirus.Contains(n)) w.Antivirus.Add(n);
        }
        catch { /* not available (Windows Server, policies) */ }

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var run = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (run is null) continue;
            foreach (var name in run.GetValueNames())
                if (name.Length > 0 && !w.StartupApps.Contains(name)) w.StartupApps.Add(name);
        }

        using (var pref = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
        {
            if (pref is not null)
                foreach (var exe in pref.GetValueNames())
                    if (Regex.IsMatch(exe, @"fivem|gta5|citizenfx", RegexOptions.IgnoreCase))
                        w.GpuPreferences[Privacy.Mask(exe)] = pref.GetValue(exe)?.ToString() switch
                        {
                            var v when v?.Contains("GpuPreference=2") == true => "prestazioni elevate (GPU dedicata)",
                            var v when v?.Contains("GpuPreference=1") == true => "risparmio energia (GPU integrata)",
                            var v => "decide Windows" + (string.IsNullOrEmpty(v) ? "" : $" ({v.TrimEnd(';')})"),
                        };
        }
    }

    private static void PowerPlan(WindowsInfo w)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var m = Regex.Match(output, @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\s*\((.+)\)", RegexOptions.IgnoreCase);
            if (m.Success) { w.PowerPlanGuid = m.Groups[1].Value.ToLowerInvariant(); w.PowerPlan = m.Groups[2].Value.Trim(); }
        }
        catch { /* powercfg missing */ }

        using var schemes = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
        if (string.IsNullOrEmpty(w.PowerPlanGuid) && schemes?.GetValue("ActivePowerScheme") is string active)
            w.PowerPlanGuid = active.ToLowerInvariant();
        if (string.IsNullOrEmpty(w.PowerPlan)) w.PowerPlan = PlanName(w.PowerPlanGuid);
        if (schemes?.GetValue("ActiveOverlayAcPowerScheme") is string overlay)
            w.PowerModeOverlay = overlay.ToLowerInvariant() switch
            {
                "ded574b5-45a0-4f42-8737-46345c09c238" => "Prestazioni migliori",
                "961cc777-2547-4f9d-8174-7d86181b8a7a" => "Massima efficienza energetica",
                "3af9b8d9-7c97-431d-ad78-34a8bfea439f" => "Prestazioni migliori (bilanciato)",
                _ => "Bilanciata",
            };
    }

    internal static string PlanName(string guid) => guid switch
    {
        "381b4222-f694-41f0-9685-ff5bb260df2e" => "Bilanciato",
        "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "Prestazioni elevate",
        "a1841308-3bce-4f06-8b0b-7c6ab0ca0f35" => "Risparmio di energia",
        "e9a42b02-d5df-448d-aa00-03f14749eb61" => "Prestazioni eccellenti",
        _ => "personalizzato",
    };

    // Processes that matter for games: overlays, capture, RGB and tuning suites, heavy background apps.
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Discord"] = "Discord (overlay possibile)",
        ["obs64"] = "OBS Studio (registrazione)",
        ["obs32"] = "OBS Studio (registrazione)",
        ["MSIAfterburner"] = "MSI Afterburner",
        ["RTSS"] = "RivaTuner Statistics Server (overlay/limitatore FPS)",
        ["NVIDIA Overlay"] = "Overlay NVIDIA",
        ["NVIDIA Share"] = "Overlay NVIDIA (ShadowPlay)",
        ["NVIDIA app"] = "NVIDIA App",
        ["GameBar"] = "Xbox Game Bar",
        ["GameBarFTServer"] = "Xbox Game Bar",
        ["Medal"] = "Medal (registrazione clip)",
        ["Overwolf"] = "Overwolf (overlay)",
        ["Outplayed"] = "Outplayed (registrazione)",
        ["wallpaper32"] = "Wallpaper Engine",
        ["wallpaper64"] = "Wallpaper Engine",
        ["Lively"] = "Lively Wallpaper",
        ["iCUE"] = "Corsair iCUE (RGB)",
        ["ArmouryCrate"] = "ASUS Armoury Crate",
        ["ArmouryCrateControlInterface"] = "ASUS Armoury Crate",
        ["LGHUB"] = "Logitech G HUB",
        ["lghub_agent"] = "Logitech G HUB",
        ["SignalRgb"] = "SignalRGB",
        ["NZXT CAM"] = "NZXT CAM",
        ["RazerCentral"] = "Razer Synapse",
        ["Razer Synapse 3"] = "Razer Synapse",
        ["Steam"] = "Steam",
        ["EpicGamesLauncher"] = "Epic Games Launcher",
        ["chrome"] = "Google Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Firefox",
        ["opera"] = "Opera",
        ["Spotify"] = "Spotify",
        ["Teams"] = "Microsoft Teams",
        ["ms-teams"] = "Microsoft Teams",
        ["OneDrive"] = "OneDrive (sincronizzazione)",
        ["Dropbox"] = "Dropbox",
        ["MsMpEng"] = "Antivirus di Windows (scansione)",
        ["SearchIndexer"] = "Indicizzazione di Windows",
        ["CCleaner64"] = "CCleaner",
        ["ProcessLasso"] = "Process Lasso",
        ["HWiNFO64"] = "HWiNFO",
        ["ReShade"] = "ReShade",
        ["Lossless Scaling"] = "Lossless Scaling",
        ["LosslessScaling"] = "Lossless Scaling",
    };

    public static void Processes(ReportData r, int sampleMs = 2000)
    {
        var first = Snapshot();
        Thread.Sleep(sampleMs);
        var second = Snapshot();
        int cores = Math.Max(1, Environment.ProcessorCount);
        _ = cores;

        var byName = new Dictionary<string, ProcessUsage>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pid, (name, cpu, mem)) in second)
        {
            if (pid == 0 || name is "Idle" or "System") continue;
            if (!byName.TryGetValue(name, out var u))
                byName[name] = u = new ProcessUsage { Name = name, Label = Known.TryGetValue(name, out var l) ? l : "" };
            u.Instances++;
            u.MemoryBytes += mem;
            if (first.TryGetValue(pid, out var before) && cpu >= before.Cpu)
                u.CpuPercentOfOneCore += (cpu - before.Cpu).TotalMilliseconds / sampleMs * 100;
        }
        r.Processes.Count = second.Count;
        r.Processes.TopCpu = byName.Values.Where(u => u.CpuPercentOfOneCore >= 1)
            .OrderByDescending(u => u.CpuPercentOfOneCore).Take(12).ToList();
        r.Processes.TopMemory = byName.Values.OrderByDescending(u => u.MemoryBytes).Take(12).ToList();
        r.Processes.Notable = byName.Values.Where(u => u.Label.Length > 0).OrderBy(u => u.Label).ToList();
    }

    private static Dictionary<int, (string Name, TimeSpan Cpu, long Mem)> Snapshot()
    {
        var map = new Dictionary<int, (string, TimeSpan, long)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    TimeSpan cpu = TimeSpan.Zero;
                    try { cpu = p.TotalProcessorTime; } catch { /* protected process */ }
                    map[p.Id] = (p.ProcessName, cpu, p.WorkingSet64);
                }
                catch { /* exited */ }
            }
        }
        return map;
    }
}
