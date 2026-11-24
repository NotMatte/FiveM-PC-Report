using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FivemPcReport.Util;
using Microsoft.Win32;

namespace FivemPcReport.Collect;

/// <summary>FiveM and GTA V files on this PC: read-only, nothing is changed.</summary>
internal static partial class FivemCollector
{
    public static string LocalFivemRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM");

    public static string CitizenFxRoaming =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX");

    public static void Fivem(ReportData r)
    {
        var f = r.Fivem;
        var root = LocalFivemRoot;
        var app = Path.Combine(root, "FiveM.app");
        if (!Directory.Exists(app)) { f.Installed = false; return; }
        f.Installed = true;

        var real = Resolve(root);
        f.IsLinked = !string.Equals(real, root, StringComparison.OrdinalIgnoreCase);
        var realApp = Path.Combine(real, "FiveM.app");
        f.AppPath = Privacy.Mask(realApp);
        f.DriveLetter = Path.GetPathRoot(realApp)?[..1] ?? "";

        var ini = Path.Combine(app, "CitizenFX.ini");
        if (File.Exists(ini)) f.Ini = ParseIni(File.ReadAllText(ini));

        var settings = Path.Combine(CitizenFxRoaming, "gta5_settings.xml");
        if (File.Exists(settings))
        {
            f.GraphicsSettingsFound = true;
            f.GraphicsSettings = ParseGtaSettings(ReadShared(settings));
        }
        var cfg = Path.Combine(CitizenFxRoaming, "fivem.cfg");
        if (File.Exists(cfg)) f.Cfg = ParseCfg(ReadShared(cfg));

        var mods = Path.Combine(app, "mods");
        if (Directory.Exists(mods))
            foreach (var file in SafeFiles(mods, "*", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(file);
                f.Mods.Add(new FileEntry
                {
                    Name = Privacy.Mask(Path.GetRelativePath(mods, file)),
                    Bytes = fi.Length,
                    Detail = ModPack(fi.Name),
                    Disabled = !fi.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase),
                });
            }

        var plugins = Path.Combine(app, "plugins");
        if (Directory.Exists(plugins))
            foreach (var file in SafeFiles(plugins, "*", SearchOption.TopDirectoryOnly))
            {
                var fi = new FileInfo(file);
                f.Plugins.Add(new FileEntry
                {
                    Name = fi.Name,
                    Bytes = fi.Length,
                    Detail = PluginKind(file),
                    Disabled = Regex.IsMatch(fi.Name, @"\.(off|disabled|bak|old)$", RegexOptions.IgnoreCase),
                });
            }

        var data = Path.Combine(app, "data");
        f.CacheBytes = FolderSize(Path.Combine(data, "cache"));
        f.ServerCacheBytes = FolderSize(Path.Combine(data, "server-cache"));
        f.ServerCachePrivBytes = FolderSize(Path.Combine(data, "server-cache-priv"));

        var crashes = Path.Combine(app, "crashes");
        if (Directory.Exists(crashes))
        {
            var dumps = SafeFiles(crashes, "*.dmp", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p)).OrderByDescending(x => x.LastWriteTime).ToList();
            // One crash writes "<id>.dmp" and often "<id>-full.dmp": count crashes, not files.
            var crashesById = dumps.GroupBy(d => Path.GetFileNameWithoutExtension(d.Name).Replace("-full", "", StringComparison.OrdinalIgnoreCase))
                .Select(g => g.OrderByDescending(x => x.LastWriteTime).First()).OrderByDescending(x => x.LastWriteTime).ToList();
            f.CrashesLast30Days = crashesById.Count(d => d.LastWriteTime > DateTime.Now.AddDays(-30));
            f.RecentCrashes = crashesById.Take(5).Select(d => $"{d.LastWriteTime:dd/MM/yyyy HH:mm}").ToList();
        }

        var logs = Path.Combine(app, "logs");
        if (Directory.Exists(logs))
        {
            var latest = SafeFiles(logs, "CitizenFX_log_*.log", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p)).OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
            if (latest is not null)
            {
                f.LatestLogName = latest.Name;
                f.LatestLogDate = latest.LastWriteTime;
                var text = ReadShared(latest.FullName, 40 * 1024 * 1024);
                f.GameBuild = GameBuild(text);
                f.LogIssues = LogIssues(text);
            }
        }
        if (string.IsNullOrEmpty(f.GameBuild) && f.Ini.TryGetValue("SavedBuildNumber", out var sb)) f.GameBuild = sb;
    }

    public static void Gta(ReportData r)
    {
        var g = r.Gta;
        var path = r.Fivem.Ini.TryGetValue("IVPath", out var iv) ? iv : "";
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) path = GtaFromRegistry();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        g.Found = true;
        g.Path = Privacy.Mask(path);
        g.DriveLetter = Path.GetPathRoot(Resolve(path))?[..1] ?? "";
        g.Store = path.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ? "Steam"
            : path.Contains("Epic", StringComparison.OrdinalIgnoreCase) ? "Epic Games" : "Rockstar Games Launcher";

        var enhanced = Path.Combine(path, "GTA5_Enhanced.exe");
        var legacy = Path.Combine(path, "GTA5.exe");
        var exe = File.Exists(legacy) ? legacy : File.Exists(enhanced) ? enhanced : "";
        g.Edition = exe == legacy ? "Legacy (quella che usa FiveM)" : exe == enhanced ? "Enhanced" : "non trovata";
        if (exe.Length > 0)
            try { g.ExeVersion = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? ""; } catch { }

        foreach (var file in SafeFiles(path, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (Regex.IsMatch(name, @"^(dinput8|dxgi|d3d11|d3d9|ScriptHookV|ScriptHookVDotNet\d*|ReShade\d*|opengl32|version|winmm)\.dll$|\.asi$|^(ReShade|enbseries|enblocal)\.ini$",
                    RegexOptions.IgnoreCase))
                g.RootMods.Add(name);
        }
        var modsDir = Path.Combine(path, "mods");
        if (Directory.Exists(modsDir))
        {
            var files = SafeFiles(modsDir, "*", SearchOption.AllDirectories).Take(20000).ToList();
            g.ModsFolderFiles = files.Count;
            g.ModsFolderBytes = files.Sum(x => { try { return new FileInfo(x).Length; } catch { return 0L; } });
        }
    }

    private static string GtaFromRegistry()
    {
        foreach (var (key, value) in new[]
                 {
                     (@"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V", "InstallFolder"),
                     (@"SOFTWARE\WOW6432Node\Rockstar Games\GTAV", "InstallFolderSteam"),
                     (@"SOFTWARE\WOW6432Node\Rockstar Games\GTAV", "InstallFolderEpic"),
                     (@"SOFTWARE\Rockstar Games\Grand Theft Auto V", "InstallFolder"),
                 })
        {
            using var k = Registry.LocalMachine.OpenSubKey(key);
            if (k?.GetValue(value) is string p && p.Length > 0)
            {
                p = p.TrimEnd('\\');
                if (p.EndsWith("GTA5.exe", StringComparison.OrdinalIgnoreCase)) p = Path.GetDirectoryName(p)!;
                if (Directory.Exists(p)) return p;
            }
        }
        return "";
    }

    internal static Dictionary<string, string> ParseIni(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('[') || line.StartsWith(';')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (Regex.IsMatch(key, "token|key|pass|auth|secret", RegexOptions.IgnoreCase)) continue;
            d[key] = line[(eq + 1)..].Trim();
        }
        return d;
    }

    /// <summary>gta5_settings.xml: every element with a value attribute, as "Section.Name" → value.</summary>
    internal static Dictionary<string, string> ParseGtaSettings(string xml)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = XDocument.Parse(xml);
        foreach (var e in doc.Descendants())
        {
            var v = e.Attribute("value")?.Value;
            if (v is null || e.Parent is null) continue;
            var section = e.Parent.Name.LocalName;
            section = char.ToUpperInvariant(section[0]) + section[1..];
            d[$"{section}.{e.Name.LocalName}"] = v;
        }
        return d;
    }

    internal static Dictionary<string, string> ParseCfg(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in CfgLine().Matches(text))
        {
            var key = m.Groups[1].Value;
            if (Regex.IsMatch(key, "token|key|pass|auth|secret|license|steam|discord|nick|name", RegexOptions.IgnoreCase)) continue;
            // Only what can change performance or what the client draws; crosshair colours and hashed profile keys are noise.
            if (!Regex.IsMatch(key, @"^(vid_|game_|nui_|r_|gfx_|cl_drawPerf|cl_drawFPS|net_maxMtu|streaming|sv_)", RegexOptions.IgnoreCase)) continue;
            d[key] = m.Groups[2].Value;
        }
        return d;
    }

    [GeneratedRegex(@"^\s*seta?\s+""?([\w.:-]+)""?\s+""?([^""\r\n]*)""?", RegexOptions.Multiline)]
    private static partial Regex CfgLine();

    internal static string GameBuild(string log)
    {
        var builds = Regex.Matches(log, @"\bb(\d{4})_(?:GTAProcess|DumpServer)|GTAProcess.*?\bb(\d{4})\b|FiveM_b(\d{4})_")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)
            .GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        return builds ?? "";
    }

    private static readonly Regex IssueRx = new(
        @"\b(error|errore|failed|failure|exception|crash|out of memory|oversized|could not|couldn't|unable to|timed out|timeout|streaming|corrupt|missing|not found)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Known noise: lines that show up on every healthy client.
    private static readonly Regex NoiseRx = new(
        @"ResourceCacheDevice|unknown native|Couldn't find native|discord.*rpc|Steam.*not running|LoadLibrary failed for 'xinput|SetThreadDescription",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static List<LogIssue> LogIssues(string log)
    {
        var counts = new Dictionary<string, int>();
        foreach (var raw in log.Split('\n'))
        {
            if (!IssueRx.IsMatch(raw) || NoiseRx.IsMatch(raw)) continue;
            var line = Regex.Replace(raw, @"^\[\s*\d+\]\s*", "");
            line = Regex.Replace(line, @"^\[[^\]]*\]\s*", "");
            line = Regex.Replace(line, @"^\S+/\s*", "");
            line = Privacy.Mask(line);
            line = Regex.Replace(line, @"0x[0-9a-fA-F]+|\b[0-9a-fA-F]{8,}\b", "#");
            line = Regex.Replace(line, @"\d+", "#").Trim();
            if (line.Length < 8) continue;
            if (line.Length > 180) line = line[..180] + "…";
            counts[line] = counts.TryGetValue(line, out var c) ? c + 1 : 1;
        }
        return counts.OrderByDescending(kv => kv.Value).Take(30)
            .Select(kv => new LogIssue { Text = kv.Key, Count = kv.Value }).ToList();
    }

    internal static string ModPack(string fileName)
    {
        var n = fileName.ToLowerInvariant();
        if (n.Contains("nve") || n.Contains("naturalvision")) return "NaturalVision Evolved";
        if (n.Contains("quantv")) return "QuantV";
        if (n.Contains("visualv")) return "VisualV";
        if (n.Contains("redux")) return "Redux";
        if (n.Contains("roads") || n.Contains("road")) return "strade";
        if (n.Contains("tree") || n.Contains("veg")) return "vegetazione";
        if (n.Contains("4k") || n.Contains("8k") || n.Contains("texture")) return "texture";
        if (n.Contains("sound") || n.Contains("audio") || n.Contains("gunshot")) return "suoni";
        if (n.Contains("blood") || n.Contains("fx") || n.Contains("particle")) return "effetti";
        return "";
    }

    private static string PluginKind(string file)
    {
        var name = Path.GetFileName(file).ToLowerInvariant();
        try
        {
            if (name.Contains(".dll"))
            {
                var vi = FileVersionInfo.GetVersionInfo(file);
                var desc = $"{vi.ProductName} {vi.FileDescription}".Trim();
                if (desc.Contains("ReShade", StringComparison.OrdinalIgnoreCase)) return "ReShade " + vi.ProductVersion;
                if (desc.Contains("ENB", StringComparison.OrdinalIgnoreCase)) return "ENBSeries";
                if (desc.Length > 0) return desc;
            }
        }
        catch { /* unreadable */ }
        if (name.Contains("nve")) return "NaturalVision Evolved";
        if (name.EndsWith(".asi") || name.Contains(".asi.")) return "plugin ASI";
        if (name.StartsWith("reshade")) return "ReShade";
        if (name.StartsWith("enb")) return "ENBSeries";
        return "";
    }

    internal static string Resolve(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var target = di.ResolveLinkTarget(true);
            if (target is not null) return target.FullName;
            // A link may sit on a parent folder too.
            var parent = di.Parent;
            if (parent is not null && parent.FullName != di.FullName)
            {
                var resolvedParent = Resolve(parent.FullName);
                if (!string.Equals(resolvedParent, parent.FullName, StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(resolvedParent, di.Name);
            }
        }
        catch { /* no access */ }
        return path;
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = option == SearchOption.AllDirectories,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System,
            }).ToList();
        }
        catch { return []; }
    }

    internal static long FolderSize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long total = 0;
        try
        {
            foreach (var fi in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions
                     { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                total += fi.Length;
        }
        catch { /* partial */ }
        return total;
    }

    internal static string ReadShared(string path, long maxBytes = 4 * 1024 * 1024)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > maxBytes) fs.Seek(-maxBytes, SeekOrigin.End);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    public static Process? FindGame()
    {
        foreach (var p in Process.GetProcesses())
        {
            if (p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase) &&
                p.ProcessName.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase))
                return p;
            p.Dispose();
        }
        return null;
    }

    public static bool FivemRunning()
    {
        using var game = FindGame();
        if (game is not null) return true;
        return Process.GetProcessesByName("FiveM").Length > 0;
    }
}
