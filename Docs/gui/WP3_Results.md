# WP3 — frameworkvergelijking

**Stand:** 13 november 2026; in uitvoering. **Geen frameworkbesluit.**
Het [protocol](WP3_Measurement_Protocol.md) is vastgelegd vóór de prototypes.
De broncode staat in [spikes/gui](../../spikes/gui/README.md).

## Beschikbaar bewijs

- De drie slices zijn geïmplementeerd en cross-compileren op Linux.
- 4/4 geïsoleerde tests geslaagd; 534/534 bestaande regressietests geslaagd.
- Avalonia-headless: 10.000 items, tussen 1 en 200 gerealiseerde containers;
  filter naar 100 items en veilig sluiten gecontroleerd.
- Echte status/scan/doctor op synthetische wegwerprepository uitgevoerd.
- Ruwe model-/backendmetingen en tweevoudige Windows-crosspublicatie staan in
  `evidence/`. Definitieve meting volgt op de vastgelegde prototypebroncode.

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
