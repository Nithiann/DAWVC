# WP3 — frameworkvergelijking

**Actueel besluit, 13 november 2026:** De gebruiker heeft Avalonia expliciet gekozen
voor Windows, macOS en Linux en opdracht gegeven WP4 te starten. De oorspronkelijke
vergelijkingsgate is daarmee vervangen; onderstaande ontbrekende metingen blijven
ontbrekend. Zie de geaccepteerde [ADR-GUI-001](../adr/ADR-GUI-001-desktop-framework.md).
De eerdere vergelijkingsresultaten en vervolgprocedure hieronder blijven bewaard
als historische context; ze blokkeren de frameworkkeuze niet langer.
Het [protocol](WP3_Measurement_Protocol.md) is vastgelegd vóór de prototypes.
De broncode staat in [spikes/gui](../../spikes/gui/README.md).

## Beschikbaar bewijs

- De drie slices zijn geïmplementeerd en cross-compileren op Linux.
- 4/4 geïsoleerde tests geslaagd; 534/534 bestaande regressietests geslaagd.
- Avalonia-headless: 10.000 items, tussen 1 en 200 gerealiseerde containers;
  filter naar 100 items en veilig sluiten gecontroleerd.
- Echte status/scan/doctor op synthetische wegwerprepository uitgevoerd.
- Ruwe model-/backendmetingen en tweevoudige Windows-crosspublicatie staan in
  `evidence/`, gekoppeld aan de vastgelegde prototypebroncode.

## Nog geen Windows-acceptatie

| Check | Avalonia | WPF | Photino.Blazor |
| --- | --- | --- | --- |
| Native Windows-slice | Niet uitgevoerd | Niet uitgevoerd | Niet uitgevoerd |
| First-renderstartup, procesboomgeheugen | Niet uitgevoerd | Niet uitgevoerd | Niet uitgevoerd |
| Filter tot render / UI-heartbeat | Niet uitgevoerd | Niet uitgevoerd | Niet uitgevoerd |
| Native scan/cancel | Niet uitgevoerd | Niet uitgevoerd | Niet uitgevoerd |
| Folderpicker kiezen/annuleren | Geïmplementeerd, niet native getest | Geïmplementeerd, niet native getest | Geïmplementeerd, niet native getest |
| Directory drag/drop | Geïmplementeerd, niet native getest | Geïmplementeerd, niet native getest | Nog open |
| Keyboard, focus, DPI, contrast | Niet uitgevoerd | Niet uitgevoerd | Niet uitgevoerd |
| Schone Windows-machine / WebView2 | Niet uitgevoerd | Niet uitgevoerd | Niet uitgevoerd |

Native starts zijn daadwerkelijk geprobeerd op deze Linux-host. Avalonia faalt
op `XOpenDisplay failed` (geen displayserver); Photino faalt op ontbrekende
`libwebkit2gtk-4.1.so.0`; WPF vereist Windows. Dit zijn omgevingsblokkades en geen
Windows-knock-outresultaten. Headless succes bewijst geen native UI-kwaliteit.

## Verder uitvoeren

1. Laat het aparte Windows-workflow bouwen/testen/publiceren en native openen/
   sluiten controleren. Bewaar logs/artifacts. Het workflow is nog niet door deze
   sessie als uitgevoerd geverifieerd; window-handlemetingen zijn geen first-render.
2. Voer het protocol op één Windows 11-vergelijkingsmachine uit voor alle drie
   kandidaten. Registreer CPU/RAM/SDK/commit, ruwe timings, procesboomgeheugen,
   screenshots, interactiechecks en ontwikkeltijd. Ontwikkeltijd is hier niet
   geïnstrumenteerd en wordt niet achteraf verzonnen.
3. Los Photino-directorydrop en origin-/navigatiereview op of onderbouw een
   afwijzing op concrete criteria. WebView2-verplichtingen en runtimeattributies
   moeten bij de vergelijking blijven staan.
4. Vul de tabel in, toets de vooraf vastgelegde budgetten en werk pas daarna
   ADR-GUI-001 en WP3-02 t/m WP3-06 af. WP4 blijft wachten op dit besluit.

## Ondersteunende Linux-metingen

Meetbron: prototypecommit `73d613b` (broncode voor deze bewijsregistratie);
Linux x64-container met quota van 4 CPU-cores en 32 GiB RAM, SDK 10.0.404,
runtime 10.0.15. Zie [omgeving en validatie](evidence/environment.json).

De gedeelde filterbenchmark bevat 60 modelmetingen (20× drie queries): p95
1,5975 ms, maximum 7,1385 ms. Dit meet geen UI-rendering en is geen
frameworkspecifiek acceptatieresultaat. Ruwe waarden:
[shared-filter-linux.json](evidence/shared-filter-linux.json).

Op de synthetische kleine fixture: echte status 603,97 ms; scan 23,66 ms;
doctor 118,87 ms. Alle drie gaven een resultaat zonder failurecategorie terug.
Status meldt `main`, nul staged en 101 untracked; doctor meldt score 100 met
één waarschuwing. Dit zijn afzonderlijke backend-smokes, geen UI-responscijfers.
Zie [backendresultaten](evidence/backend-linux.jsonl).

Op de 256 MiB-fixture zijn tien echte status-hashingoperaties geannuleerd. Het
verzoek wordt na de hashingfase-start met een timer gepland; de timestamp staat
vóór `CancellationTokenSource.Cancel`, de eindtijd na het afwachten van de taak.
Alle tien eindigden met cancellation; p95/max 26,823 ms. Dit vervangt geen native
scan/cancelmeting. Zie [ruwe cancellationmetingen](evidence/backend-cancellation-linux.jsonl).


## Windows x64 self-contained crosspublicatie vanuit Linux

| Kandidaat | Grootte (MiB) | Bestanden | Eerste / tweede publicatie (s) | Niet-PDB-hashes gelijk |
| --- | ---: | ---: | ---: | --- |
| Avalonia | 207.45 | 248 | 6.16 / 5.46 | Ja |
| Wpf | 143.39 | 425 | 5.90 / 6.04 | Ja |
| Photino | 82.68 | 251 | 12.05 / 5.50 | Ja |

Alle drie pakketten blijven onder het 250 MiB-budget; dit is alleen een
crosspublicatiecontrole. De tijden bevatten restore/build/publicatie en zijn
geen zuivere buildbenchmark. WebView2 is extern en telt niet mee in Photino's
pakketgrootte. Schone Windows-start en externe runtime-installatie zijn nog open.

Commando per kandidaat, tweemaal naar een verse outputmap:
`dotnet publish spikes/gui/<host>/<host>.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -o <fresh-output>`.
De definitieve runs zijn serieel uitgevoerd met `MSBUILDDISABLENODEREUSE=1` na
een eerdere MSB4166-buildnodefout tijdens overlappende .NET-tooling. Die mislukte
poging is geen geslaagde publicatie en telt niet mee in de tabel.

[Ruwe publicatieresultaten](evidence/windows-cross-publish-linux.json) verwijzen
naar de SHA-256-manifests per run. [Pakketlicentiemetadata](evidence/package-license-metadata.json)
is uit de daadwerkelijk geresolvde NuGet-graphs verzameld; dit is geen volledige
native/OS-licentie- of vulnerability-audit. Geen betaalde control toegevoegd.
