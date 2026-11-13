# WP3 — vooraf vastgelegd meetprotocol

Datum: 13 november 2026. Historisch protocol, vastgesteld vóór implementatie/metingen.
Later die dag koos de gebruiker expliciet Avalonia voor Windows, macOS en Linux.
De vergelijkende selectiegate is vervangen; de budgetten zijn niet achteraf gewijzigd.
Native kwaliteitsmetingen voor Avalonia gaan mee naar WP8 voor alle drie OS'en.
Ontbrekende vergelijkingsmetingen worden niet als geslaagd beschouwd.

## Gelijke slice en fixtures

Alle kandidaten (Avalonia, WPF, Photino.Blazor) gebruiken dezelfde `SpikeSession`
en Bootstrap/Application rechtstreeks, zonder CLI-processen. Elke host biedt:
folderpicker, repositorypad ook via toetsenbord/drop, echte status, 10.000 identieke
synthetische assetrijen met filtering en virtualisatie, scan/cancel, doctor en
dark/light. Real repository data en synthetische rijen staan afzonderlijk aangeduid.
Gebruik alleen een wegwerpfixture; status kan de hashcache wijzigen. Dit omzeilt
het openstaande WP5-08-probleem niet. Maak geen gebruik van commerciële fixtures.

`--fixture <nieuwe-map>` maakt deterministische minimale FLP-bytes, 100 synthetische
PCM-WAV-samples van 4 KiB en een geïnitialiseerde repository. `--large-fixture`
voegt een synthetische 256 MiB asset toe voor echte hashing/cancellation. De UI-rijen
zijn `Samples/Asset00000.wav` t/m `Samples/Asset09999.wav`, met vaste sizes/statussen.
Fixturecreatie mag een bestaande map nooit overschrijven.

## Vergelijkingsmachine en meetwijze

Beslissende metingen: dezelfde fysieke Windows 11 x64-machine, netvoeding,
minimaal 4 CPU-cores, 16 GiB RAM, lokale SSD, 1920×1080, 100% scaling. Noteer
CPU, RAM, Windows-versie, SDK, commit en pakketversies. Sluit overige apps.
Linux-/headlessmetingen zijn aanvullend en bewijzen geen Windows-acceptatie.
Geen frameworkkeuze zolang één vereiste slice of Windows-check ongetest is.

Publiceer Release, win-x64, self-contained, zonder trimming en zonder single-file
voor alle drie kandidaten. Herhaal publicatie met een lege outputmap; vergelijk
bestandshashes (zonder PDB's) om reproduceerbaarheid te toetsen. Noteer restore en
build apart. Meet vijf proceskoude starts (OS-cache niet geleegd), vanaf vóór
processtart tot het eerste werkelijk gerenderde venster; rapporteer alle runs,
mediaan en maximum. Een headless constructor of ViewModeltijd is geen startupmeting.

Na 10 s idle: private bytes en working set van de gehele procesboom, ook WebView2.
Herhaal na laden van 10.000 rijen en 10 s idle. Meet filteren op `Asset099`, leeg en
`no-match`, ieder 20 keer, vanaf invoer tot gerenderde resultaatlijst; rapporteer
p95 (nearest rank) en maximum. Shared-model filtertijd is apart ondersteunend bewijs.
Monitor heartbeat/UI-eventvertraging tijdens echte status/scan/doctor en scroll.
Meet cancel vanaf gebruikersactie tot de taak daadwerkelijk eindigt; behoud token,
wachttijd en uitkomst. Herhaal 10 keer op de grote fixture met een verse sessie.
Scan kan te snel zijn om betrouwbaar te annuleren: noteer dan "niet meetbaar" en
voer de status-hashingtest aanvullend uit, zonder die als scanresultaat te boeken.

## Budgetten (vooraf)

| Eigenschap | Acceptatiegrens |
| --- | --- |
| Proceskoude startup | mediaan ≤ 2 s, maximum ≤ 3 s |
| Idle private bytes, gehele procesboom | ≤ 250 MiB |
| Private bytes met 10.000 rijen | ≤ 400 MiB |
| Filter tot render | p95 ≤ 100 ms, maximum ≤ 200 ms |
| UI-eventvertraging tijdens backendwerk | p95 ≤ 100 ms, maximum ≤ 250 ms |
| Cancellation na annuleerverzoek | p95 ≤ 500 ms, maximum ≤ 2 s; geen vrijgave vóór einde taak |
| Virtuele lijst | hoogstens 200 gerealiseerde rijcontainers bij 10.000 rijen |
| Publicatiegrootte | ≤ 250 MiB per kandidaat; noteer externe runtimeverplichtingen |

Een overschrijding blijft een bevinding; wijzig budgetten niet achteraf om een
kandidaat te laten slagen. Leg eventuele herziening apart vast vóór een nieuwe run.

## Handmatige knock-outchecks op Windows

Per kandidaat: folderpicker kiezen én annuleren; drop van lokale repositorymap
(met spaties/Unicode); Tab/Shift+Tab, Enter, Escape, pijlen en PageDown; zichtbare
focus en bereikbaarheid van alle acties; scroll/filter met 10.000 rijen; DPI 100%,
150%, 200% en monitorwissel; dark/light inclusief fout- en disabled-states;
scan/cancel tijdens echte backend-I/O; doctorresultaat; venster sluiten tijdens
werk; procesboom eindigt; schone self-contained-start zonder geïnstalleerde .NET.
Photino vereist WebView2; leg installatiegedrag op een schone machine expliciet vast.

Een kandidaat valt af bij ontbreken van soepele virtualisatie, onopgeloste
UI-blokkades, onbetrouwbare packaging, verplichte custom native code voor basis-
interacties, ontoelaatbare commerciële controls of niet los testbare ViewModels.
Registreer ontwikkeltijd met een klok/log; schat geen retrospectieve cijfers.

## Bewijs en besluit

Bewaar ruwe JSON/CSV, logs, package-inventaris, screenshots en checks per kandidaat.
Gebruik `niet uitgevoerd`, `geblokkeerd` of `niet meetbaar` waar van toepassing,
nooit nul of een afgeleid pass-resultaat. Vul `WP3_Results.md` aan en laat ADR-GUI-001
op Proposed staan tot de drie Windows-slices en licentiecontrole onderbouwd zijn.
