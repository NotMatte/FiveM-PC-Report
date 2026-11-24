# FiveM PC Report

Un programma per Windows che controlla il PC di chi gioca su FiveM e spiega **cosa rallenta il gioco**: processore,
RAM, scheda video, Windows, impostazioni di GTA V e FiveM, mod, plugin, crash e log. Alla fine scrive un report
leggibile e una versione pronta da incollare in Claude (o in un altro assistente) per farsi aiutare.

Pensato per lo staff dei server: lo mandi a chi ha pochi FPS o crasha, lui lo apre, preme un pulsante e ti
rimanda il report. Niente da installare, niente da configurare.

## Come si usa

1. Scarica `FiveM-PC-Report.exe` dalla pagina delle release e aprilo.
   Windows può mostrare "Windows ha protetto il PC" perché l'exe non è firmato: **Ulteriori informazioni →
   Esegui comunque**.
2. Scrivi, se vuoi, cosa succede e quanti FPS fai.
3. Premi **Avvia analisi**.
   - Con **FiveM chiuso** controlla hardware, Windows, impostazioni, mod e log (circa 20 secondi).
   - Con **FiveM aperto** puoi attivare l'**analisi dal vivo**: premi Avvia, torna in gioco e resta dove fai pochi
     FPS. Per 15, 30 o 60 secondi misura processore, scheda video, memoria video e interfacce del server e dice
     cosa limita gli FPS: la scheda video, un singolo thread del processore, il processore intero o un limite
     di FPS.
4. Alla fine vedi il punteggio, il verdetto e l'elenco di cosa sistemare, ognuno con **cosa fare**.
   - **Copia il prompt per Claude**: incollalo in Claude e chiedi aiuto.
   - **Apri il report** / **Apri la cartella**: i file da mandare a chi ti aiuta.

I report finiscono in `Documenti\FiveM-PC-Report\<data e ora>\`:

| File | Cosa contiene |
|---|---|
| `report.md` | Il report completo: verdetto, problemi trovati con la soluzione, tutti i dati |
| `prompt-per-claude.md` | Istruzioni per l'assistente seguite dal report: si incolla così com'è |
| `dati.json` | Gli stessi dati in formato macchina |

## Cosa controlla

- **Hardware**: processore (e microcode dei 13ª/14ª generazione Intel), RAM (canali, banchi diversi, XMP/EXPO
  spento letto dal codice del kit), scheda video e driver, schermi (risoluzione e frequenza reali), dischi e spazio
  libero.
- **Windows**: piano energetico, modalità gioco, Game DVR, pianificazione GPU, integrità della memoria (VBS),
  preferenza GPU dei portatili, programmi all'avvio, antivirus.
- **GTA V e FiveM**: impostazioni grafiche (`gta5_settings.xml`), `fivem.cfg`, canale di aggiornamento, build del
  gioco, mod nella cartella `mods`, plugin (ReShade, ENB, NaturalVision), cache, crash degli ultimi 30 giorni, righe
  del log che indicano problemi (asset troppo grandi, memoria esaurita).
- **Programmi aperti**: chi usa processore e RAM in background, overlay e programmi RGB.

## Privacy

Tutto resta sul PC: il programma non invia niente a nessuno e non entra nel gioco. Nel report sono oscurati nome
utente di Windows, nome del PC, indirizzi IP e identificativi di FiveM (license, steam, discord, xbl, live). Legge
soltanto: non modifica file, impostazioni o registro.

## Da riga di comando

```
FiveM-PC-Report.exe --headless [--live 30] [--out <cartella>] [--open]
```

`--headless` fa l'analisi senza finestra; `--live` aggiunge la misura dal vivo (FiveM deve essere aperto);
`--open` apre la cartella del report alla fine. Senza il runtime WebView2 (già presente su Windows 10 e 11
aggiornati) il programma passa da solo a questa modalità.

## Compilare dai sorgenti

Serve l'SDK di .NET 10.

```
dotnet build -c Release
dotnet test -c Release
dotnet publish src/FivemPcReport -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

L'interfaccia è HTML/CSS/JS in `src/FivemPcReport/Ui/wwwroot`, incorporata nell'exe e mostrata con WebView2.
Le regole che danno i consigli sono in `src/FivemPcReport/Analysis/Rules.cs`: ogni regola ha un test in
`tests/FivemPcReport.Tests`.

## Licenza

MIT, vedi [LICENSE](LICENSE). Componenti di terze parti in [THIRD_PARTY.md](THIRD_PARTY.md).
