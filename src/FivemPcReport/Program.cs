using System.Diagnostics;
using FivemPcReport.Analysis;
using FivemPcReport.Collect;
using FivemPcReport.Report;
using FivemPcReport.Ui;

namespace FivemPcReport;

internal static class Program
{
    /// <summary>
    /// Without arguments: the app window. With --headless: scan, write the report and exit (for scripts or when
    /// WebView2 is missing). Options: --live [seconds], --out [folder], --open.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
            return Headless(args);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Contains("--dev", StringComparer.OrdinalIgnoreCase)));
        return 0;
    }

    internal static int Headless(string[] args, bool openFolder = false)
    {
        string? Arg(string name)
        {
            var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
        }

        bool live = args.Contains("--live", StringComparer.OrdinalIgnoreCase);
        int seconds = int.TryParse(Arg("--live"), out var s) ? Math.Clamp(s, 5, 120) : 20;
        try
        {
            var data = Collector.Run(new UserNotes(), live && FivemCollector.FivemRunning(), seconds, null, null, CancellationToken.None);
            var analysis = Rules.Evaluate(data);
            var written = ReportWriter.Write(data, analysis, Sections.Build(data), Arg("--out"));
            if (openFolder || args.Contains("--open", StringComparer.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{written.Folder}\"") { UseShellExecute = true });
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "FiveM-PC-Report-error.txt"), e.ToString());
            return 1;
        }
    }
}
