using System.Globalization;
using System.Text.RegularExpressions;
using FivemPcReport.Collect;
using FivemPcReport.Util;

namespace FivemPcReport.Analysis;

public enum Severity { Critical, Warning, Info, Good }

public sealed class Finding
{
    public Severity Severity { get; set; }
    public string Area { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Fix { get; set; } = "";
}

public sealed class Verdict
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class AnalysisResult
{
    public List<Finding> Findings { get; set; } = [];
    public Verdict Verdict { get; set; } = new();
    public int Score { get; set; }
}

/// <summary>
/// Turns the collected data into findings: what is wrong, why it costs FPS, what to do. Each rule is
/// independent; thresholds are deliberately conservative, a "Good" finding is written when a common
/// suspect is fine so the reader knows it was checked.
/// </summary>
public static class Rules
{
    public static AnalysisResult Evaluate(ReportData r)
    {
        var a = new AnalysisResult();
        var f = a.Findings;
        var live = r.Live;
        bool cpuBound = live?.Bottleneck is "cpu-thread" or "cpu-all";
        bool gpuBound = live?.Bottleneck == "gpu";

        Hardware(r, f, cpuBound);
        Memory(r, f, cpuBound);
        Gpu(r, f, gpuBound);
        Power(r, f);
        Displays(r, f);
        WindowsSettings(r, f);
        Disks(r, f);
        GameSettings(r, f, cpuBound, gpuBound);
        Mods(r, f);
        FivemState(r, f);
        Processes(r, f);
        Live(r, f);

        a.Verdict = MakeVerdict(r);
        a.Findings = f.OrderBy(x => x.Severity).ToList();
        int critical = f.Count(x => x.Severity == Severity.Critical);
        int warning = f.Count(x => x.Severity == Severity.Warning);
        a.Score = Math.Clamp(100 - critical * 18 - warning * 6, 5, 100);
        return a;
    }

    private static void Add(List<Finding> f, Severity s, string area, string title, string detail, string fix = "") =>
        f.Add(new Finding { Severity = s, Area = area, Title = title, Detail = detail, Fix = fix });

    private static void Hardware(ReportData r, List<Finding> f, bool cpuBound)
    {
        var cpu = r.Cpu;
        if (cpu.Cores is > 0 and < 6)
            Add(f, Severity.Warning, "Processore", $"Processore con {cpu.Cores} core",
                "Su FiveM, con server pieni di script e interfacce, sotto i 6 core il processore resta spesso al limite.",
                "Chiudi tutto ciò che gira in background mentre giochi; a lungo termine il processore è il primo pezzo da cambiare.");

        // Intel 13th/14th gen Raptor Lake: instability and degradation fixed by microcode 0x12B (BIOS from late 2024).
        if (Regex.IsMatch(cpu.Name, @"i[579]-1[34]\d{3}", RegexOptions.IgnoreCase))
        {
            if (cpu.Microcode is { } mc && mc < 0x12B)
                Add(f, Severity.Critical, "Processore", "BIOS da aggiornare (processore Intel di 13ª/14ª generazione)",
                    $"Il microcode è 0x{mc:X}: le versioni prima di 0x12B causano crash, instabilità e un degrado nel tempo di questi processori.",
                    "Aggiorna il BIOS della scheda madre all'ultima versione dal sito del produttore (cerca il modello: " +
                    $"{(r.Machine.Motherboard.Length > 0 ? r.Machine.Motherboard : "scheda madre")}).");
            else if (cpu.Microcode is { } ok)
                Add(f, Severity.Good, "Processore", "Microcode Intel aggiornato", $"Microcode 0x{ok:X} (0x12B o successivo).");
        }

        if (r.Machine.BiosDate is { } bd && bd < DateTime.Now.AddYears(-3))
            Add(f, Severity.Info, "Scheda madre", "BIOS vecchio",
                $"BIOS del {Format.Date(bd)} ({r.Machine.BiosVersion}). Gli aggiornamenti spesso migliorano stabilità e compatibilità della RAM.",
                "Valuta un aggiornamento del BIOS dal sito del produttore della scheda madre.");
    }

    private static void Memory(ReportData r, List<Finding> f, bool cpuBound)
    {
        var m = r.Memory;
        var totalGb = m.TotalBytes / 1024d / 1024 / 1024;
        if (totalGb > 0 && totalGb < 12)
            Add(f, Severity.Critical, "RAM", $"Solo {Format.Gb(m.TotalBytes)} di RAM",
                "FiveM su un server RP con molte mappe, auto e vestiti custom usa da solo 8-12 GB: con meno di 16 GB il gioco scarica e ricarica di continuo.",
                "Porta la RAM almeno a 16 GB, meglio 32 GB.");
        else if (totalGb is >= 12 and < 15.5)
            Add(f, Severity.Warning, "RAM", $"{Format.Gb(m.TotalBytes)} di RAM",
                "Sotto i 16 GB, con un server pieno di contenuti custom, la RAM finisce presto.", "Porta la RAM a 16-32 GB.");
        else if (totalGb is >= 15.5 and < 20)
            Add(f, Severity.Info, "RAM", "16 GB di RAM",
                "16 GB bastano, ma con browser, Discord e altri programmi aperti sono al limite su server pesanti.",
                "Mentre giochi chiudi browser e programmi che non servono; 32 GB eliminano il problema.");

        var mods = m.Modules;
        if (mods.Count == 1 && !r.Machine.IsLaptop)
            Add(f, Severity.Warning, "RAM", "Un solo banco di RAM (single channel)",
                "Con un solo banco la memoria lavora a metà della banda: nei giochi limitati dal processore, come FiveM, si perdono anche il 15-25% degli FPS.",
                "Usa due banchi uguali (kit da 2), negli slot indicati dal manuale della scheda madre (di solito A2 e B2).");
        if (mods.Select(x => x.PartNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            Add(f, Severity.Info, "RAM", "Banchi di RAM diversi tra loro",
                "Banchi di modelli diversi spesso non reggono la velocità nominale (XMP/EXPO) e vanno più piano o sono instabili.",
                "Quando puoi, usa un kit unico.");

        var first = mods.FirstOrDefault();
        if (first is not null && first.ConfiguredMts > 0)
        {
            var rated = mods.Select(x => x.RatedMtsFromPartNumber ?? 0).Max();
            var cfg = mods.Min(x => x.ConfiguredMts);
            if (rated > cfg + 150)
                Add(f, cpuBound ? Severity.Critical : Severity.Warning, "RAM", "XMP/EXPO spento: la RAM va più piano di quanto può",
                    $"Il kit è da {rated} MT/s ma gira a {cfg} MT/s. In FiveM il processore aspetta la memoria: è uno dei motivi più comuni di FPS bassi su PC buoni.",
                    "Nel BIOS attiva il profilo XMP (Intel) o EXPO (AMD) e salva. Se il PC diventa instabile, prova il profilo più basso.");
            else if (rated > 0)
                Add(f, Severity.Good, "RAM", "RAM alla sua velocità", $"Configurata a {cfg} MT/s, come da specifica del kit ({rated}).");
            else if (!r.Machine.IsLaptop && ((first.Type == "DDR5" && cfg <= 4800) || (first.Type == "DDR4" && cfg <= 2400)))
                Add(f, Severity.Warning, "RAM", "RAM probabilmente senza XMP/EXPO",
                    $"{first.Type} a {cfg} MT/s è la velocità di base: quasi tutti i kit da gaming sono fatti per andare più veloce.",
                    "Controlla nel BIOS se c'è un profilo XMP/EXPO e attivalo.");
        }

        if (!m.PagefilePresent)
            Add(f, Severity.Warning, "RAM", "File di paging disattivato",
                "Senza file di paging, quando la RAM si riempie FiveM può chiudersi o bloccarsi invece di rallentare.",
                "Impostazioni di sistema avanzate → Prestazioni → Memoria virtuale: lascia che sia Windows a gestirla.");
    }

    private static void Gpu(ReportData r, List<Finding> f, bool gpuBound)
    {
        var dedicated = r.Gpus.Where(g => !g.IsIntegrated).ToList();
        if (r.Gpus.Count > 0 && dedicated.Count == 0)
            Add(f, Severity.Critical, "Scheda video", "Nessuna scheda video dedicata",
                "C'è solo la grafica integrata nel processore: FiveM resterà sotto i 30 FPS quasi ovunque.",
                "Serve una scheda video dedicata.");

        foreach (var g in dedicated)
        {
            if (g.DriverDate is { } dd)
            {
                var months = (DateTime.Now - dd).TotalDays / 30.4;
                if (months > 12)
                    Add(f, Severity.Warning, "Scheda video", "Driver della scheda video vecchio",
                        $"{g.Name}: driver {g.DriverFriendly} del {Format.Date(dd)} ({Format.AgeMonths(dd)}).",
                        g.Vendor == "NVIDIA" ? "Installa l'ultimo driver Game Ready da nvidia.com o dalla NVIDIA App."
                        : g.Vendor == "AMD" ? "Installa l'ultimo driver Adrenalin da amd.com." : "Aggiorna il driver dal sito del produttore.");
                else if (months > 6)
                    Add(f, Severity.Info, "Scheda video", "Driver della scheda video non recentissimo",
                        $"{g.Name}: driver {g.DriverFriendly} del {Format.Date(dd)} ({Format.AgeMonths(dd)}).",
                        "Aggiornarlo è gratis e spesso risolve scatti e crash.");
                else
                    Add(f, Severity.Good, "Scheda video", "Driver aggiornato", $"{g.Name}: {g.DriverFriendly} del {Format.Date(dd)}.");
            }
        }

        if (r.Machine.IsLaptop && dedicated.Count > 0 && r.Gpus.Any(g => g.IsIntegrated))
        {
            var prefs = r.Windows.GpuPreferences;
            if (prefs.Count == 0 || prefs.Values.Any(v => !v.StartsWith("prestazioni")))
                Add(f, Severity.Warning, "Scheda video", "Portatile con due schede video: FiveM potrebbe usare quella integrata",
                    "Su un portatile Windows può far girare il gioco sulla grafica del processore. Con quella si fanno 15-25 FPS anche avendo una scheda potente.",
                    "Impostazioni → Sistema → Schermo → Grafica: aggiungi FiveM.exe e il file FiveM_b*_GTAProcess.exe (nella cartella FiveM.app) e scegli \"Prestazioni elevate\". " +
                    "Nel pannello NVIDIA/AMD imposta la scheda dedicata come predefinita.");
        }
    }

    private static void Power(ReportData r, List<Finding> f)
    {
        var w = r.Windows;
        var m = r.Machine;
        if (m.HasBattery && m.OnAcPower == false)
            Add(f, Severity.Critical, "Alimentazione", "Il portatile va a batteria",
                "A batteria processore e scheda video vengono rallentati di molto: è normale vedere metà degli FPS o meno.",
                "Gioca sempre con il caricabatterie collegato.");

        switch (w.PowerPlanGuid)
        {
            case "a1841308-3bce-4f06-8b0b-7c6ab0ca0f35":
                Add(f, Severity.Critical, "Alimentazione", "Piano energetico \"Risparmio di energia\"",
                    "Il processore viene tenuto a frequenze basse: in gioco si perde moltissimo.",
                    "Pannello di controllo → Opzioni risparmio energia → \"Prestazioni elevate\" (o \"Bilanciato\" con la modalità \"Prestazioni migliori\").");
                break;
            case "381b4222-f694-41f0-9685-ff5bb260df2e":
                if (w.PowerModeOverlay.StartsWith("Prestazioni migliori"))
                    Add(f, Severity.Good, "Alimentazione", "Modalità di alimentazione \"Prestazioni migliori\"", "Piano Bilanciato con la modalità per le prestazioni.");
                else if (w.PowerModeOverlay.StartsWith("Massima"))
                    Add(f, Severity.Critical, "Alimentazione", "Modalità \"Massima efficienza energetica\"",
                        "Windows limita il processore per risparmiare: in gioco si perdono molti FPS.",
                        "Impostazioni → Sistema → Alimentazione → Modalità di alimentazione: \"Prestazioni migliori\".");
                else
                    Add(f, Severity.Info, "Alimentazione", "Piano energetico \"Bilanciato\"",
                        "Va bene per l'uso normale; per giocare la modalità \"Prestazioni migliori\" tiene il processore più reattivo.",
                        "Impostazioni → Sistema → Alimentazione → Modalità di alimentazione: \"Prestazioni migliori\" (oppure il piano \"Prestazioni elevate\").");
                break;
            case "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" or "e9a42b02-d5df-448d-aa00-03f14749eb61":
                Add(f, Severity.Good, "Alimentazione", $"Piano energetico \"{w.PowerPlan}\"", "Adatto al gioco.");
                break;
        }
    }

    private static void Displays(ReportData r, List<Finding> f)
    {
        foreach (var d in r.Displays)
        {
            if (d.MaxRefreshHzAtThisResolution > d.RefreshHz + 5)
                Add(f, Severity.Warning, "Schermo", $"Monitor impostato a {d.RefreshHz} Hz invece di {d.MaxRefreshHzAtThisResolution} Hz",
                    $"{(d.Primary ? "Schermo principale" : "Schermo")} {d.Width}x{d.Height}: Windows lo fa andare a {d.RefreshHz} Hz. Con il VSync il gioco non supera {d.RefreshHz} FPS e tutto sembra meno fluido.",
                    $"Impostazioni → Sistema → Schermo → Schermo avanzato → Frequenza di aggiornamento: {d.MaxRefreshHzAtThisResolution} Hz.");
        }
    }

    private static void WindowsSettings(ReportData r, List<Finding> f)
    {
        var w = r.Windows;
        if (w.Build is > 0 and < 19045)
            Add(f, Severity.Warning, "Windows", "Windows non aggiornato",
                $"{w.Caption} build {w.Build}: versione vecchia, senza le correzioni per i giochi e gli scheduler dei processori recenti.",
                "Aggiorna Windows da Windows Update.");
        if (w.MemoryIntegrity == true)
            Add(f, Severity.Info, "Windows", "Integrità della memoria (VBS/HVCI) attiva",
                "Protegge il sistema ma costa qualche punto percentuale di prestazioni nei giochi limitati dal processore (di solito 3-8%).",
                "Se cerchi ogni FPS: Sicurezza di Windows → Sicurezza dispositivi → Isolamento core → Integrità della memoria: disattiva e riavvia. È una scelta di sicurezza: valuta tu.");
        if (w.GameDvrBackgroundRecording == true)
            Add(f, Severity.Warning, "Windows", "Registrazione in background di Xbox Game Bar attiva",
                "Windows registra di continuo gli ultimi minuti di gioco: costa scheda video e disco.",
                "Impostazioni → Giochi → Acquisizioni: disattiva \"Registra cosa è successo\".");
        if (w.GameMode == false)
            Add(f, Severity.Info, "Windows", "Modalità gioco disattivata",
                "La modalità gioco dà priorità al gioco rispetto agli aggiornamenti e alle attività in background.",
                "Impostazioni → Giochi → Modalità gioco: attiva.");
        if (w.LastBoot is { } lb && (DateTime.Now - lb).TotalDays > 7)
            Add(f, Severity.Info, "Windows", $"PC acceso da {(int)(DateTime.Now - lb).TotalDays} giorni senza riavvio",
                "Con l'avvio rapido di Windows, \"spegni\" non svuota davvero la memoria: dopo giorni restano processi e memoria occupata.",
                "Riavvia il PC (Riavvia, non Spegni) prima di giocare.");
        var thirdParty = w.Antivirus.Where(a => !a.Contains("Defender", StringComparison.OrdinalIgnoreCase)).ToList();
        if (thirdParty.Count > 0)
            Add(f, Severity.Info, "Windows", "Antivirus di terze parti: " + string.Join(", ", thirdParty),
                "Alcuni antivirus scansionano ogni file che FiveM scarica dal server (migliaia di file di cache): caricamenti lenti e scatti.",
                "Aggiungi la cartella di FiveM alle esclusioni dell'antivirus.");
    }

    private static void Disks(ReportData r, List<Finding> f)
    {
        var byLetter = r.Disks.ToDictionary(d => d.Letter, StringComparer.OrdinalIgnoreCase);
        foreach (var (what, letter) in new[] { ("FiveM", r.Fivem.DriveLetter), ("GTA V", r.Gta.DriveLetter) })
        {
            if (string.IsNullOrEmpty(letter) || !byLetter.TryGetValue(letter, out var d)) continue;
            if (d.MediaType == "HDD")
                Add(f, Severity.Critical, "Disco", $"{what} è su un disco meccanico (HDD)",
                    $"Il disco {letter}: è un HDD. FiveM carica di continuo mappe, auto e vestiti dal disco: su HDD gli oggetti arrivano tardi e il gioco scatta.",
                    $"Sposta {what} su un SSD.");
        }
        foreach (var d in r.Disks)
        {
            bool relevant = d.Letter.Equals("C", StringComparison.OrdinalIgnoreCase) ||
                            d.Letter.Equals(r.Fivem.DriveLetter, StringComparison.OrdinalIgnoreCase);
            if (!relevant) continue;
            var freeGb = d.FreeBytes / 1024d / 1024 / 1024;
            if (freeGb < 10)
                Add(f, Severity.Critical, "Disco", $"Disco {d.Letter}: quasi pieno ({Format.Gb(d.FreeBytes)} liberi)",
                    "Con il disco pieno la cache di FiveM e il file di paging non hanno spazio: crash e caricamenti infiniti.",
                    "Libera almeno 30 GB.");
            else if (freeGb < 25)
                Add(f, Severity.Warning, "Disco", $"Disco {d.Letter}: poco spazio libero ({Format.Gb(d.FreeBytes)})",
                    "FiveM tiene una cache dei server che cresce di diversi GB.", "Libera spazio (almeno 30 GB liberi).");
        }
    }

    private static string? S(ReportData r, string key) =>
        r.Fivem.GraphicsSettings.TryGetValue(key, out var v) ? v : null;

    private static double? D(ReportData r, string key) =>
        S(r, key) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static void GameSettings(ReportData r, List<Finding> f, bool cpuBound, bool gpuBound)
    {
        if (!r.Fivem.GraphicsSettingsFound) return;

        var w = D(r, "Video.ScreenWidth");
        var h = D(r, "Video.ScreenHeight");
        var screen = r.Displays.FirstOrDefault(x => x.Primary) ?? r.Displays.FirstOrDefault();
        if (w is > 0 && h is > 0 && screen is not null && screen.NativeWidth > 0 &&
            w.Value * h.Value > screen.NativeWidth * (double)screen.NativeHeight * 1.05)
            Add(f, gpuBound ? Severity.Critical : Severity.Warning, "Grafica", $"Il gioco disegna a {w}x{h}, più del monitor ({screen.NativeWidth}x{screen.NativeHeight})",
                "È attivo un super-campionamento (DSR/DLDSR o VSR): la scheda video lavora molto di più per un'immagine solo un po' più nitida.",
                $"Nelle impostazioni grafiche di FiveM metti la risoluzione a {screen.NativeWidth}x{screen.NativeHeight}.");

        var rr = D(r, "Video.RefreshRate");
        if (rr is > 0 && screen is not null && screen.MaxRefreshHzAtThisResolution > rr + 5)
            Add(f, Severity.Warning, "Grafica", $"FiveM è impostato a {rr} Hz, il monitor arriva a {screen.MaxRefreshHzAtThisResolution} Hz",
                "Con la frequenza bassa nel gioco gli FPS vengono limitati (con il VSync) o l'immagine è meno fluida.",
                $"Impostazioni → Grafica → Frequenza di aggiornamento: {screen.MaxRefreshHzAtThisResolution} Hz.");

        var vsync = D(r, "Video.VSync");
        if (vsync is > 0)
            Add(f, Severity.Info, "Grafica", vsync == 2 ? "VSync a metà frequenza" : "VSync attivo",
                vsync == 2 ? "Il gioco è limitato a metà della frequenza del monitor (per esempio 30 FPS su 60 Hz)."
                           : "Gli FPS sono limitati alla frequenza del monitor; se il PC non ce la fa, scendono a scatti (60 → 30).",
                "Per misurare gli FPS veri disattivalo; con un monitor G-Sync/FreeSync puoi tenerlo spento.");

        if (D(r, "Graphics.DX_Version") is { } dx && dx < 2)
            Add(f, Severity.Warning, "Grafica", "Il gioco non usa DirectX 11",
                "Con DirectX 10/10.1 GTA V va peggio e ha meno opzioni.", "Impostazioni → Grafica → Versione DirectX: DirectX 11.");

        var maxLod = D(r, "Graphics.MaxLodScale");
        if (maxLod is > 0.2)
            Add(f, cpuBound ? Severity.Critical : Severity.Warning, "Grafica", "\"Ridimensionamento distanza estesa\" alto",
                $"Valore {maxLod:0.##}: il gioco disegna molti più oggetti lontani. È l'impostazione che pesa di più sul processore, soprattutto su server con mappe custom.",
                "Grafica avanzata → Ridimensionamento distanza estesa: al minimo.");

        if (D(r, "Graphics.GrassQuality") is >= 3)
            Add(f, cpuBound || gpuBound ? Severity.Warning : Severity.Info, "Grafica", "Erba su \"Ultra\"",
                "L'erba Ultra è una delle impostazioni più pesanti di GTA V, sia per il processore sia per la scheda video.",
                "Grafica → Qualità erba: Alta (o Normale).");

        if (S(r, "Graphics.Shadow_LongShadows") is "true")
            Add(f, cpuBound ? Severity.Warning : Severity.Info, "Grafica", "\"Ombre lunghe\" attive",
                "Fa calcolare le ombre su distanze maggiori al tramonto e all'alba: costa processore.",
                "Grafica avanzata → Ombre lunghe: disattiva.");

        if (D(r, "Graphics.ReflectionQuality") is >= 3)
            Add(f, cpuBound ? Severity.Warning : Severity.Info, "Grafica", "Riflessi su \"Ultra\"",
                "I riflessi ridisegnano la scena una seconda volta: su Ultra pesano su processore e scheda video.",
                "Grafica → Qualità riflessi: Molto alta o Alta.");

        if (D(r, "Graphics.ReflectionMSAA") is >= 4)
            Add(f, gpuBound ? Severity.Warning : Severity.Info, "Grafica", $"MSAA dei riflessi a {D(r, "Graphics.ReflectionMSAA")}x",
                "Pesa sulla scheda video quasi senza differenze visibili.", "Grafica → MSAA riflessi: disattivato o 2x.");

        if (D(r, "Graphics.MSAA") is >= 4)
            Add(f, gpuBound ? Severity.Critical : Severity.Warning, "Grafica", $"MSAA a {D(r, "Graphics.MSAA")}x",
                "L'MSAA alto in GTA V costa moltissimo alla scheda video e alla memoria video.",
                "Grafica → MSAA: disattivato o 2x (usa FXAA per ammorbidire i bordi).");

        var tex = D(r, "Graphics.TextureQuality");
        var vram = r.Gpus.Where(g => !g.IsIntegrated).Select(g => g.VramBytes ?? 0).DefaultIfEmpty(0).Max();
        var vramGb = vram / 1024d / 1024 / 1024;
        if (tex is >= 2 && vramGb is > 0 and <= 8.5)
            Add(f, Severity.Warning, "Grafica", $"Texture \"Molto alta\" con {vramGb:0} GB di memoria video",
                "Sui server con tante auto, vestiti e mappe custom la memoria video si riempie: quando succede gli FPS crollano e le texture arrivano sfocate.",
                "Grafica → Qualità texture: Alta.");
        else if (tex is >= 1 && vramGb is > 0 and <= 4.5)
            Add(f, Severity.Warning, "Grafica", $"Texture \"Alta\" con {vramGb:0} GB di memoria video",
                "Con 4 GB o meno la memoria video si riempie in fretta sui server custom.", "Grafica → Qualità texture: Normale.");

        if (S(r, "Graphics.UltraShadows_Enabled") is "true" && gpuBound)
            Add(f, Severity.Info, "Grafica", "Ombre ad alta risoluzione attive", "Pesano sulla scheda video.", "Grafica avanzata → Ombre ad alta risoluzione: disattiva.");

        if (D(r, "Graphics.Tessellation") is >= 3 && gpuBound)
            Add(f, Severity.Info, "Grafica", "Tassellazione al massimo", "Pesa sulla scheda video.", "Grafica → Tassellazione: Alta o Normale.");
    }

    private static void Mods(ReportData r, List<Finding> f)
    {
        var activeMods = r.Fivem.Mods.Where(m => !m.Disabled).ToList();
        var modBytes = activeMods.Sum(m => m.Bytes);
        if (modBytes > 1L << 30)
        {
            var packs = activeMods.Select(m => m.Detail).Where(d => d.Length > 0).Distinct().ToList();
            Add(f, Severity.Warning, "Mod", $"Mod grafiche nella cartella mods di FiveM ({Format.Bytes(modBytes)})",
                $"{activeMods.Count} file{(packs.Count > 0 ? ": " + string.Join(", ", packs) : "")}. Pacchetti grafici come NaturalVision o QuantV sostituiscono modelli, texture e luci: pesano su processore e memoria video, e su un server già pesante possono dimezzare gli FPS.",
                "Per provare: sposta i file da FiveM.app\\mods in un'altra cartella (non cancellarli), gioca e confronta gli FPS.");
        }
        else if (activeMods.Count > 0)
            Add(f, Severity.Info, "Mod", $"{activeMods.Count} file nella cartella mods di FiveM ({Format.Bytes(modBytes)})",
                "Mod leggere: di solito non cambiano gli FPS.", "Se hai problemi, prova comunque a toglierle per un confronto.");

        // Only loadable files count (a .dll or .asi without .off): ReShade.ini, logs and backups do nothing alone.
        var loaded = r.Fivem.Plugins.Where(p => !p.Disabled && Regex.IsMatch(p.Name, @"\.(dll|asi)$", RegexOptions.IgnoreCase)).ToList();
        foreach (var p in loaded.Where(p => p.Detail.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)))
            Add(f, Severity.Warning, "Mod", "ReShade attivo",
                $"{p.Name} ({p.Detail}). ReShade lavora sul fotogramma finito: su un PC già al limite toglie dal 5 al 20% degli FPS, di più con preset pesanti (ombre, illuminazione globale).",
                "Per provare: rinomina " + p.Name + " in " + p.Name + ".off nella cartella FiveM.app\\plugins, a FiveM chiuso.");
        foreach (var p in loaded.Where(p => p.Detail.StartsWith("ENB", StringComparison.OrdinalIgnoreCase)))
            Add(f, Severity.Critical, "Mod", "ENBSeries attivo",
                $"{p.Name}: gli ENB in GTA V costano moltissimo e su FiveM causano anche crash.", "Toglilo dalla cartella plugins.");
        var nve = loaded.Where(p => p.Detail.Contains("NaturalVision")).Select(p => p.Name).ToList();
        if (nve.Count > 0)
            Add(f, Severity.Info, "Mod", "Plugin di NaturalVision attivo",
                string.Join(", ", nve) + ": parte di NVE, aggiunge effetti di luce e meteo; costa un po' di processore.", "");
        var others = loaded.Where(p => !p.Detail.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase) &&
                                       !p.Detail.StartsWith("ENB", StringComparison.OrdinalIgnoreCase) &&
                                       !p.Detail.Contains("NaturalVision")).Select(p => p.Name).ToList();
        if (others.Count > 0)
            Add(f, Severity.Info, "Mod", "Altri plugin nella cartella plugins di FiveM",
                string.Join(", ", others) + ". Ogni plugin entra nel gioco: se ci sono crash o FPS bassi, prova senza.",
                "Per provare: aggiungi .off alla fine del nome del file, a FiveM chiuso.");

        if (r.Gta.RootMods.Count > 0)
            Add(f, Severity.Info, "Mod", "File di mod nella cartella di GTA V",
                string.Join(", ", r.Gta.RootMods) + ". FiveM di solito li ignora, ma ReShade/ENB o vecchi file possono dare problemi o farlo partire male.",
                "Se FiveM crasha all'avvio, spostali temporaneamente fuori dalla cartella di GTA V.");
    }

    private static void FivemState(ReportData r, List<Finding> f)
    {
        var fv = r.Fivem;
        if (!fv.Installed)
        {
            Add(f, Severity.Info, "FiveM", "FiveM non trovato", "La cartella %LOCALAPPDATA%\\FiveM\\FiveM.app non esiste su questo PC.");
            return;
        }
        if (!r.Gta.Found)
            Add(f, Severity.Warning, "GTA V", "Cartella di GTA V non trovata", "FiveM non ha un percorso di GTA V valido in CitizenFX.ini.");
        else if (r.Gta.Edition.StartsWith("Enhanced"))
            Add(f, Severity.Warning, "GTA V", "Solo GTA V Enhanced", "FiveM usa la versione Legacy di GTA V.", "Installa anche GTA V Legacy dal tuo launcher.");

        if (fv.CrashesLast30Days >= 3)
            Add(f, Severity.Warning, "FiveM", $"{fv.CrashesLast30Days} crash negli ultimi 30 giorni",
                "Crash frequenti di solito vengono da RAM instabile (XMP troppo spinto), driver, mod nella cartella mods/plugins o overlay.",
                "Prova senza mod e overlay; se continua, controlla la stabilità della RAM.");

        var cacheTotal = fv.CacheBytes + fv.ServerCacheBytes + fv.ServerCachePrivBytes;
        if (cacheTotal > 40L << 30)
            Add(f, Severity.Info, "FiveM", $"Cache di FiveM molto grande ({Format.Bytes(cacheTotal)})",
                "Non rallenta da sola, ma una cache rovinata dà texture mancanti e crash su un server.",
                "Se hai texture strane o crash su un solo server: a FiveM chiuso cancella FiveM.app\\data\\server-cache e server-cache-priv (non 'game-storage').");

        var oversized = fv.LogIssues.Where(i => Regex.IsMatch(i.Text, "oversized|out of memory|memory", RegexOptions.IgnoreCase)).Sum(i => i.Count);
        if (oversized > 0)
            Add(f, Severity.Warning, "FiveM", "Il log di FiveM segnala problemi di memoria o file enormi",
                $"{oversized} righe su memoria o asset troppo grandi nell'ultimo log: sono di solito auto, vestiti o mappe del server troppo pesanti.",
                "Se sei del server: ottimizza quegli asset. Se sei un giocatore: riferiscilo allo staff con il report.");
    }

    private static void Processes(ReportData r, List<Finding> f)
    {
        var heavy = r.Processes.TopCpu
            .Where(p => p.CpuPercentOfOneCore >= 25 && !p.Name.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase) &&
                        !p.Name.Equals("FiveM-PC-Report", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (heavy.Count > 0)
            Add(f, Severity.Warning, "Programmi", "Programmi che usano il processore in background",
                string.Join(", ", heavy.Select(p => $"{(p.Label.Length > 0 ? p.Label : p.Name)} {p.CpuPercentOfOneCore:0}% di un core")) +
                ". Tolgono processore al gioco proprio quando serve.",
                "Chiudili mentre giochi (o controlla cosa stanno facendo: aggiornamenti, scansioni, download).");

        var overlays = r.Processes.Notable.Where(p => Regex.IsMatch(p.Label,
            "overlay|registrazione|Wallpaper|RGB|Armoury|iCUE|G HUB|Synapse|NZXT|Afterburner|RivaTuner|Lossless|Overwolf|Medal", RegexOptions.IgnoreCase)).ToList();
        if (overlays.Count > 0)
            Add(f, Severity.Info, "Programmi", "Overlay, registrazione e programmi RGB aperti",
                string.Join(", ", overlays.Select(p => p.Label).Distinct()) +
                ". Ognuno costa poco, insieme possono causare scatti; gli overlay in gioco (Discord, Steam, NVIDIA) a volte calano gli FPS su FiveM.",
                "Per una prova pulita chiudili tutti e disattiva gli overlay in gioco.");
    }

    private static void Live(ReportData r, List<Finding> f)
    {
        var l = r.Live;
        if (l is null) return;

        var vram = r.Gpus.Where(g => !g.IsIntegrated).Select(g => g.VramBytes ?? 0).DefaultIfEmpty(0).Max();
        // The game's own allocation can exceed what is resident on the card: that already means spill-over.
        var used = Math.Max(l.GpuVramUsedBytes ?? 0, l.GameVramBytes ?? 0);
        if (vram > 0 && used > vram * 0.92)
            Add(f, Severity.Critical, "Dal vivo", "Memoria video piena",
                $"In gioco erano occupati o richiesti {Format.Bytes(used)} su {Format.Bytes(vram)}. Quando la memoria video finisce il gioco usa la RAM al suo posto: FPS che crollano, scatti e texture sfocate.",
                "Abbassa Qualità texture di un livello, togli MSAA e le mod grafiche; chiudi browser e programmi che usano la scheda video.");

        if (l.AvailableRamBytes is > 0 and < 1_500_000_000L)
            Add(f, Severity.Critical, "Dal vivo", "RAM quasi finita durante il gioco",
                $"Solo {Format.Bytes(l.AvailableRamBytes)} di RAM liberi con FiveM aperto: Windows sposta memoria sul disco e il gioco scatta.",
                "Chiudi browser e programmi aperti; se succede sempre, serve più RAM.");

        if (l.CpuPerformancePercent is { } perf && perf < 80 && l.SystemCpuPercent > 20)
            Add(f, Severity.Warning, "Dal vivo", "Il processore va più piano del normale",
                $"Durante il gioco lavorava al {perf:0}% della sua frequenza base: segno di risparmio energetico, temperatura alta o limiti di potenza.",
                "Controlla piano energetico e temperature (con HWiNFO); su un portatile usa la modalità prestazioni del produttore e il caricabatterie.");

        if (l.BrowserCpuPercentOfOneCore >= 60)
            Add(f, Severity.Info, "Dal vivo", "Interfacce del server pesanti",
                $"I processi delle interfacce di FiveM (pagine web dei menu, HUD, telefono) usano {l.BrowserCpuPercentOfOneCore:0}% di un core anche a menu chiusi. È un costo del server, uguale per tutti i giocatori.",
                "Se sei dello staff: controlla le interfacce che animano qualcosa anche quando sono nascoste.");

        if (l.GpuBusyPercent is { } busy && l.GameGpuPercent is { } mine && busy - mine > 20)
            Add(f, Severity.Warning, "Dal vivo", "Un altro programma usa la scheda video",
                $"La scheda video era occupata al {busy:0}%, il gioco ne usava il {mine:0}%: il resto lo prende un altro programma (browser con video, wallpaper animati, registrazione).",
                "Chiudi i programmi che usano la scheda video mentre giochi.");
    }

    private static Verdict MakeVerdict(ReportData r)
    {
        var l = r.Live;
        if (l is null)
            return new Verdict
            {
                Kind = "static",
                Title = "Analisi senza il gioco aperto",
                Text = "Ho controllato hardware, Windows e impostazioni. Per capire cosa limita gli FPS (processore o scheda video), " +
                       "apri FiveM, entra nel server, mettiti dove fai pochi FPS e rifai l'analisi con \"Analisi dal vivo\".",
            };

        var top = l.TopThreadsPercentOfOneCore.FirstOrDefault();
        return l.Bottleneck switch
        {
            "gpu" => new Verdict
            {
                Kind = "gpu", Title = "Il limite è la scheda video",
                Text = $"La scheda video era occupata al {l.GpuBusyPercent:0}%: lavora al massimo. Per più FPS abbassa le impostazioni che pesano sulla scheda video: risoluzione, MSAA, ombre ad alta risoluzione, riflessi, ReShade.",
            },
            "cpu-thread" => new Verdict
            {
                Kind = "cpu", Title = "Il limite è il processore (un thread del gioco al massimo)",
                Text = $"Un thread del gioco era al {top:0}% di un core mentre la scheda video era al {l.GpuBusyPercent:0}%. GTA V prepara i fotogrammi su un solo thread: " +
                       "contano la frequenza del processore, la velocità della RAM (XMP/EXPO), le impostazioni che aumentano gli oggetti da disegnare (distanza estesa, erba, ombre lunghe, riflessi), le mod grafiche e quanto è pesante la zona del server.",
            },
            "cpu-all" => new Verdict
            {
                Kind = "cpu", Title = "Il processore è pieno su tutti i core",
                Text = $"Il processore era al {l.SystemCpuPercent:0}% complessivo: ci sono troppi programmi in background o il processore è troppo debole per questo server.",
            },
            "cap" => new Verdict
            {
                Kind = "cap", Title = "Né processore né scheda video al massimo",
                Text = $"Scheda video al {Format.Pct(l.GpuBusyPercent)} e thread più carico al {top:0}%: gli FPS sono limitati da qualcos'altro. Di solito VSync o limitatore di FPS, risparmio energetico, oppure il gioco era in pausa o in un menu durante la misura.",
            },
            _ => new Verdict
            {
                Kind = "mixed", Title = "Carico misto",
                Text = $"Scheda video al {Format.Pct(l.GpuBusyPercent)}, thread più carico al {top:0}% di un core: nessuno dei due è chiaramente al limite. Rifai la misura nel punto dove gli FPS sono più bassi.",
            },
        };
    }
}
