namespace FivemPcReport.Collect;

public sealed record ScanStep(string Id, string Label);

/// <summary>Runs every collector in order; a failing step is written to CollectorErrors and the scan goes on.</summary>
internal static class Collector
{
    public static readonly ScanStep[] Steps =
    [
        new("machine", "Computer e scheda madre"),
        new("cpu", "Processore"),
        new("memory", "Memoria RAM"),
        new("gpu", "Scheda video e driver"),
        new("display", "Schermi"),
        new("disks", "Dischi e spazio libero"),
        new("windows", "Impostazioni di Windows"),
        new("fivem", "FiveM: impostazioni, mod, cache e log"),
        new("gta", "GTA V"),
        new("processes", "Programmi aperti"),
        new("live", "Analisi dal vivo del gioco"),
    ];

    public static ReportData Run(UserNotes notes, bool live, int liveSeconds,
        Action<string, string>? onStep, Action<int>? onLiveTick, CancellationToken ct)
    {
        var r = new ReportData
        {
            ToolVersion = AppInfo.Version,
            CreatedAt = DateTimeOffset.Now,
            Notes = notes,
        };

        void Step(string id, Action action)
        {
            ct.ThrowIfCancellationRequested();
            var label = Steps.First(s => s.Id == id).Label;
            onStep?.Invoke(id, "running");
            try { action(); onStep?.Invoke(id, "done"); }
            catch (Exception e)
            {
                r.CollectorErrors.Add($"{label}: {e.GetType().Name}: {Util.Privacy.Mask(e.Message)}");
                onStep?.Invoke(id, "error");
            }
        }

        Step("machine", () => SystemCollector.Machine(r));
        Step("cpu", () => SystemCollector.Cpu(r));
        Step("memory", () => SystemCollector.Memory(r));
        Step("gpu", () => SystemCollector.Gpus(r));
        Step("display", () => SystemCollector.Displays(r));
        Step("disks", () => SystemCollector.Disks(r));
        Step("windows", () => SystemCollector.Windows(r));
        Step("fivem", () => FivemCollector.Fivem(r));
        Step("gta", () => FivemCollector.Gta(r));
        Step("processes", () => SystemCollector.Processes(r));
        if (live)
            Step("live", () => r.Live = LiveSampler.Sample(liveSeconds, onLiveTick, ct));
        else
            onStep?.Invoke("live", "skipped");
        return r;
    }
}

internal static class AppInfo
{
    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}
