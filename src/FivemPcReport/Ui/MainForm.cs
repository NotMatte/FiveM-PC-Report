using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FivemPcReport.Analysis;
using FivemPcReport.Collect;
using FivemPcReport.Report;
using FivemPcReport.Util;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FivemPcReport.Ui;

/// <summary>
/// The window: a WebView2 page (Ui/wwwroot, embedded in the exe) talking to this class with JSON messages.
/// page → host: ready, scan, copyPrompt, openFolder, openReport, openUrl.
/// host → page: hello, fivem, step, tick, done, error.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly bool _dev;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(8, 8, 11) };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 3000 };
    private WrittenReport? _last;
    private bool _scanning;
    private CancellationTokenSource? _cts;

    public MainForm(bool dev)
    {
        _dev = dev;
        Text = "FiveM PC Report";
        BackColor = Color.FromArgb(8, 8, 11);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        Size = new Size(1240, 800);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
        HandleCreated += (_, _) => DarkTitleBar();
        FormClosing += (_, _) => _cts?.Cancel();
        _statusTimer.Tick += (_, _) => { if (!_scanning) Post(new { type = "fivem", running = FivemCollector.FivemRunning() }); };
    }

    private void DarkTitleBar()
    {
        int on = 1;
        _ = Native.DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
        int caption = 0x000B0808; // COLORREF 0x00BBGGRR: #08080b
        _ = Native.DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
    }

    private async Task InitAsync()
    {
        try
        {
            var ui = ExtractUi();
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM-PC-Report", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = _dev;
            core.Settings.AreDefaultContextMenusEnabled = _dev;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping("app.fivem-pc-report", ui, CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnMessage;
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenUrl(e.Uri); };
            _web.Source = new Uri("https://app.fivem-pc-report/index.html");
            _statusTimer.Start();
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException || e.InnerException is WebView2RuntimeNotFoundException)
        {
            var answer = MessageBox.Show(this,
                "Su questo PC manca Microsoft Edge WebView2, che serve per la finestra dell'app.\n\n" +
                "Posso creare comunque il report (senza finestra): lo trovi in Documenti\\FiveM-PC-Report.\n\nProcedo?",
                "FiveM PC Report", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
            {
                Cursor = Cursors.WaitCursor;
                await Task.Run(() => Program.Headless(["--headless", "--live"], openFolder: true));
            }
            Close();
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Errore all'avvio: " + e.Message, "FiveM PC Report", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private static string ExtractUi()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FiveM-PC-Report", "ui-" + AppInfo.Version);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("ui/", StringComparison.Ordinal)))
        {
            var rel = name[3..].Replace('\\', '/');
            var target = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var src = asm.GetManifestResourceStream(name)!;
            using var dst = File.Create(target);
            src.CopyTo(dst);
        }
        return dir;
    }

    private void Post(object payload)
    {
        // CoreWebView2 may only be touched on the UI thread, even to check it for null: scans post from workers.
        if (IsDisposed) return;
        var json = JsonSerializer.Serialize(payload, ReportWriter.Json);
        if (InvokeRequired)
        {
            try { BeginInvoke(() => { if (!IsDisposed) _web.CoreWebView2?.PostWebMessageAsJson(json); }); }
            catch (InvalidOperationException) { /* window closing */ }
        }
        else _web.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? msg;
        try { msg = JsonNode.Parse(e.WebMessageAsJson); } catch { return; }
        switch (msg?["type"]?.GetValue<string>())
        {
            case "ready":
                Post(new
                {
                    type = "hello",
                    version = AppInfo.Version,
                    running = FivemCollector.FivemRunning(),
                    output = Privacy.Mask(ReportWriter.DefaultRoot),
                    steps = Collector.Steps,
                });
                break;
            case "scan":
                if (_scanning) return;
                var notes = new UserNotes
                {
                    Name = (msg["name"]?.GetValue<string>() ?? "").Trim(),
                    Problem = (msg["problem"]?.GetValue<string>() ?? "").Trim(),
                    Fps = (msg["fps"]?.GetValue<string>() ?? "").Trim(),
                };
                var live = msg["live"]?.GetValue<bool>() ?? false;
                var seconds = msg["seconds"]?.GetValue<int>() ?? 20;
                _ = Scan(notes, live, Math.Clamp(seconds, 5, 120));
                break;
            case "copyPrompt":
                if (_last is not null)
                {
                    try { Clipboard.SetText(_last.PromptText); Post(new { type = "copied" }); }
                    catch (Exception ex) { Post(new { type = "error", message = "Non riesco a copiare: " + ex.Message }); }
                }
                break;
            case "openFolder":
                if (_last is not null) Shell("explorer.exe", $"\"{_last.Folder}\"");
                else Shell("explorer.exe", $"\"{Directory.CreateDirectory(ReportWriter.DefaultRoot).FullName}\"");
                break;
            case "openReport":
                if (_last is not null) Shell(_last.ReportPath, null);
                break;
            case "openUrl":
                OpenUrl(msg["url"]?.GetValue<string>() ?? "");
                break;
        }
    }

    private async Task Scan(UserNotes notes, bool live, int seconds)
    {
        _scanning = true;
        _cts = new CancellationTokenSource();
        try
        {
            live = live && FivemCollector.FivemRunning();
            var result = await Task.Run(() =>
            {
                var data = Collector.Run(notes, live, seconds,
                    (id, state) => Post(new { type = "step", id, state }),
                    left => Post(new { type = "tick", left }),
                    _cts.Token);
                var analysis = Rules.Evaluate(data);
                var sections = Sections.Build(data);
                var written = ReportWriter.Write(data, analysis, sections);
                return (data, analysis, sections, written);
            });
            _last = result.written;
            var d = result.data;
            Post(new
            {
                type = "done",
                analysis = result.analysis,
                sections = result.sections,
                live = d.Live is not null,
                summary = new
                {
                    cpu = d.Cpu.Name,
                    gpu = string.Join(" + ", d.Gpus.Where(g => !g.IsIntegrated || d.Gpus.Count == 1).Select(g => g.Name)),
                    vram = Format.Bytes(d.Gpus.Where(g => !g.IsIntegrated).Select(g => g.VramBytes).Max()),
                    ram = Format.Gb(d.Memory.TotalBytes) + (d.Memory.Modules.Count > 0
                        ? $" · {d.Memory.Modules.First().Type} {d.Memory.Modules.Min(m => m.ConfiguredMts)} MT/s" : ""),
                    os = $"{d.Windows.Caption}",
                    display = d.Displays.FirstOrDefault(x => x.Primary) is { } p ? $"{p.Width}x{p.Height} · {p.RefreshHz} Hz" : "",
                },
                folder = Privacy.Mask(result.written.Folder),
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Post(new { type = "error", message = Privacy.Mask(ex.Message) });
        }
        finally
        {
            _scanning = false;
        }
    }

    private static void Shell(string file, string? args)
    {
        try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); } catch { }
    }

    private static void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            Shell(u.ToString(), null);
    }
}
