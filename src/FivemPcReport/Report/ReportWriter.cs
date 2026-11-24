using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FivemPcReport.Analysis;
using FivemPcReport.Collect;
using FivemPcReport.Util;

namespace FivemPcReport.Report;

public sealed record WrittenReport(string Folder, string ReportPath, string PromptPath, string JsonPath, string PromptText);

public static class ReportWriter
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FiveM-PC-Report");

    public static WrittenReport Write(ReportData data, AnalysisResult analysis, List<Section> sections, string? root = null)
    {
        var folder = Path.Combine(root ?? DefaultRoot, data.CreatedAt.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(folder);

        var report = Markdown(data, analysis, sections);
        var prompt = Prompt(report);
        var reportPath = Path.Combine(folder, "report.md");
        var promptPath = Path.Combine(folder, "prompt-per-claude.md");
        var jsonPath = Path.Combine(folder, "dati.json");
        File.WriteAllText(reportPath, report, new UTF8Encoding(false));
        File.WriteAllText(promptPath, prompt, new UTF8Encoding(false));
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { data, analysis }, Json), new UTF8Encoding(false));
        return new WrittenReport(folder, reportPath, promptPath, jsonPath, prompt);
    }

    private static readonly Dictionary<Severity, (string Icon, string Title)> Groups = new()
    {
        [Severity.Critical] = ("🔴", "Problemi gravi"),
        [Severity.Warning] = ("🟠", "Da sistemare"),
        [Severity.Info] = ("🔵", "Da sapere"),
        [Severity.Good] = ("🟢", "Controllato, a posto"),
    };

    public static string Markdown(ReportData r, AnalysisResult a, List<Section> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Report FiveM PC");
        sb.AppendLine();
        sb.AppendLine($"Creato con FiveM PC Report {r.ToolVersion} il {r.CreatedAt:dd/MM/yyyy HH:mm}. " +
                      "Nome utente, nome del PC, indirizzi IP e identificativi sono oscurati.");
        sb.AppendLine();

        if (r.Notes.Name.Length + r.Notes.Problem.Length + r.Notes.Fps.Length > 0)
        {
            sb.AppendLine("## Cosa dice la persona");
            sb.AppendLine();
            if (r.Notes.Name.Length > 0) sb.AppendLine($"- **Chi**: {Esc(r.Notes.Name)}");
            if (r.Notes.Problem.Length > 0) sb.AppendLine($"- **Problema**: {Esc(r.Notes.Problem)}");
            if (r.Notes.Fps.Length > 0) sb.AppendLine($"- **FPS e dove**: {Esc(r.Notes.Fps)}");
            sb.AppendLine();
        }

        sb.AppendLine("## In breve");
        sb.AppendLine();
        sb.AppendLine($"- **Processore**: {r.Cpu.Name} ({r.Cpu.Cores} core / {r.Cpu.Threads} thread)");
        sb.AppendLine($"- **Scheda video**: {string.Join(" + ", r.Gpus.Select(g => $"{g.Name} ({Format.Bytes(g.VramBytes)})"))}");
        var mod = r.Memory.Modules.FirstOrDefault();
        sb.AppendLine($"- **RAM**: {Format.Gb(r.Memory.TotalBytes)}" +
                      (mod is null ? "" : $", {r.Memory.Modules.Count} banchi {mod.Type} a {r.Memory.Modules.Min(x => x.ConfiguredMts)} MT/s"));
        var primary = r.Displays.FirstOrDefault(d => d.Primary) ?? r.Displays.FirstOrDefault();
        if (primary is not null) sb.AppendLine($"- **Schermo**: {primary.Width}x{primary.Height} a {primary.RefreshHz} Hz");
        sb.AppendLine($"- **Windows**: {r.Windows.Caption} {r.Windows.Version}");
        sb.AppendLine($"- **Punteggio**: {a.Score}/100 ({a.Findings.Count(f => f.Severity == Severity.Critical)} gravi, {a.Findings.Count(f => f.Severity == Severity.Warning)} da sistemare)");
        sb.AppendLine();
        sb.AppendLine($"**{a.Verdict.Title}.** {a.Verdict.Text}");
        sb.AppendLine();

        foreach (var (sev, (icon, title)) in Groups)
        {
            var items = a.Findings.Where(f => f.Severity == sev).ToList();
            if (items.Count == 0) continue;
            sb.AppendLine($"## {icon} {title} ({items.Count})");
            sb.AppendLine();
            foreach (var f in items)
            {
                sb.AppendLine($"### {Esc(f.Title)}");
                sb.AppendLine();
                sb.AppendLine($"*{f.Area}.* {Esc(f.Detail)}");
                if (f.Fix.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"**Cosa fare:** {Esc(f.Fix)}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# Dati completi");
        sb.AppendLine();
        foreach (var s in sections)
        {
            sb.AppendLine($"## {s.Title}");
            sb.AppendLine();
            if (s.Rows.Count > 0)
            {
                sb.AppendLine("| | |");
                sb.AppendLine("|---|---|");
                foreach (var row in s.Rows) sb.AppendLine($"| {Cell(row[0])} | {Cell(row.Length > 1 ? row[1] : "")} |");
                sb.AppendLine();
            }
            foreach (var t in s.Tables)
            {
                if (t.Rows.Count == 0) continue;
                if (t.Title.Length > 0) { sb.AppendLine($"**{t.Title}**"); sb.AppendLine(); }
                sb.AppendLine("| " + string.Join(" | ", t.Headers.Select(Cell)) + " |");
                sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", t.Headers.Length)));
                foreach (var row in t.Rows) sb.AppendLine("| " + string.Join(" | ", row.Select(Cell)) + " |");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    public static string Prompt(string report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            Sei un tecnico esperto di PC da gioco, di GTA V e di FiveM. Qui sotto c'è il report completo di un PC che gioca
            su un server FiveM, creato da FiveM PC Report: hardware, Windows, impostazioni grafiche di FiveM, mod, cache,
            crash, errori del log e, se c'è, una misura fatta col gioco aperto (carico dei thread del gioco, della scheda
            video, memoria video). Il tool ha già scritto le sue conclusioni nelle sezioni con i pallini colorati: usale come
            punto di partenza, ma ragiona tu sui dati completi e correggile se i dati dicono altro.

            Il tuo compito:
            1. Dimmi in 2-3 frasi qual è la causa più probabile degli FPS bassi o dei problemi descritti, e perché (cita i
               numeri del report che lo dimostrano).
            2. Dammi le azioni da fare, in ordine dalla più utile alla meno utile, ognuna con: cosa fare passo per passo
               (menu e voci esatte di Windows, BIOS, pannello NVIDIA/AMD, impostazioni di FiveM), cosa aspettarsi, e se è
               reversibile. Prima le cose gratuite e veloci.
            3. Separa ciò che dipende dal PC da ciò che dipende dal server (per esempio interfacce pesanti, file enormi
               segnalati nel log): queste vanno riferite allo staff del server.
            4. Se manca un dato che serve davvero per decidere, dimmi esattamente come raccoglierlo (per esempio: rifare il
               report con FiveM aperto nel punto dove gli FPS sono più bassi).

            Rispondi in italiano semplice, senza gergo inutile. Non inventare dati che non sono nel report.

            ======== REPORT ========

            """);
        sb.Append(report);
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("\r", "").Replace("\n", " ");

    private static string Cell(string s) => Esc(s).Replace("|", "\\|");
}
