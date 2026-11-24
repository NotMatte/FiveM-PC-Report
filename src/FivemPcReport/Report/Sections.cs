using FivemPcReport.Collect;
using FivemPcReport.Util;

namespace FivemPcReport.Report;

public sealed class Section
{
    public string Title { get; set; } = "";
    public List<string[]> Rows { get; set; } = [];
    public List<Table> Tables { get; set; } = [];
}

public sealed class Table
{
    public string Title { get; set; } = "";
    public string[] Headers { get; set; } = [];
    public List<string[]> Rows { get; set; } = [];
}

/// <summary>The data as readable sections: used by both the Markdown report and the app's detail view.</summary>
public static class Sections
{
    // Graphics settings shown with their in-game name; the rest of gta5_settings.xml follows in raw form.
    private static readonly (string Key, string Label)[] SettingLabels =
    [
        ("Video.ScreenWidth", "Risoluzione larghezza"), ("Video.ScreenHeight", "Risoluzione altezza"),
        ("Video.RefreshRate", "Frequenza (Hz)"), ("Video.Windowed", "Finestra (0 schermo intero, 1 finestra, 2 senza bordi)"),
        ("Video.VSync", "VSync (0 no, 1 sì, 2 metà)"), ("Graphics.DX_Version", "DirectX (2 = DX11)"),
        ("Graphics.TextureQuality", "Qualità texture (0-2)"), ("Graphics.ShaderQuality", "Qualità shader (0-2)"),
        ("Graphics.ShadowQuality", "Qualità ombre (0-3)"), ("Graphics.ReflectionQuality", "Qualità riflessi (0-3)"),
        ("Graphics.ReflectionMSAA", "MSAA riflessi"), ("Graphics.WaterQuality", "Qualità acqua (0-2)"),
        ("Graphics.ParticleQuality", "Qualità particelle (0-2)"), ("Graphics.GrassQuality", "Qualità erba (0-3)"),
        ("Graphics.Shadow_SoftShadows", "Ombre morbide"), ("Graphics.PostFX", "Effetti post-elaborazione (0-3)"),
        ("Graphics.MSAA", "MSAA"), ("Graphics.FXAA_Enabled", "FXAA"), ("Graphics.TXAA_Enabled", "TXAA"),
        ("Graphics.AnisotropicFiltering", "Filtro anisotropico"), ("Graphics.SSAO", "Occlusione ambientale (0-2)"),
        ("Graphics.Tessellation", "Tassellazione (0-3)"), ("Graphics.LodScale", "Ridimensionamento distanza"),
        ("Graphics.MaxLodScale", "Ridimensionamento distanza estesa"), ("Graphics.Shadow_LongShadows", "Ombre lunghe"),
        ("Graphics.UltraShadows_Enabled", "Ombre ad alta risoluzione"), ("Graphics.HdStreamingInFlight", "Streaming HD in volo"),
        ("Graphics.Shadow_Distance", "Distanza ombre"), ("Graphics.SamplingMode", "Ridimensionamento fotogrammi (SamplingMode)"),
        ("Graphics.CityDensity", "Densità popolazione"), ("Graphics.PedVarietyMultiplier", "Varietà pedoni"),
        ("Graphics.VehicleVarietyMultiplier", "Varietà veicoli"), ("Graphics.DoF", "Profondità di campo"),
        ("Graphics.MotionBlurStrength", "Sfocatura movimento"),
    ];

    public static List<Section> Build(ReportData r)
    {
        var list = new List<Section>();

        var m = r.Machine;
        list.Add(new Section
        {
            Title = "Computer",
            Rows =
            [
                ["Modello", $"{m.Manufacturer} {m.Model}".Trim()],
                ["Tipo", m.IsLaptop ? "portatile" : "fisso"],
                ["Scheda madre", m.Motherboard],
                ["BIOS", $"{m.BiosVersion} del {Format.Date(m.BiosDate)}"],
                .. m.HasBattery ? new[] { new[] { "Alimentazione", (m.OnAcPower == true ? "caricabatterie collegato" : "a batteria") + $" ({m.BatteryPercent}%)" } } : [],
            ],
        });

        var c = r.Cpu;
        list.Add(new Section
        {
            Title = "Processore",
            Rows =
            [
                ["Modello", c.Name],
                ["Core / thread", $"{c.Cores} / {c.Threads}"],
                ["Frequenza base", c.MaxClockMhz > 0 ? $"{c.MaxClockMhz} MHz" : "n/d"],
                ["Cache L3", c.L3CacheKb > 0 ? $"{c.L3CacheKb / 1024} MB" : "n/d"],
                ["Microcode", c.Microcode is { } mc ? $"0x{mc:X}" : "n/d"],
            ],
        });

        var mem = r.Memory;
        list.Add(new Section
        {
            Title = "Memoria RAM",
            Rows =
            [
                ["Totale", Format.Gb(mem.TotalBytes)],
                ["Libera durante l'analisi", Format.Gb(mem.AvailableBytes)],
                ["File di paging", mem.PagefilePresent ? (mem.PagefileAutomatic ? "gestito da Windows" : Format.Bytes(mem.PagefileBytes)) : "disattivato"],
            ],
            Tables =
            [
                new Table
                {
                    Title = "Banchi",
                    Headers = ["Slot", "Capacità", "Tipo", "Velocità attuale", "Velocità massima (SPD)", "Velocità del kit (dal codice)", "Produttore", "Codice"],
                    Rows = mem.Modules.Select(x => new[]
                    {
                        x.Slot, Format.Bytes(x.CapacityBytes), x.Type, x.ConfiguredMts > 0 ? $"{x.ConfiguredMts} MT/s" : "n/d",
                        x.SpeedMts > 0 ? $"{x.SpeedMts} MT/s" : "n/d", x.RatedMtsFromPartNumber is { } rr ? $"{rr} MT/s" : "n/d",
                        x.Manufacturer, x.PartNumber,
                    }).ToList(),
                },
            ],
        });

        list.Add(new Section
        {
            Title = "Scheda video",
            Tables =
            [
                new Table
                {
                    Headers = ["Nome", "Tipo", "Memoria video", "Driver", "Data del driver"],
                    Rows = r.Gpus.Select(g => new[]
                    {
                        g.Name, g.IsIntegrated ? "integrata" : "dedicata", Format.Bytes(g.VramBytes),
                        g.DriverFriendly == g.DriverVersion ? g.DriverVersion : $"{g.DriverFriendly} ({g.DriverVersion})",
                        $"{Format.Date(g.DriverDate)} ({Format.AgeMonths(g.DriverDate)})",
                    }).ToList(),
                },
            ],
            Rows = r.Windows.GpuPreferences.Select(kv => new[] { "Preferenza GPU di Windows per " + Path.GetFileName(kv.Key), kv.Value }).ToList(),
        });

        list.Add(new Section
        {
            Title = "Schermi",
            Tables =
            [
                new Table
                {
                    Headers = ["Schermo", "Principale", "Risoluzione", "Risoluzione massima", "Frequenza", "Frequenza massima"],
                    Rows = r.Displays.Select(d => new[]
                    {
                        $"{d.Monitor} ({d.Device})", d.Primary ? "sì" : "no", $"{d.Width}x{d.Height}",
                        $"{d.NativeWidth}x{d.NativeHeight}", $"{d.RefreshHz} Hz", $"{d.MaxRefreshHzAtThisResolution} Hz",
                    }).ToList(),
                },
            ],
        });

        list.Add(new Section
        {
            Title = "Dischi",
            Tables =
            [
                new Table
                {
                    Headers = ["Unità", "Tipo", "Modello", "Spazio", "Libero", "Contiene"],
                    Rows = r.Disks.Select(d => new[]
                    {
                        d.Letter + ":", $"{d.MediaType} {d.BusType}".Trim(), d.Model, Format.Bytes(d.TotalBytes), Format.Bytes(d.FreeBytes),
                        string.Join(", ", new[]
                        {
                            d.Letter.Equals("C", StringComparison.OrdinalIgnoreCase) ? "Windows" : "",
                            d.Letter.Equals(r.Fivem.DriveLetter, StringComparison.OrdinalIgnoreCase) ? "FiveM" : "",
                            d.Letter.Equals(r.Gta.DriveLetter, StringComparison.OrdinalIgnoreCase) ? "GTA V" : "",
                        }.Where(s => s.Length > 0)),
                    }).ToList(),
                },
            ],
        });

        var w = r.Windows;
        list.Add(new Section
        {
            Title = "Windows",
            Rows =
            [
                ["Versione", $"{w.Caption} {w.Version}, build {w.Build}.{w.Ubr}"],
                ["Ultimo avvio", w.LastBoot is { } lb ? $"{lb:dd/MM/yyyy HH:mm} ({(int)(DateTime.Now - lb).TotalDays} giorni fa)" : "n/d"],
                ["Piano energetico", w.PowerPlan + (w.PowerModeOverlay.Length > 0 ? $" · modalità: {w.PowerModeOverlay}" : "")],
                ["Modalità gioco", Format.YesNo(w.GameMode, "attiva", "disattivata")],
                ["Xbox Game Bar: acquisizione", Format.YesNo(w.GameDvrEnabled, "attiva", "disattivata")],
                ["Registrazione in background", Format.YesNo(w.GameDvrBackgroundRecording, "attiva", "disattivata")],
                ["Pianificazione GPU con accelerazione hardware", w.HardwareGpuScheduling is null ? "predefinita di Windows" : Format.YesNo(w.HardwareGpuScheduling, "attiva", "disattivata")],
                ["Sicurezza basata su virtualizzazione (VBS)", Format.YesNo(w.VbsRunning, "attiva", "disattivata")],
                ["Integrità della memoria (HVCI)", Format.YesNo(w.MemoryIntegrity, "attiva", "disattivata")],
                ["Antivirus", w.Antivirus.Count > 0 ? string.Join(", ", w.Antivirus) : "n/d"],
                ["Programmi all'avvio", w.StartupApps.Count > 0 ? $"{w.StartupApps.Count}: " + string.Join(", ", w.StartupApps.Select(Privacy.Mask)) : "nessuno"],
            ],
        });

        var g = r.Gta;
        list.Add(new Section
        {
            Title = "GTA V",
            Rows = g.Found
                ?
                [
                    ["Cartella", g.Path],
                    ["Versione", $"{g.Edition}, eseguibile {g.ExeVersion}"],
                    ["Launcher", g.Store],
                    ["File di mod nella cartella", g.RootMods.Count > 0 ? string.Join(", ", g.RootMods) : "nessuno"],
                    ["Cartella mods di GTA (OpenIV)", g.ModsFolderFiles > 0 ? $"{g.ModsFolderFiles} file, {Format.Bytes(g.ModsFolderBytes)} (FiveM non la usa)" : "assente"],
                ]
                : [["Cartella", "non trovata"]],
        });

        var fv = r.Fivem;
        var fivem = new Section { Title = "FiveM" };
        if (!fv.Installed) fivem.Rows.Add(["Stato", "non installato"]);
        else
        {
            fivem.Rows.Add(["Cartella", fv.AppPath + (fv.IsLinked ? " (collegamento)" : "")]);
            fivem.Rows.Add(["Canale di aggiornamento", fv.Ini.TryGetValue("UpdateChannel", out var ch) ? ch : "n/d"]);
            fivem.Rows.Add(["Build del gioco (dall'ultimo log)", fv.GameBuild.Length > 0 ? fv.GameBuild : "n/d"]);
            fivem.Rows.Add(["Cache", $"cache {Format.Bytes(fv.CacheBytes)} · server-cache {Format.Bytes(fv.ServerCacheBytes)} · server-cache-priv {Format.Bytes(fv.ServerCachePrivBytes)}"]);
            fivem.Rows.Add(["Crash negli ultimi 30 giorni", fv.CrashesLast30Days.ToString()]);
            if (fv.RecentCrashes.Count > 0) fivem.Rows.Add(["Ultimi crash", string.Join(" · ", fv.RecentCrashes)]);
            fivem.Rows.Add(["Ultimo log", fv.LatestLogName.Length > 0 ? $"{fv.LatestLogName} ({fv.LatestLogDate:dd/MM/yyyy HH:mm})" : "nessuno"]);
            foreach (var kv in fv.Cfg) fivem.Rows.Add([$"fivem.cfg: {kv.Key}", kv.Value]);

            fivem.Tables.Add(new Table
            {
                Title = $"Cartella mods ({fv.Mods.Count} file, {Format.Bytes(fv.Mods.Sum(x => x.Bytes))})",
                Headers = ["File", "Dimensione", "Tipo", "Attivo"],
                Rows = fv.Mods.OrderByDescending(x => x.Bytes).Select(x => new[] { x.Name, Format.Bytes(x.Bytes), x.Detail, x.Disabled ? "no" : "sì" }).ToList(),
            });
            fivem.Tables.Add(new Table
            {
                Title = $"Cartella plugins ({fv.Plugins.Count} file)",
                Headers = ["File", "Dimensione", "Cos'è", "Attivo"],
                Rows = fv.Plugins.Select(x => new[] { x.Name, Format.Bytes(x.Bytes), x.Detail, x.Disabled ? "no" : "sì" }).ToList(),
            });
            if (fv.GraphicsSettingsFound)
            {
                var known = SettingLabels.Where(s => fv.GraphicsSettings.ContainsKey(s.Key))
                    .Select(s => new[] { s.Label, fv.GraphicsSettings[s.Key], s.Key }).ToList();
                var rest = fv.GraphicsSettings.Where(kv => SettingLabels.All(s => s.Key != kv.Key))
                    .OrderBy(kv => kv.Key).Select(kv => new[] { "", kv.Value, kv.Key });
                fivem.Tables.Add(new Table
                {
                    Title = "Impostazioni grafiche (gta5_settings.xml di FiveM)",
                    Headers = ["Impostazione", "Valore", "Chiave"],
                    Rows = [.. known, .. rest],
                });
            }
            if (fv.LogIssues.Count > 0)
                fivem.Tables.Add(new Table
                {
                    Title = "Righe di errore più frequenti nell'ultimo log",
                    Headers = ["Volte", "Messaggio (numeri sostituiti da #)"],
                    Rows = fv.LogIssues.Select(i => new[] { i.Count.ToString(), i.Text }).ToList(),
                });
        }
        list.Add(fivem);

        if (r.Live is { } l)
            list.Add(new Section
            {
                Title = "Dal vivo (FiveM aperto)",
                Rows =
                [
                    ["Durata della misura", $"{l.Seconds} s"],
                    ["Processo del gioco", l.GameProcess],
                    ["Gioco: processore", $"{l.GameCpuPercentOfOneCore:0}% di un core (in totale)"],
                    ["Thread più carichi del gioco", l.ThreadsReadable ? string.Join(" · ", l.TopThreadsPercentOfOneCore.Select(x => $"{x:0}%")) + " di un core" : "non leggibili"],
                    ["Processore di tutto il PC", Format.Pct(l.SystemCpuPercent)],
                    ["Frequenza rispetto alla base", Format.Pct(l.CpuPerformancePercent)],
                    ["Scheda video occupata", Format.Pct(l.GpuBusyPercent)],
                    ["Scheda video usata dal gioco", Format.Pct(l.GameGpuPercent)],
                    ["Memoria video occupata sulla scheda", Format.Bytes(l.GpuVramUsedBytes)],
                    ["Memoria video allocata dal gioco", Format.Bytes(l.GameVramBytes) + (l.GameVramBytes > l.GpuVramUsedBytes ? " (più di quella sulla scheda: una parte può finire nella RAM)" : "")],
                    ["RAM usata dal gioco", Format.Bytes(l.GameWorkingSetBytes)],
                    ["RAM libera", Format.Bytes(l.AvailableRamBytes)],
                    ["Interfacce di FiveM (browser)", $"{l.BrowserProcesses} processi, {l.BrowserCpuPercentOfOneCore:0}% di un core"],
                ],
            });

        list.Add(new Section
        {
            Title = "Programmi aperti",
            Rows = [["Processi in esecuzione", r.Processes.Count.ToString()]],
            Tables =
            [
                new Table
                {
                    Title = "Più carichi sul processore (2 secondi di misura)",
                    Headers = ["Programma", "Istanze", "Processore", "RAM"],
                    Rows = r.Processes.TopCpu.Select(p => new[] { Name(p), p.Instances.ToString(), $"{p.CpuPercentOfOneCore:0}% di un core", Format.Bytes(p.MemoryBytes) }).ToList(),
                },
                new Table
                {
                    Title = "Più pesanti in RAM",
                    Headers = ["Programma", "Istanze", "RAM"],
                    Rows = r.Processes.TopMemory.Select(p => new[] { Name(p), p.Instances.ToString(), Format.Bytes(p.MemoryBytes) }).ToList(),
                },
                new Table
                {
                    Title = "Programmi da tenere d'occhio",
                    Headers = ["Programma", "RAM"],
                    Rows = r.Processes.Notable.Select(p => new[] { p.Label, Format.Bytes(p.MemoryBytes) }).ToList(),
                },
            ],
        });

        if (r.CollectorErrors.Count > 0)
            list.Add(new Section { Title = "Dati non letti", Rows = r.CollectorErrors.Select(e => new[] { "", e }).ToList() });

        return list;

        static string Name(ProcessUsage p) => p.Label.Length > 0 ? $"{p.Label} ({p.Name})" : p.Name;
    }
}
