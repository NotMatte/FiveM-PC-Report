using System.Diagnostics;
using System.Text.RegularExpressions;
using FivemPcReport.Util;

namespace FivemPcReport.Collect;

/// <summary>
/// While the game runs: how busy the game's threads, the CPU and the GPU are. Reads only Windows performance
/// counters and process times (what Task Manager shows); it never touches the game's memory.
/// </summary>
internal static partial class LiveSampler
{
    public static LiveInfo? Sample(int seconds, Action<int>? tick, CancellationToken ct)
    {
        using var game = FivemCollector.FindGame();
        if (game is null) return null;

        var live = new LiveInfo { Seconds = seconds, GameProcess = game.ProcessName };
        int cores = Math.Max(1, Environment.ProcessorCount);
        int pid = game.Id;

        PerformanceCounter? sysCpu = TryCounter("Processor Information", "% Processor Utility", "_Total")
                                   ?? TryCounter("Processor", "% Processor Time", "_Total");
        PerformanceCounter? perf = TryCounter("Processor Information", "% Processor Performance", "_Total");
        var gpu = GpuCounters.Create(pid);

        TimeSpan gameCpu0 = SafeCpu(game);
        var threads0 = Threads(game, out bool readable);
        live.ThreadsReadable = readable;
        var browsers0 = Browsers();

        sysCpu?.NextValue();
        perf?.NextValue();
        gpu?.Prime();

        var sw = Stopwatch.StartNew();
        double sysSum = 0, perfSum = 0; int samples = 0;
        var gpuBusy = new List<double>(); var gameGpu = new List<double>();
        for (int s = 0; s < seconds && !ct.IsCancellationRequested; s++)
        {
            Thread.Sleep(1000);
            tick?.Invoke(seconds - s - 1);
            if (sysCpu is not null) sysSum += sysCpu.NextValue();
            if (perf is not null) perfSum += perf.NextValue();
            samples++;
            if (gpu is not null)
            {
                var (busy, mine) = gpu.Read();
                gpuBusy.Add(busy);
                gameGpu.Add(mine);
            }
        }
        var elapsed = sw.Elapsed.TotalMilliseconds;

        game.Refresh();
        live.GameCpuPercentOfOneCore = (SafeCpu(game) - gameCpu0).TotalMilliseconds / elapsed * 100;
        live.SystemCpuPercent = samples > 0 && sysCpu is not null ? Math.Min(100, sysSum / samples) : 0;
        live.CpuPerformancePercent = samples > 0 && perf is not null ? perfSum / samples : null;
        try { live.GameWorkingSetBytes = game.WorkingSet64; } catch { }
        live.AvailableRamBytes = Native.Memory().Available;

        if (readable)
        {
            var threads1 = Threads(game, out _);
            live.TopThreadsPercentOfOneCore = threads1
                .Where(t => threads0.ContainsKey(t.Key))
                .Select(t => (t.Value - threads0[t.Key]).TotalMilliseconds / elapsed * 100)
                .OrderByDescending(x => x).Take(6).Select(x => Math.Round(x, 1)).ToList();
        }

        var browsers1 = Browsers();
        live.BrowserProcesses = browsers1.Count;
        live.BrowserCpuPercentOfOneCore = browsers1
            .Where(b => browsers0.ContainsKey(b.Key))
            .Sum(b => (b.Value - browsers0[b.Key]).TotalMilliseconds) / elapsed * 100;

        if (gpuBusy.Count > 0)
        {
            live.GpuBusyPercent = Math.Min(100, gpuBusy.Average());
            live.GameGpuPercent = Math.Min(100, gameGpu.Average());
        }
        if (gpu is not null)
        {
            live.GameVramBytes = gpu.GameDedicatedBytes();
            live.GpuVramUsedBytes = gpu.AdapterDedicatedBytes();
            gpu.Dispose();
        }
        sysCpu?.Dispose();
        perf?.Dispose();

        live.Bottleneck = Verdict(live, cores);
        return live;
    }

    internal static string Verdict(LiveInfo l, int cores)
    {
        var top = l.TopThreadsPercentOfOneCore.FirstOrDefault();
        if (l.GpuBusyPercent is >= 92) return "gpu";
        if (l.ThreadsReadable && top >= 85 && l.GpuBusyPercent is null or < 85) return "cpu-thread";
        if (l.SystemCpuPercent >= 90) return "cpu-all";
        if (l.GpuBusyPercent is < 70 && (!l.ThreadsReadable || top < 70)) return "cap";
        return "mixed";
    }

    private static TimeSpan SafeCpu(Process p)
    {
        try { return p.TotalProcessorTime; } catch { return TimeSpan.Zero; }
    }

    private static Dictionary<int, TimeSpan> Threads(Process p, out bool readable)
    {
        var d = new Dictionary<int, TimeSpan>();
        readable = false;
        try
        {
            p.Refresh();
            foreach (ProcessThread t in p.Threads)
            {
                try { d[t.Id] = t.TotalProcessorTime; readable = true; } catch { /* thread gone or denied */ }
            }
        }
        catch { readable = false; }
        return d;
    }

    private static Dictionary<int, TimeSpan> Browsers()
    {
        var d = new Dictionary<int, TimeSpan>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (!p.ProcessName.Contains("ChromeBrowser", StringComparison.OrdinalIgnoreCase) &&
                    !p.ProcessName.Contains("FiveM_ROSLauncher", StringComparison.OrdinalIgnoreCase)) continue;
                d[p.Id] = SafeCpu(p);
            }
        }
        return d;
    }

    private static PerformanceCounter? TryCounter(string category, string counter, string instance)
    {
        try
        {
            var c = new PerformanceCounter(category, counter, instance, readOnly: true);
            c.NextValue();
            return c;
        }
        catch { return null; }
    }

    /// <summary>"GPU Engine" counters, the ones behind Task Manager's GPU column.</summary>
    private sealed partial class GpuCounters : IDisposable
    {
        private readonly List<(PerformanceCounter C, string Engine, bool Mine)> _engines = [];
        private readonly List<PerformanceCounter> _gameMem = [];
        private readonly List<PerformanceCounter> _adapterMem = [];

        public static GpuCounters? Create(int pid)
        {
            try
            {
                var g = new GpuCounters();
                var cat = new PerformanceCounterCategory("GPU Engine");
                foreach (var inst in cat.GetInstanceNames())
                {
                    if (!inst.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                    var m = InstanceRx().Match(inst);
                    if (!m.Success) continue;
                    var engine = m.Groups["luid"].Value + "/" + m.Groups["eng"].Value;
                    g._engines.Add((new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true), engine,
                        m.Groups["pid"].Value == pid.ToString()));
                }
                try
                {
                    var pm = new PerformanceCounterCategory("GPU Process Memory");
                    foreach (var inst in pm.GetInstanceNames().Where(i => i.StartsWith($"pid_{pid}_", StringComparison.Ordinal)))
                        g._gameMem.Add(new PerformanceCounter("GPU Process Memory", "Dedicated Usage", inst, true));
                    var am = new PerformanceCounterCategory("GPU Adapter Memory");
                    foreach (var inst in am.GetInstanceNames())
                        g._adapterMem.Add(new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst, true));
                }
                catch { /* memory counters are optional */ }
                return g._engines.Count > 0 ? g : null;
            }
            catch { return null; }
        }

        public void Prime()
        {
            foreach (var e in _engines) { try { e.C.NextValue(); } catch { } }
        }

        public (double Busy, double Mine) Read()
        {
            var perEngine = new Dictionary<string, double>();
            double mine = 0;
            foreach (var e in _engines)
            {
                double v;
                try { v = e.C.NextValue(); } catch { continue; }
                perEngine[e.Engine] = perEngine.TryGetValue(e.Engine, out var cur) ? cur + v : v;
                if (e.Mine) mine += v;
            }
            return (perEngine.Count > 0 ? perEngine.Values.Max() : 0, mine);
        }

        public long? GameDedicatedBytes()
        {
            if (_gameMem.Count == 0) return null;
            try { return _gameMem.Sum(c => (long)c.NextValue()); } catch { return null; }
        }

        public long? AdapterDedicatedBytes()
        {
            if (_adapterMem.Count == 0) return null;
            try { return _adapterMem.Max(c => (long)c.NextValue()); } catch { return null; }
        }

        public void Dispose()
        {
            foreach (var e in _engines) e.C.Dispose();
            foreach (var c in _gameMem) c.Dispose();
            foreach (var c in _adapterMem) c.Dispose();
        }

        [GeneratedRegex(@"^pid_(?<pid>\d+)_luid_(?<luid>0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_\d+_eng_(?<eng>\d+)_engtype_", RegexOptions.IgnoreCase)]
        private static partial Regex InstanceRx();
    }
}
