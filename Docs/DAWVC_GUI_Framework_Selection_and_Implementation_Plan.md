# DAWVC GUI Framework Selection & Implementation Plan

**Status:** Voorstel voor besluitvorming  
**Datum:** 24 oktober 2026  
**Repositorybaseline:** DAWVC `v0.2.0-preview.1` (`8795fa6`)  
**Doel:** Een zelfstandig beslis- en implementatiedocument voor het toevoegen van een desktop-GUI aan DAWVC.  
**Uitvoering en voortgang:** [GUI-takenlijst per werkpakket](GUI_Implementation_Tasks.md). Deze takenlijst is de centrale statusregistratie; dit document blijft de ontwerp- en besluitvormingsbron.

---

## 1. Samenvatting

DAWVC is technisch ver genoeg om naast de CLI een volwaardige grafische desktopclient te krijgen. De huidige Application-laag bevat al use-cases voor repository-initialisatie, status, scanning, staging, commits, history, branches, checkout, dependencydiagnostiek, `doctor`, `fsck`, remotes, clone, fetch en pull. De GUI hoeft deze functionaliteit daarom niet opnieuw te implementeren; zij wordt een tweede presentation layer boven dezelfde DAWVC-engine.

De belangrijkste keuze is niet alleen welke controls mooi ogen. Eerst moet worden bepaald welk **GUI-framework** het venstermodel, renderingmodel, platformbereik, packaging en ontwikkelmodel bepaalt. Pas daarna wordt een **component- en themelibrary** gekozen.

De voorlopige voorkeursrichting voor DAWVC is:

```text
Avalonia UI
+ CommunityToolkit.Mvvm
+ officiële Avalonia FluentTheme
+ eigen DAWVC design tokens en controls
```

Deze keuze is nadrukkelijk nog geen definitief besluit. De aanbevolen besluitvorming bestaat uit een korte technische spike waarin Avalonia, WPF en één webgebaseerd alternatief dezelfde representatieve DAWVC-workflow implementeren. Als Windows permanent het enige ondersteunde desktopplatform blijft, is WPF een zeer sterke en pragmatische optie. Als macOS en Linux daadwerkelijk onderdeel zijn van de toekomstige multi-DAW-strategie, sluit Avalonia het beste aan.

---

## 2. Context: waarom een GUI nu logisch is

DAWVC `v0.2.0-preview.1` heeft inmiddels voldoende functionele breedte voor een zinvolle desktopclient:

- lokale repositories initialiseren en openen;
- FL Studio-projecten inspecteren zonder native projectbytes te herschrijven;
- gewijzigde, gestagete, ontbrekende en nieuwe assets tonen;
- snapshots committen en history uitlezen;
- branches maken, verwijderen en veilig wisselen;
- projectversies veilig materialiseren en herstellen;
- samples, plugins en overige dependencies analyseren;
- lokale bindings en portabilitystatus beheren;
- repository-integriteit controleren;
- lokale, externe-schijf- en UNC-remotes configureren;
- repositories clonen, fetchen en fast-forward pullen.

De GUI lost daarmee een werkelijk gebruikersprobleem op: muziekproducenten hoeven de terminal niet te begrijpen om versiebeheer, dependencycontrole en samenwerking te gebruiken.

De CLI blijft wel een volwaardig productonderdeel voor:

- scripting en automatisering;
- CI en headless controles;
- technische diagnose;
- power users;
- herstel wanneer de GUI niet start.

De GUI vervangt de CLI dus niet. Beide gebruiken dezelfde Application-laag.

---

## 3. Eerst de begrippen scheiden

### 3.1 GUI-framework

Het GUI-framework bepaalt onder andere:

- welke besturingssystemen worden ondersteund;
- XAML versus HTML/CSS;
- rendering en native integratie;
- vensters, dialogs, drag-and-drop en file pickers;
- packaging en deployment;
- toegankelijkheid, high-DPI en toetsenbordbediening;
- beschikbare tooling en testmogelijkheden.

Voorbeelden: Avalonia, WPF, WinUI 3, Uno Platform, .NET MAUI en Photino.Blazor.

### 3.2 MVVM-library

Een MVVM-library helpt met ViewModels, property notifications en commands, maar maakt zelf geen vensters of controls.

Voorbeelden:

- `CommunityToolkit.Mvvm`;
- ReactiveUI;
- Prism.

Voor DAWVC is `CommunityToolkit.Mvvm` de voorkeursoptie: klein, platformonafhankelijk en zonder noodzaak om de volledige applicatie rond een zwaar framework te organiseren.

### 3.3 Component- of themelibrary

Een componentlibrary vult een gekozen GUI-framework aan met extra controls, navigatiepatronen, iconen of styling.

Voorbeelden:

- Avalonia FluentTheme, FluentAvalonia, Material.Avalonia;
- WPF UI, MaterialDesignInXamlToolkit, MahApps.Metro;
- Windows Community Toolkit voor WinUI en Uno.

Het is dus onjuist om bijvoorbeeld “Avalonia versus Material Design” te vergelijken. Avalonia is het framework; Material.Avalonia is een mogelijke visuele laag erbovenop.

---

## 4. Besliscriteria voor DAWVC

Een framework moet worden beoordeeld op de werkelijke eigenschappen van DAWVC, niet alleen op een algemene populariteitsvergelijking.

### 4.1 Harde eisen

1. Direct te integreren met de bestaande .NET 10-solution.
2. De bestaande Application-use-cases rechtstreeks kunnen aanroepen.
3. Geen CLI-processen hoeven starten of terminaloutput hoeven parsen.
4. Geschikt voor een desktop-first workflow met toetsenbord, muis, drag-and-drop en file pickers.
5. Asynchrone en langdurige operaties ondersteunen zonder een bevroren interface.
6. Grote lijsten met assets, commits en dependencies efficiënt kunnen tonen.
7. Dark mode, high-DPI en toegankelijke focus-/toetsenbordnavigatie ondersteunen.
8. Self-contained distributie mogelijk maken.
9. De byte-integriteit en safety guarantees van DAWVC niet omzeilen.

### 4.2 Strategische wensen

1. Een mogelijke toekomstige macOS-client voor Logic Pro niet blokkeren.
2. Linux niet onnodig uitsluiten.
3. Een visueel herkenbare, DAW-achtige stijl mogelijk maken.
4. Het framework moet door één ontwikkelaar beheersbaar blijven.
5. Licentiekosten of proprietary controls mogen geen verborgen architectuurafhankelijkheid worden.
6. ViewModels moeten grotendeels zonder echte UI kunnen worden getest.

### 4.3 Geen primaire eisen

- Een mobiele applicatie is geen doel voor de eerste GUI.
- Een browserversie is geen MVP-doel.
- De GUI hoeft geen audio-editor of DAW te worden.
- De GUI hoeft geen native `.flp`, `.als` of `.logicx`-inhoud te bewerken.
- De GUI hoeft in de eerste versie geen volledige Git-achtige merge-interface te bieden.

---

## 5. Kandidaten voor het GUI-framework

### 5.1 Avalonia UI

#### Profiel

Avalonia is een open-source XAML-framework voor .NET met ondersteuning voor Windows, macOS en Linux. Desktopprojecten kunnen op moderne .NET-versies draaien; de officiële documentatie noemt .NET 8 als minimum voor desktop en beveelt voor de actuele tooling .NET 10 aan.

#### Sterke punten voor DAWVC

- Eén C#/XAML-codebase voor Windows, macOS en Linux.
- Past rechtstreeks op de huidige .NET 10-architectuur.
- MVVM, dependency injection en testbare ViewModels passen natuurlijk.
- Eigen rendering geeft consistente vormgeving tussen platformen.
- Geschikt voor een custom donkere DAWVC-interface.
- Windows kan het eerste releaseplatform zijn zonder de architectuur definitief Windows-only te maken.
- Avalonia zelf is MIT-gelicentieerd en mag kosteloos voor commerciële en open-source applicaties worden gebruikt.

#### Nadelen en risico's

- Minder groot ecosysteem dan WPF.
- Niet iedere WPF-library is compatibel.
- Platformintegratie kan meer expliciet werk vragen dan bij een puur Windows-framework.
- Avalonia 12 heeft professionele tooling en Pro-controls. Het framework zelf blijft MIT, maar bepaalde nieuwe controls en tools kunnen een Community/Plus/Pro-licentie vereisen.
- De nieuwe `TreeDataGrid` is bijvoorbeeld een Pro-control. DAWVC moet niet ongemerkt een betaalde control als kernvereiste introduceren.
- Communitylibraries moeten op compatibiliteit met de gekozen Avalonia-majorversie worden gecontroleerd.

#### Componentmogelijkheden

- **Officiële FluentTheme:** aanbevolen veilige basis; modern en ingebouwd.
- **Officiële SimpleTheme:** lichtgewicht, maar visueel te kaal als eindstijl.
- **FluentAvalonia:** extra WinUI-achtige controls; eerst actuele Avalonia 12-compatibiliteit en onderhoudsstatus controleren.
- **Material.Avalonia / Semi.Avalonia:** mogelijke alternatieve look, maar minder passend bij een desktoptool voor producers.
- **Eigen DAWVC control themes:** aanbevolen voor kleuren, spacing, statussen en navigatie bovenop FluentTheme.

#### Oordeel voor DAWVC

**Beste strategische fit** wanneer cross-platform desktop een serieuze langetermijnwens is.

---

### 5.2 Windows Presentation Foundation (WPF)

#### Profiel

WPF is Microsofts volwassen XAML-framework voor Windows-desktopapplicaties op .NET. Hoewel .NET cross-platform is, draait WPF uitsluitend op Windows.

#### Sterke punten voor DAWVC

- Zeer volwassen framework en tooling.
- Groot ecosysteem aan controls, themes, voorbeelden en oplossingen.
- Uitstekend geschikt voor data-intensieve desktoptools.
- Goede data binding, templates, virtualization en custom controls.
- Windows file dialogs, drag-and-drop en shellintegratie zijn goed bereikbaar.
- Lage architectuurcomplexiteit voor een Windows-only eerste product.

#### Nadelen en risico's

- Permanent Windows-only.
- Een toekomstige Logic Pro/macOS-client vereist een tweede GUI of migratie.
- De standaardlook is zonder themelibrary gedateerd.
- Sommige populaire UI-libraries verschillen sterk in ontwerpfilosofie en onderhoudstempo.

#### Componentmogelijkheden

- **WPF UI:** moderne Fluent/Windows 11-stijl, navigatie en controls; MIT.
- **MaterialDesignInXamlToolkit:** omvangrijke Material Design-theme- en controllibrary; MIT.
- **MahApps.Metro:** volwassen desktopcontrols en window styling.
- **HandyControl:** brede set aanvullende controls; licentie en onderhoud vooraf controleren.

#### Oordeel voor DAWVC

**Beste pragmatische keuze** wanneer Windows aantoonbaar het enige relevante desktopplatform voor de komende jaren is.

---

### 5.3 WinUI 3 / Windows App SDK

#### Profiel

WinUI 3 is Microsofts aanbevolen native UI-framework voor nieuwe Windows-desktopapplicaties. Het wordt geleverd via de Windows App SDK en biedt moderne Fluent-controls en diepe Windows-integratie.

#### Sterke punten voor DAWVC

- Moderne Windows 10/11-uitstraling zonder grote externe themelibrary.
- Goede aansluiting op Fluent Design en actuele Windows-API's.
- High-DPI, moderne input en Windows App SDK-integratie.
- C# en XAML sluiten aan op de bestaande solution.

#### Nadelen en risico's

- Windows-only.
- Packaging, app lifecycle en tooling zijn complexer dan bij WPF.
- Minder lang volwassen dan WPF.
- Voor sommige klassieke desktopcontrols of workflows blijft extra werk of een toolkit nodig.
- Levert weinig strategisch voordeel als DAWVC later op macOS moet draaien.

#### Componentmogelijkheden

- Ingebouwde WinUI-controls en Fluent styles.
- Windows Community Toolkit voor aanvullende controls en helpers.
- CommunityToolkit.Mvvm voor ViewModels en commands.

#### Oordeel voor DAWVC

Sterk als een zeer moderne, uitsluitend Windows-native ervaring belangrijker is dan portability. Voor DAWVC minder aantrekkelijk dan WPF als pragmatische Windows-keuze en minder aantrekkelijk dan Avalonia als strategische keuze.

---

### 5.4 Uno Platform

#### Profiel

Uno Platform gebruikt een WinUI-achtig programmeermodel en ondersteunt onder andere Windows, macOS, Linux, WebAssembly, Android en iOS. De actuele desktoprichting gebruikt moderne .NET-targets, waaronder .NET 10.

#### Sterke punten voor DAWVC

- Breedste platformbereik van de XAML-kandidaten.
- WinUI-achtige API en styling.
- Eén codebase voor desktop en eventueel web/mobile.
- Windows Community Toolkit kan deels worden gebruikt.
- Cross-platform packaging krijgt expliciete aandacht binnen het ecosysteem.

#### Nadelen en risico's

- Breder en complexer dan DAWVC voor een desktop-only MVP nodig heeft.
- Meer platform- en rendererconcepten om te begrijpen.
- Kleinere community dan WPF en meer bewegende delen.
- Het extra bereik naar web/mobile levert voor de eerste DAWVC-GUI weinig directe waarde.

#### Oordeel voor DAWVC

Een serieuze cross-platform kandidaat, maar waarschijnlijk meer framework dan DAWVC nodig heeft. Alleen als WinUI-compatibiliteit en zeer breed platformbereik zwaar wegen, moet Uno de finale halen.

---

### 5.5 .NET MAUI

#### Profiel

.NET MAUI richt zich op native mobiele en desktopapplicaties. De officieel ondersteunde platformen zijn Android, iOS, Mac Catalyst en Windows. Linux behoort niet tot de officieel ondersteunde standaardtargets; de GTK4-backend is experimenteel.

#### Sterke punten voor DAWVC

- Microsoft-ecosysteem en .NET-integratie.
- Gedeelde code voor Windows en macOS, plus mobiel indien dat later relevant wordt.
- CommunityToolkit.Mvvm past goed.
- Native platformcontrols.

#### Nadelen en risico's

- Mobile-first compromissen zijn niet ideaal voor een dense desktoptool.
- Geen officieel ondersteunde Linux-desktopclient.
- Mac Catalyst is niet hetzelfde als een volledig AppKit-native desktopmodel.
- DAWVC heeft geen duidelijke mobiele use-case die de extra complexiteit rechtvaardigt.
- Grote desktopdatagrids, complexe multi-pane layouts en desktopinteracties zijn niet het sterkste profiel van MAUI.

#### Oordeel voor DAWVC

Niet aanbevolen voor de eerste DAWVC-desktopclient. Alleen heroverwegen wanneer mobiel een werkelijk productdoel wordt.

---

### 5.6 Photino.NET / Photino.Blazor

#### Profiel

Photino host een HTML/CSS/JavaScript- of Blazor-interface in de native webview van Windows, macOS of Linux. In tegenstelling tot Electron wordt geen volledige Chromium-runtime meegeleverd.

#### Sterke punten voor DAWVC

- Cross-platform desktop.
- Bestaande webkennis kan direct worden ingezet.
- Met Blazor kan de backend in .NET blijven.
- Zeer flexibele styling en snelle UI-iteratie.
- Kleinere distributie dan Electron doordat de systeemwebview wordt gebruikt.

#### Nadelen en risico's

- Webviewgedrag en rendering kunnen per platform verschillen.
- Native desktopintegratie en accessibility vereisen extra aandacht.
- Er ontstaat een duidelijke UI/backend-grens, ook wanneer beide in hetzelfde proces draaien.
- Grote tabellen, drag-and-drop en toetsenbordgedrag moeten als webinterface zorgvuldig worden ontworpen.
- Het ecosysteem en de hoeveelheid desktopcontrols zijn kleiner dan bij Electron of volwassen XAML-frameworks.

#### Oordeel voor DAWVC

Een interessante spike voor snelle ontwikkeling met webvaardigheden. Minder aantrekkelijk wanneer een diep geïntegreerde, robuuste desktoptool belangrijker is dan maximale stylingvrijheid.

---

### 5.7 Electron/React of een afzonderlijke webfrontend

#### Profiel

Een React-, Angular- of Vue-interface kan in Electron worden verpakt en via IPC met een .NET-service communiceren.

#### Sterke punten voor DAWVC

- Zeer groot frontendecosysteem.
- Maximale visuele vrijheid.
- Veel componentlibraries en ontwikkeltools.
- Sluit aan op bestaande web/frontendkennis.

#### Nadelen en risico's

- Extra Node/TypeScript-toolchain naast de .NET-solution.
- IPC-contracten, procesbeheer en foutafhandeling worden nieuwe architectuuronderdelen.
- Chromium en Node vergroten distributie en resourcegebruik.
- Application-use-cases kunnen niet zo direct worden aangeroepen als vanuit een .NET-GUI.
- Meer onderhoud voor één ontwikkelaar.

#### Oordeel voor DAWVC

Niet aanbevolen zolang een .NET-native of lichte webview-oplossing aan de eisen voldoet.

---

## 6. Vergelijkingsmatrix

Scores zijn voorlopig en bedoeld om de discussie te structureren: `1 = zwak`, `5 = sterk`. Ze moeten door een technische spike worden gevalideerd.

| Criterium | Gewicht | Avalonia | WPF | WinUI 3 | Uno | MAUI | Photino |
|---|---:|---:|---:|---:|---:|---:|---:|
| Integratie met huidige .NET-code | 20% | 5 | 5 | 5 | 5 | 5 | 4 |
| Desktopgeschiktheid | 15% | 5 | 5 | 4 | 4 | 3 | 4 |
| Windows/macOS/Linux-strategie | 20% | 5 | 1 | 1 | 5 | 3 | 5 |
| Windows-native integratie | 10% | 4 | 5 | 5 | 4 | 4 | 3 |
| Volwassenheid en ecosysteem | 15% | 4 | 5 | 4 | 3 | 4 | 3 |
| Custom styling | 10% | 5 | 4 | 4 | 4 | 3 | 5 |
| Eenvoud van distributie | 5% | 4 | 4 | 3 | 3 | 3 | 4 |
| Licentiecontrole | 5% | 4* | 5 | 5 | 4 | 5 | 4 |
| **Voorlopige totaalscore** | **100%** | **4,7** | **4,1** | **3,7** | **4,3** | **3,8** | **4,0** |

\* Avalonia zelf is MIT. De lagere score komt door de aparte licentiepositie van bepaalde actuele developer tooling en Pro-controls, niet door het framework zelf.

### Interpretatie

- **Avalonia:** beste totaalmatch voor de productstrategie.
- **WPF:** beste keuze voor maximale pragmatiek op uitsluitend Windows.
- **Uno:** sterke cross-platform nummer twee, maar complexer dan nodig.
- **Photino:** nuttig alternatief wanneer webdevelopment de doorslag geeft.
- **WinUI 3:** modern en native, maar Windows-only zonder het volwassenheidsvoordeel van WPF.
- **MAUI:** platformdoelen en UI-profiel sluiten minder goed aan op deze desktoptool.

---

## 7. Aanbevolen componentstack per finalist

### 7.1 Als Avalonia wordt gekozen

```text
Avalonia UI
CommunityToolkit.Mvvm
Microsoft.Extensions.DependencyInjection
Officiële Avalonia FluentTheme
Eigen DAWVC design tokens
Eigen status-, commit- en dependencycontrols
```

Begin zonder commerciële controls. Gebruik voor de eerste versie:

- gewone `DataGrid` voor assets en dependencies;
- `TreeView` voor een eenvoudige dependencyhiërarchie;
- `ListBox`/`ItemsControl` met virtualization voor history;
- officiële dialogs en storage provider API's;
- eigen badges voor `Verified`, `Missing`, `Modified`, `Staged` en `Incomplete`.

Voeg pas later FluentAvalonia of Pro-controls toe wanneer een concreet ontbrekend control de onderhouds- of licentiekosten rechtvaardigt.

### 7.2 Als WPF wordt gekozen

```text
WPF op .NET 10
CommunityToolkit.Mvvm
Microsoft.Extensions.DependencyInjection
WPF UI of eigen Fluent resource dictionaries
```

Kies één hoofdthemelibrary. Combineer niet vanaf het begin WPF UI, MahApps en MaterialDesignInXamlToolkit; dat vergroot de kans op conflicterende styles, resources en window chrome.

### 7.3 Als Photino.Blazor wordt gekozen

```text
Photino.Blazor
ASP.NET Core Razor Components
CSS design tokens
Beperkte, zorgvuldig gekozen webcomponentlibrary
Directe in-process .NET application services
```

Kies dan bij voorkeur geen volledige zware JavaScript-SPA bovenop Blazor. Het voordeel van Photino.Blazor is juist dat de bestaande .NET-laag direct beschikbaar blijft.

---

## 8. Doelarchitectuur

De GUI mag niet rechtstreeks afhankelijk worden van CLI-code.

```text
┌──────────────────────┐      ┌──────────────────────┐
│     DawVcs.Cli       │      │   DawVcs.Desktop     │
│ commands/presenters  │      │ views/viewmodels     │
└──────────┬───────────┘      └──────────┬───────────┘
           │                             │
           └──────────────┬──────────────┘
                          ▼
               ┌──────────────────────┐
               │   DawVcs.Bootstrap   │
               │ services.AddDawVcs() │
               └──────────┬───────────┘
                          ▼
               ┌──────────────────────┐
               │ DawVcs.Application   │
               │ use-cases & ports    │
               └─────┬─────────┬──────┘
                     ▼         ▼
                  Domain   Infrastructure
                                │
                                ▼
                         DAW Adapters
```

### 8.1 Nieuwe projecten

```text
src/
├── DawVcs.Bootstrap/
└── DawVcs.Desktop/

tests/
├── DawVcs.Bootstrap.Tests/
└── DawVcs.Desktop.Tests/
```

**Implementatiestand 24 oktober 2026 (WP1):** `DawVcs.Bootstrap` en `DawVcs.Bootstrap.Tests` zijn toegevoegd. De CLI gebruikt nu `services.AddDawVcs()` voor dezelfde registraties en lifetimes als voorheen. `DawVcs.Desktop` en de desktoptests hierboven blijven gepland; de frameworkkeuze blijft een voorstel. De toekomstige Desktop-host gebruikt dezelfde Bootstrap-registraties.

Ownership, disposal en metadataneveneffecten zijn vastgelegd in [Technical Design, sectie 6.1](Technical%20Design.md#61-implemented-composition-root-and-ownership-24-october-2026). De singleton FL Studio-adapter heeft nog geen cache-invalidatie; dit is een expliciet aandachtspunt voor WP4-05 voordat desktoprescans als actueel kunnen worden beschouwd. De exacte voortgang en validatie staan in de [GUI-takenlijst](GUI_Implementation_Tasks.md).

### 8.2 Belangrijke regel

```text
Desktop → Application-use-case
```

Niet:

```text
Desktop → dawvc.exe → stdout parser
```

Een subprocess-interface zou type-informatie verliezen, foutafhandeling fragiel maken en de CLI-output onbedoeld tot een publieke API promoveren.

---

## 9. Benodigde Application-uitbreidingen

De bestaande use-cases zijn herbruikbaar, maar een GUI heeft andere interactionele behoeften dan een CLI.

### 9.1 Voortgang

Langdurige operaties moeten optioneel voortgang publiceren. **Implementatiestand 24 oktober 2026 (WP2-02/03):** Application bevat `OperationProgress` met getypeerde `OperationStage`, `long Completed`, `long? Total` en `OperationProgressUnit` (`Items`/`Bytes`). Scan/status, commit, checkout/restore en fast-forward, doctor/fsck en fetch/clone/pull ontsluiten per aanroep `IProgress<OperationProgress>?` via aanvullende overloads; bestaande CLI- en `IUseCase`-aanroepen blijven ongewijzigd.

```csharp
var observation = new OperationProgress(
    OperationStage.HashingFiles, completed: 3, total: 10);
await statusUseCase.ExecuteAsync(request, cancellationToken, progress);
```

Dit vervangt het oorspronkelijke schetscontract met vrije fase-/itemtekst: getypeerde fasen zijn vertaalbaar zonder backendteksten te parsen, `long` ondersteunt grote tellers en het ontbreken van `CurrentItem` voorkomt onbedoelde padverspreiding. Een teller geldt voor één fase; `null` totaal betekent onbekend en `0` een bekende lege fase. De Domain-laag kent dit contract niet.

WP2-03 is aangesloten en getest. Fasen gebruiken bekende totalen voor afgehandelde bestanden/objecten/controles en onbekende totalen voor groeiende graphs of adapterwerk. Samengestelde operaties filteren child-`Completed`; een afgeronde fetch is niet hetzelfde als een afgeronde pull. Observercallbacks veranderen de bestaande journal-/ref-/indexpublicatiegrenzen niet. UI-threadmarshalling, throttling en sessiefiltering blijven werk van de toekomstige operatiecoördinator, niet van het voortgangsrecord. Zie [operatie-inventarisatie en contract](GUI_Operation_Inventory.md) en [Technical Design, sectie 6.2](Technical%20Design.md#62-implemented-operation-progress-contract-24-october-2026).

### 9.2 Annulering

Iedere langdurige opdracht moet een `CancellationToken` respecteren, met speciale aandacht voor:

- scan;
- commit van grote samplecollecties;
- checkout/materialisatie;
- clone;
- fetch;
- pull;
- `doctor` en `fsck`.

**Auditstand 24 oktober 2026:** Scan/status hebben entry-/loop-/afrondingschecks gekregen. Doctor/fsck bewaren cancellation door hun eigen diagnostische catches en controleren entry/afronding; commit en transfers hebben entrychecks. Dependencyzoekwerk, optionele doctorsamenvattingen en publicatie onderdrukken of herclassificeren nog cancellation; rollback heeft expliciete vervolgproblemen. De [operatie-inventarisatie](GUI_Operation_Inventory.md) scheidt deze bestaande beperkingen van bovenstaande doelstelling. WP2-04/05/06 blijven open.

### 9.3 Preflight in plaats van verrassingsdialogs

De GUI moet vóór een mutatie kunnen tonen wat gaat gebeuren:

- welke bestanden worden toegevoegd, vervangen of verwijderd;
- of de workspace dirty is;
- waar een recoverycopy wordt opgeslagen;
- welke branch of ref verandert;
- hoeveel data wordt overgebracht;
- waarom een pull geen fast-forward is.

Gebruik hiervoor expliciete plan/preflight-resultaten. Laat de GUI niet proberen foutmeldingen te ontleden om de handeling te reconstrueren.

### 9.4 Centrale foutvertaling

Application-exceptions moeten naar een beperkt aantal gebruikersgerichte categorieën worden vertaald:

- validation;
- dirty workspace;
- missing dependency;
- integrity failure;
- incompatible repository;
- remote unavailable;
- non-fast-forward;
- permission/I/O failure;
- cancellation;
- incomplete rollback met recoverypad.

De technische exception blijft beschikbaar voor logs en een uitklapbaar detailpaneel.

---

## 10. GUI-MVP

### 10.1 Startscherm

- recente repositories;
- bestaande repository openen;
- nieuwe repository initialiseren;
- repository clonen;
- duidelijke melding als een map geen geldige DAWVC-repository is.

### 10.2 Projectdashboard

- projectnaam en primaire DAW-file;
- huidige branch en HEAD-commit;
- aantal modified, staged, untracked en missing files;
- dependency health en reproduceerbaarheid;
- remote- en pullstatus;
- snelle acties: `Scan`, `Commit`, `Doctor`, `Fetch` en `Open in Explorer`.

### 10.3 Changes en staging

- gegroepeerde wijzigingen;
- bestandstype, rol, grootte en status;
- selectie en staging;
- commitbericht en auteur;
- waarschuwing voor incomplete commits;
- duidelijke bevestiging van het nieuwe commit-ID.

### 10.4 History

- virtualized commitlijst;
- branch- en HEAD-indicatoren;
- commitdetails en parents;
- snapshotinhoud;
- herstel naar afzonderlijke map;
- geen misleidende mergevisualisatie zolang merges niet worden ondersteund.

### 10.5 Dependencies

- samples, plugins en libraries als aparte filters;
- `Verified`, `Missing`, `Unresolved`, `ReferenceOnly` en `RepositoryAsset`;
- vereiste en gevonden pluginversie;
- dependency handmatig binden;
- bron/provenance tonen zonder privacygevoelige absolute paden ongeredacteerd te presenteren.

### 10.6 Branches

- branches tonen en aanmaken;
- veilig wisselen;
- dirty-workspacepreflight;
- force alleen na expliciete bevestiging;
- recoverypad na afloop aanklikbaar tonen.

### 10.7 Remotes

- remotes toevoegen, tonen en verwijderen;
- fetch/pull met dry-run;
- transfergrootte en gewijzigde refs tonen;
- non-fast-forward begrijpelijk uitleggen.

Belangrijk: `v0.2` ondersteunt alleen filesystempaden, externe schijven en UNC-shares. HTTPS en push worden nog niet ondersteund. Gebruik daarom nog geen algemene knop **Sync** die tweerichtingssynchronisatie suggereert.

### 10.8 Diagnostics

- `doctor` met score en gegroepeerde problemen;
- `fsck` met voortgang;
- oplossing of volgende stap per probleem;
- technische details optioneel uitklapbaar;
- rapport exporteren zonder lokale gebruikerspaden te lekken.

---

## 11. UX- en visuele richting

DAWVC moet eruitzien als een professionele productietool, niet als een generieke admin-interface en ook niet als een imitatie van FL Studio.

### 11.1 Richting

- donkere basis met rustig contrast;
- statuskleuren consequent en toegankelijk;
- compacte datadichtheid;
- duidelijke typografische hiërarchie;
- vaste linker navigatie;
- centraal werkgebied;
- optioneel detailpaneel rechts;
- subtiele animaties, geen overdadige effecten;
- waveforms alleen wanneer ze werkelijk informatie toevoegen.

### 11.2 Design tokens

Definieer kleuren en spacing los van concrete controls:

```text
Color.Background.Base
Color.Background.Elevated
Color.Border.Subtle
Color.Text.Primary
Color.Text.Secondary
Color.Status.Success
Color.Status.Warning
Color.Status.Error
Color.Status.Modified
Color.Status.Staged
Spacing.1 ... Spacing.8
Radius.Small / Medium / Large
```

Hierdoor blijft een overstap van theme- of componentlibrary mogelijk zonder iedere view te herschrijven.

---

## 12. Technische spike vóór de definitieve keuze

Maak drie kleine branches of losse prototypes:

1. `spike/gui-avalonia`
2. `spike/gui-wpf`
3. `spike/gui-photino` of `spike/gui-uno`

Iedere spike implementeert exact dezelfde verticale slice:

```text
Repositorymap selecteren
→ StatusUseCase uitvoeren
→ dashboard tonen
→ 10.000 gesimuleerde assets weergeven
→ scan starten en annuleren
→ doctorresultaat tonen
→ dark/light theme wisselen
```

### Metingen

- cold start;
- geheugengebruik idle en bij 10.000 rijen;
- scroll- en filterresponsiviteit;
- buildtijd;
- self-contained pakketgrootte;
- file picker en drag-and-drop;
- toetsenbordnavigatie;
- high-DPI gedrag;
- testbaarheid van ViewModels;
- benodigde workarounds;
- ontwikkeltijd per slice;
- kwaliteit van tooling en debugging.

### Knock-outcriteria

Een framework valt af wanneer:

- grote lijsten niet soepel virtualiseren;
- de UI-thread gemakkelijk blokkeert tijdens bestaande use-cases;
- packaging niet betrouwbaar reproduceerbaar is;
- fundamentele desktopinteracties custom native code vereisen;
- een cruciale control alleen onder onacceptabele licentievoorwaarden beschikbaar is;
- de ViewModels niet goed los van het venster kunnen worden getest.

---

## 13. Gefaseerde implementatie

### Fase 0 — architectuur voorbereiden

- `DawVcs.Bootstrap` introduceren;
- DI-registratie uit CLI-`Program.cs` halen;
- progressmodel ontwerpen;
- foutcategorieën en GUI error mapping ontwerpen;
- repositorysession en lifecycle vastleggen;
- frameworkspikes uitvoeren;
- frameworkkeuze vastleggen in een ADR.

### Fase 1 — read-only desktopclient

- startscherm en repository openen;
- dashboard;
- status;
- history;
- scan;
- doctor;
- fsck;
- recente repositories.

Deze fase wijzigt geen projectinhoud, staging, branches of refs en vormt daarmee een veilige eerste release. Zij is niet strikt schrijfloos: de bestaande status-use-case kan de lokale hashcache bijwerken. Controleer en documenteer ook de metadata-neveneffecten van de overige leesoperaties.

### Fase 2 — lokale mutaties

- add/stage;
- commit;
- branch create/delete/switch;
- checkout/restore;
- dependency binding;
- preflight en recoverydialogen.

### Fase 3 — samenwerking

- clone;
- remotebeheer;
- fetch;
- pull en dry-run;
- progress en transferinformatie;
- later push en HTTPS zodra de backend die ondersteunt.

### Fase 4 — productisering

- platformpackaging;
- signing;
- automatische updatestrategie;
- crashlogs met pathredactie;
- toegankelijkheidsaudit;
- performance- en grote-projecttests;
- gebruikersdocumentatie en screenshots.

---

## 14. Teststrategie

### ViewModel-tests

- commands en enabled states;
- loading, success, empty en error states;
- annulering;
- navigation state;
- mapping van use-case-resultaten;
- bevestigingsflows via abstracte dialogservices.

### UI-smoketests

- applicatie start;
- repository openen;
- hoofdviews navigeren;
- status laden;
- dialog openen en annuleren;
- theme laden;
- focusvolgorde en basale toetsenbordbediening.

### Integratietests

- GUI-composition root kan alle use-cases oplossen;
- dezelfde fixtures en tijdelijke repositories als CLI/E2E-tests gebruiken;
- muterende acties behouden bestaande safety guarantees;
- GUI-resultaat en CLI-resultaat zijn semantisch gelijk voor dezelfde use-case.

### Niet doen

- businesslogica alleen via pixel- of screenshottests testen;
- echte file dialogs in iedere ViewModel-test openen;
- CLI-output gebruiken als GUI-databron;
- testrepositories in gebruikersmappen aanmaken.

---

## 15. Voorlopige aanbeveling

De voorlopig aanbevolen stack is:

```text
Framework:             Avalonia UI
Architecture:          MVVM
MVVM library:          CommunityToolkit.Mvvm
Dependency injection:  Microsoft.Extensions.DependencyInjection
Base theme:            officiële Avalonia FluentTheme
Advanced controls:     pas toevoegen na concrete behoefte en licentiecheck
Initial platform:      Windows 11 x64
Future platforms:      macOS en Linux zonder architectuurrewrite
```

Redenen:

1. DAWVC gebruikt al .NET 10 en een schone Application-laag.
2. De toekomstige DAW-roadmap omvat niet alleen Windows-DAWs.
3. De GUI kan eerst uitsluitend voor Windows worden uitgebracht zonder WPF als permanente platformgrens te introduceren.
4. Een custom donkere desktopinterface is goed haalbaar.
5. CLI en Desktop kunnen dezelfde typed use-cases gebruiken.

De aanbeveling wordt definitief wanneer de Avalonia-spike aantoont dat:

- grote assetlijsten soepel blijven;
- de benodigde controls zonder ongewenste Pro-afhankelijkheid kunnen worden gebouwd;
- file dialogs, drag-and-drop en packaging betrouwbaar werken;
- ontwikkelsnelheid acceptabel is;
- ViewModels volledig testbaar blijven.

Als die spike tegenvalt en macOS/Linux geen concrete roadmapverplichting blijken, wordt **WPF + WPF UI + CommunityToolkit.Mvvm** de pragmatische fallback.

---

## 16. Besluittemplate voor de ADR

```markdown
# ADR: Desktop UI framework for DAWVC

## Status
Accepted / Rejected / Superseded

## Context
Welke product- en technische eisen bepalen de keuze?

## Decision
Welk framework, welke MVVM-library en welke basistheme worden gebruikt?

## Evidence
- spike-resultaten;
- performancecijfers;
- packagegrootte;
- platformtests;
- licensing review;
- developer experience.

## Consequences
- ondersteunde platformen;
- nieuwe dependencies;
- packaging;
- onderhoud;
- beperkingen.

## Rejected alternatives
Waarom zijn de andere kandidaten afgevallen?
```

---

## 17. Bronnen

### DAWVC

- [DAWVC repository](https://github.com/Nithiann/DAWVC)
- [DAWVC v0.2.0-preview.1](https://github.com/Nithiann/DAWVC/releases/tag/v0.2.0-preview.1)
- [Huidige CLI composition root](https://github.com/Nithiann/DAWVC/blob/8795fa6/src/DawVcs.Cli/Program.cs)
- [DAWVC Application-laag](https://github.com/Nithiann/DAWVC/tree/8795fa6/src/DawVcs.Application)

### Avalonia

- [Avalonia documentation](https://docs.avaloniaui.net/docs/welcome)
- [Supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [Dependency injection with Avalonia](https://docs.avaloniaui.net/docs/app-development/dependency-injection)
- [Avalonia themes](https://docs.avaloniaui.net/docs/styling/themes)
- [Avalonia licensing FAQ](https://docs.avaloniaui.net/tools/faq)
- [TreeDataGrid licensing and availability](https://docs.avaloniaui.net/controls/data-display/structured-data/treedatagrid/)
- [FluentAvalonia repository](https://github.com/amwx/FluentAvalonia)

### Microsoft desktopframeworks

- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview)
- [WinUI 3 overview](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/)
- [.NET MAUI supported platforms](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms?view=net-maui-10.0)
- [Experimental MAUI platform backends](https://learn.microsoft.com/en-us/dotnet/maui/developer-tools/platform-backends/?view=net-maui-10.0)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [Windows Community Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/)

### Overige kandidaten

- [Uno Platform supported platforms](https://platform.uno/docs/articles/getting-started/requirements.html)
- [Uno Platform .NET version support](https://platform.uno/docs/articles/net-version-support.html)
- [Photino.NET documentation](https://docs.tryphotino.io/Photino-.NET)
- [Photino.Blazor documentation](https://docs.tryphotino.io/Photino-Blazor)
- [WPF UI repository](https://github.com/lepoco/wpfui)
- [MaterialDesignInXamlToolkit repository](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)

---

## 18. Directe volgende stap

Voer eerst **Fase 0** uit volgens de [GUI-takenlijst](GUI_Implementation_Tasks.md): begin met de gedeelde Bootstrap-extractie (WP1), gevolgd door de operatiecontracten (WP2) en geïsoleerde frameworkspikes (WP3). De Avalonia-spike bouwt de verticale slice uit sectie 12, zonder wijzigingen aan projectinhoud; metadatawrites worden gecontroleerd en vastgelegd. Neem pas na vergelijking met de andere kandidaten een definitieve frameworkbeslissing. Zo blijft de keuze bewijsbaar en wordt de bestaande DAWVC-engine niet voortijdig aan één presentatieframework gekoppeld. Houd bij iedere oplevering de taakstatus, validatie en relevante documentatie bij.
