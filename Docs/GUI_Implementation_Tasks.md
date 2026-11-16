<!--  --># DAWVC GUI — Takenlijst en voortgang

**Laatst bijgewerkt:** 16 november 2026
**Status:** Avalonia expliciet gekozen voor Windows, macOS en Linux; WP3-selectie afgesloten met scopewijziging. WP4 afgerond; WP5 is de volgende uitvoering.

**Ontwerpbron:** [GUI Framework Selection & Implementation Plan](DAWVC_GUI_Framework_Selection_and_Implementation_Plan.md)

Dit document is de centrale voortgangsregistratie voor de desktop-GUI. Het oorspronkelijke GUI-document beschrijft de richting en afwegingen; deze takenlijst beschrijft de uitvoering. Het bestaande [MVP Implementation Plan](Implementation%20plan.md) blijft de historische CLI-MVP-baseline en wordt niet met terugwerkende kracht herschreven.

## 1. Werkwijze en definitie van voltooid

- `[ ]` betekent open. Een gestarte of geblokkeerde taak blijft open; noteer die toestand met taak-ID bij de actuele voortgang.
- `[x]` betekent aantoonbaar voltooid, inclusief relevante tests en documentatie.
- Werk bij iedere GUI-wijziging de betreffende taak, werkpakketstatus, validatie en documentatie in dezelfde wijziging bij.
- Een werkpakket is pas voltooid als alle taken en acceptatiecriteria zijn afgehandeld. Een geslaagde implementatie zonder vereiste validatie is nog niet voltooid.
- Noteer bij afronding de taak-ID's, gewijzigde bestanden, werkelijk uitgevoerde controles, resultaten en eventuele beperkingen in het voortgangslogboek.
- Als validatie niet kan worden uitgevoerd, vermeld waarom en wat nog nodig is. Meld geen tests als geslaagd zonder ze te hebben uitgevoerd.
- Wijzigingen aan scope, volgorde of architectuur worden hier toegelicht en zo nodig in het ontwerpdocument of een ADR verwerkt.
- Frameworkkeuze en toekomstige mogelijkheden blijven als voorstel gemarkeerd totdat het besluit respectievelijk de implementatie is afgerond.

## 2. Actuele voortgang

**Eerstvolgende taak:** WP5-01 — viewer-startscherm en repository-opening verder uitwerken op de geleverde shell; daarna dashboard/changes/history. WP5 is nog niet gestart.

**Lopende taken:** Geen binnen WP4. Native platform-/releasechecks blijven geregistreerd in WP8.

**Blokkades:** GitHub API blijft `Forbidden` geven voor Actions en PR-aanmaak; branchpush werkt. De gebruiker heeft de frameworkkeuze expliciet gemaakt en WP4 toegestaan. Native Windows/macOS-QA en platformbudgetten blijven open in WP8, niet als geslaagd aangemerkt. Linux-native validatie is nu mogelijk met een tijdelijk lokaal uitgepakte Xvfb.

**Aandachtspunten:** Rollback probeert alle herstelacties; falen wordt typed gemeld en beschikbare journalback-ups blijven bewaard. Fouten kunnen na duurzame publicatie optreden. SQLite-corruptieherstel kan staged entries wissen: dit moet vóór vieweracceptatie worden aangepakt (WP5-08). Fetch/pull-dry-runs schrijven tijdelijke metadata en kunnen stale sessies verwijderen. Adaptercache-invalidatie blijft WP4-05. Zie de inventarisatie voor bronnen en vervolggrenzen.

| Werkpakket | Status | Afhankelijkheden | Oplevering |
| --- | --- | --- | --- |
| WP0 — Planning en documentatie | Voltooid | Geen | Takenlijst, verwijzingen en onderhoudsafspraken |
| WP1 — Gedeelde composition root | Voltooid | WP0 | Bootstrap en ongewijzigd CLI-gedrag |
| WP2 — Operatiecontracten | Voltooid | WP1 | Voortgang, cancellation en typed fouten |
| WP3 — Frameworkspikes en ADR | Afgesloten op gebruikersbesluit | WP1; WP2 voor progress/cancellation | Onderbouwde frameworkkeuze |
| WP4 — Desktop-shell en session | Voltooid | WP2, WP3 | Responsieve shell en veilige lifecycle |
| WP5 — Viewer-release | Open | WP4 | Geen mutaties aan projectinhoud, staging, branches of refs |
| WP6 — Lokale mutaties | Open | WP5 | Preflight, bevestiging en recovery |
| WP7 — Clone en remotes | Open | WP6 | Backendconforme samenwerking |
| WP8 — Productisering voor Windows, macOS en Linux | Open | Start bij WP3/WP4; afronding na WP7 | Gevalideerde Windows-release |

Werkpakketten zijn opleverbaar in kleine wijzigingen. Packaging, CI, privacy en toegankelijkheid beginnen vroeg; WP8 is geen reden om die uit te stellen.

## 3. Taken per werkpakket

### WP0 — Planning en documentatie

- [x] WP0-01 — Implementatie opdelen in werkpakketten met stabiele taak-ID's en acceptatiecriteria.
- [x] WP0-02 — Centrale takenlijst koppelen aan het GUI-ontwerpdocument en het bestaande MVP-plan.
- [x] WP0-03 — Documentatieonderhoud en bewijs van afronding vastleggen in de contributorafspraken.

**Acceptatie:** De uitvoering heeft één centrale statusregistratie; geplande functionaliteit wordt niet als bestaande functionaliteit gepresenteerd.

### WP1 — Gedeelde composition root

- [x] WP1-01 — `src/DawVcs.Bootstrap` en `tests/DawVcs.Bootstrap.Tests` toevoegen aan `DawVcs.slnx`.
- [x] WP1-02 — `AddDawVcs()` introduceren en de bestaande DI-registraties uit CLI-`Program.cs` verplaatsen, zonder commandpresentatie of exitcodes te wijzigen.
- [x] WP1-03 — Ownership, disposal en lifetimes van repositorycontexts, adapters en transports controleren en vastleggen; gerichte tests toevoegen.
- [x] WP1-04 — Registratietests toevoegen die alle gedeelde use-cases oplossen.
- [x] WP1-05 — CLI-build en relevante regressie-/E2E-tests uitvoeren; architectuurdocumentatie bijwerken.

**Acceptatie:** De CLI gebruikt de gedeelde registraties en behoudt zijn gedrag. Application en Domain krijgen geen desktopafhankelijkheden.

### WP2 — GUI-geschikte operatiecontracten

- [x] WP2-01 — Bestaande cancellation, synchronische werkpaden, fouttypen en dry-run-neveneffecten inventariseren per langdurige use-case; vastgelegd in `GUI_Operation_Inventory.md`.
- [x] WP2-02 — `OperationProgress` in Application introduceren; optioneel per aanroep ontsluiten met behoud van bestaande CLI-call-sites. Contract en aanvullende overloads geleverd voor scan/status; overige integratie valt onder WP2-03.
- [x] WP2-03 — Voortgang aansluiten op scan/status, commit, checkout/restore, diagnostics en transfers waar zinvol; onbekende totalen ondersteunen. Geleverd voor scan/status, commit, checkout/restore en fast-forward, doctor/fsck en fetch/clone/pull; nested completion en legacy-overloads getest.
- [x] WP2-04 — Cancellation tijdens enumeratie, hashing, adapterwerk en I/O controleren en ontbrekende checks toevoegen. Stream-disposal bij gefaalde/geannuleerde header-read gerealiseerd in LooseObjectStore. Cancellationbehoud in PublicationJournal (rollback met uncancelled token en directe rethrow), Clone/Pull optionele doctor reporting en Dependency Discovery/Resolver. Entry- en loop-checks toegevoegd aan alle use cases (Checkout, Commit, Add, Log, Bind, Switch, Init, Fsck, Doctor, Remotes), SQLite-stagingindex (Dapper CommandDefinition) en QuarantineObjectStore.
- [x] WP2-05 — Typed fouten aanvullen en centrale gebruikerscategorieën definiëren zonder exceptiontekst te parsen; timeout, annulering en incomplete rollback onderscheiden.
- [x] WP2-06 — Veilige annuleringsgrenzen en rollbackbeleid vastleggen en testen; cleanup mag niet door een reeds geannuleerd token worden overgeslagen.
- [x] WP2-07 — Contract-, cancellation- en regressietests uitvoeren; Application-contracten en beperkingen documenteren.

**Acceptatie:** Bestaande clients blijven werken zonder progressconsument. Annulering is pas afgerond zodra het werk stopt; fouten zijn typed presenteerbaar.

### WP3 — Frameworkspikes en ADR

**Scopewijziging 13 november 2026:** De gebruiker vereist Windows, macOS en Linux,
kiest expliciet Avalonia en geeft opdracht met WP4 verder te gaan. De oorspronkelijke
vergelijkingsgate is hiermee vervangen. Eerder ontbrekende metingen blijven ontbreken;
native kwaliteits-/releasevalidatie voor Avalonia gaat mee naar WP8 voor alle drie OS'en.

- [x] WP3-01 — Gelijke fixtures, meetprocedure en budgetten vooraf vastgelegd; originele protocolwaarden behouden.
- [x] WP3-02 — Avalonia-prototype geleverd met gedeelde backend, virtualisatie, filtering, cancellation, doctor en Windows-crosspublicatie; headless bewijs geregistreerd. Native platform-QA verplaatst naar WP8.
- [x] WP3-03 — WPF-referentiespike behouden; uitgesloten als productkandidaat wegens Windows-only. Native vergelijkingsrun vervalt door gebruikersbesluit.
- [x] WP3-04 — Photino-referentiespike behouden als niet-gekozen alternatief; beperkingen niet als geslaagde checks gepresenteerd.
- [x] WP3-05 — Beschikbaar bewijs, licentiemetadata en open platformchecks geregistreerd; volledige driewegvergelijking vervalt door gebruikersbesluit.
- [x] WP3-06 — Expliciete Avalonia-keuze vastgelegd in Accepted ADR-GUI-001; implementatie- en ontwerpstatus bijgewerkt.

**Acceptatie na scopewijziging:** Avalonia is een expliciet gebruikersbesluit, geen
volledig bewezen vergelijkingswinnaar. Geen commerciële controls. Productie-Desktop
mag worden gebouwd; native kwaliteit en distributie op de drie OS'en blijven releasegates.

### WP4 — Desktop-shell en repositorysession

- [x] WP4-01 — `src/DawVcs.Desktop` en `tests/DawVcs.Desktop.Tests` toevoegen met de gekozen stack; packages centraal beheren.
- [x] WP4-02 — Startup, Bootstrap-integratie, navigatie, thema en design tokens bouwen.
- [x] WP4-03 — Interfaces voor folderpicker, dialogs en openen in de bestandsverkenner implementeren.
- [x] WP4-04 — Recente repositories in gebruikersgebonden opslag implementeren, zonder machinepaden in gedeelde repositorydata op te slaan.
- [x] WP4-05 — Repositorysession met sessie-identiteit, cancellation en disposal implementeren; late resultaten van een vorige session negeren. Adaptercacheverversing of een expliciet lifetimebeleid implementeren en testen, inclusief gewijzigde/ontbrekende samples en gewijzigde zoekpaden.
- [x] WP4-06 — Operatiecoördinator bouwen met busy state, begrensde progressupdates, foutpresentatie en serialisatie van conflicterende acties.
- [x] WP4-07 — UI-threadresponsiviteit borgen, inclusief synchronisch werk vóór de eerste `await`.
- [x] WP4-08 — Architectuurtests toevoegen voor Desktop → Application en het verbod op Desktop → CLI; lifecycle-, commandstate- en navigatietests uitvoeren.

**Acceptatie:** Repositorywissels tonen geen oude resultaten; langdurig werk blokkeert de UI niet. GUI-coördinatie vervangt geen backendbeveiliging tegen gelijktijdig CLI-gebruik.

### WP5 — Viewer-release zonder projectinhoudmutaties

- [ ] WP5-01 — Startscherm met openen, recente repositories en afhandeling van ongeldige repositories opleveren.
- [ ] WP5-02 — Dashboard met branch, HEAD, statusaantallen en afzonderlijke laad-/fouttoestanden bouwen.
- [ ] WP5-03 — Changes-view met groepering, zoeken, selectie en virtualisatie bouwen, zonder stagingacties.
- [ ] WP5-04 — Begrensde/gepagineerde history en commit-/snapshotdetails aanbieden; ontbrekende queries via Application toevoegen.
- [ ] WP5-05 — Scan, doctor en fsck met voortgang, annulering en geredacteerde rapportexport aansluiten.
- [ ] WP5-06 — Beschikbare dependencygegevens tonen en filteren zonder bindacties.
- [ ] WP5-07 — Loading-, empty-, success- en errorstates testen; CLI/GUI-resultaten semantisch vergelijken met gedeelde fixtures.
- [ ] WP5-08 — Cachewrites en overige metadata-neveneffecten controleren en documenteren; voorkom dat SQLite-corruptieherstel staged entries wist bij viewer/statusgebruik. Viewer-smoketest en Windows-previewpublicatie uitvoeren.

**Acceptatie:** Geen wijzigingen aan projectbestanden, staging, branches of refs. De status-hashcache kan wel worden bijgewerkt; de release wordt daarom niet als strikt schrijfloos beschreven. Init, restore en andere muterende acties ontbreken nog.

### WP6 — Lokale mutaties met preflight

- [ ] WP6-01 — Operatiespecifieke preflight ontwerpen met doel, bestandswijzigingen, dirty-workspaceblokkades, dependencies en recoverybeleid.
- [ ] WP6-02 — Uitvoering uitgangspunten opnieuw laten controleren; gewijzigde repositorytoestand leidt tot stoppen of een nieuwe bevestiging.
- [ ] WP6-03 — Repository-initialisatie toevoegen.
- [ ] WP6-04 — Staging en commit toevoegen met auteur, bericht en expliciete incomplete-commitwaarschuwing.
- [ ] WP6-05 — Branch aanmaken en verwijderen toevoegen met passende validatie en bevestiging.
- [ ] WP6-06 — Snapshotrestore naar een afzonderlijke map toevoegen.
- [ ] WP6-07 — Branchwissel en in-place checkout toevoegen met dirty-workspacepreflight, expliciete forcebevestiging en aanklikbare recoverylocatie.
- [ ] WP6-08 — Dependencybinding toevoegen met privacybewuste presentatie van lokale paden.
- [ ] WP6-09 — Annulering, gewijzigde preflightuitgangspunten, fouten, rollback en incomplete recovery integratietesten; recovery- en gebruikersdocumentatie bijwerken.

**Acceptatie:** De GUI hergebruikt de Application-safety guarantees. Geen force zonder bevestiging en geen mutatie op basis van een ongecontroleerd verouderde preview.

### WP7 — Clone en remotes

- [ ] WP7-01 — Remotes tonen, toevoegen en verwijderen.
- [ ] WP7-02 — Clone toevoegen met doelvalidatie, voortgang en annulering.
- [ ] WP7-03 — Bestaande fetch/pull-dry-runs hergebruiken en hun werkelijke neveneffecten documenteren.
- [ ] WP7-04 — Transfers en gewijzigde refs presenteren; non-fast-forward als afzonderlijke toestand uitleggen.
- [ ] WP7-05 — Filesystem-, externe-schijf- en UNC-workflows testen, inclusief onbereikbare remote en onderbroken transfer.
- [ ] WP7-06 — Samenwerkingsdocumentatie en GUI-beperkingen actualiseren; geen push, HTTPS of misleidende Sync-actie aanbieden zolang de backend die niet ondersteunt.

**Acceptatie:** Alleen ondersteunde transportmogelijkheden worden aangeboden; dry-run, uitvoering en foutpresentatie komen overeen met backendgedrag.

### WP8 — Productisering voor Windows, macOS en Linux

- [ ] WP8-01 — Vroeg reproduceerbare self-contained publicaties voor Windows, macOS en Linux inrichten en packageversies/licenties registreren.
- [ ] WP8-02 — Vanaf de desktop-shell CI-build, tests en artifacts toevoegen.
- [ ] WP8-03 — Signing en distributiestrategie vastleggen; automatische updates pas na expliciet beveiligings- en distributiebesluit implementeren.
- [ ] WP8-04 — High-DPI-, toetsenbord-, focus- en toegankelijkheidstests uitvoeren.
- [ ] WP8-05 — Grote-projecttests met echte hashing en grote samples uitvoeren; resultaten vergelijken met de vastgelegde budgetten.
- [ ] WP8-06 — Logs, crashdetails en exports op pad-/privacyredactie controleren.
- [ ] WP8-07 — Installatie, QuickStart, beperkingen, recovery, screenshots en afhankelijkhedenoverzicht bijwerken voor de werkelijk geleverde GUI.
- [ ] WP8-08 — Gepubliceerde app op schone Windows-, macOS- en Linux-omgevingen valideren en releasecriteria met bewijs aftekenen.

**Acceptatie:** De distributie is reproduceerbaar en de ondersteunde Windows-, macOS- en Linux-workflows zijn aantoonbaar getest; documentatie beschrijft geen nog ongeïmplementeerde functies als beschikbaar.

## 4. Welke documentatie wordt wanneer bijgewerkt?

| Wijziging | Bij te werken documentatie |
| --- | --- |
| Iedere GUI-taak | Deze takenlijst: checkbox, werkpakketstatus, actuele voortgang en logboek |
| Frameworkbesluit of scopewijziging | GUI-ontwerpdocument en passende ADR in `Docs/adr` |
| Bootstrap, lifetimes en Application-contracten | `Technical Design.md`; architectuuroverzicht in `../README.md` zodra gerealiseerd |
| Packaging en beschikbaar gemaakte GUI-workflow | `Installation.md`, `QuickStart.md` en `../README.md` |
| Force, preflight, rollback en recovery | `Recovery_Guide.md`; relevante gebruikersinstructies |
| Clone, fetch, pull en transportbeperkingen | `Collaborator_Workflow.md` en `Known_Limitations.md` |
| Privacy, logging en nieuwe packages | `SecurityThreatModelReview.md` en `SBOM.md` waar van toepassing |
| CLI-gedrag dat daadwerkelijk verandert | `CLI_Reference.md`; geen wijziging suggereren bij alleen DI-extractie |

Bestaande historische plannen behouden hun oorspronkelijke releasescope. Voeg verwijzingen of expliciet gedateerde aanvullingen toe in plaats van historische beslissingen stilzwijgend te herschrijven.

## 5. Voortgangslogboek

### 24 oktober 2026 — WP0 afgerond

- **Taken:** WP0-01, WP0-02, WP0-03.
- **Opgeleverd:** Centrale takenlijst met acht implementatiewerkpakketten, afhankelijkheden, acceptatiecriteria en onderhoudsafspraken; verwijzingen vanuit GUI-plan en MVP-plan; contributorafspraken aangevuld.
- **Implementatiestatus:** WP1–WP8 zijn open. Er zijn nog geen GUI-projecten, spikes of frameworkbesluiten opgeleverd in deze wijziging.
- **Validatie:** Documentatieverwijzingen, taak-ID's, checkboxes en whitespace gecontroleerd; geen build of tests uitgevoerd omdat uitsluitend documentatie is gewijzigd.

### 24 oktober 2026 — WP1 afgerond

- **Taken:** WP1-01 t/m WP1-05.
- **Opgeleverd:** `src/DawVcs.Bootstrap/DawVcs.Bootstrap.csproj` en `ServiceCollectionExtensions.cs` met `AddDawVcs()`; `tests/DawVcs.Bootstrap.Tests/DawVcs.Bootstrap.Tests.csproj` en `ServiceCollectionExtensionsTests.cs` met 26 tests. Beide projecten toegevoegd aan `DawVcs.slnx`; `src/DawVcs.Cli/DawVcs.Cli.csproj` en `Program.cs` aangesloten zonder wijzigingen aan commandafhandeling, presentatie of exitcodes. Bestaande centrale packageversies hergebruikt; geen nieuwe externe dependency.
- **Ownershipcontrole:** Alle contextfactory-aanroepen in Application gecontroleerd, evenals adapterregistry, filesystemtransport en quarantainestore. De reeds aanwezige lokale Application-wijzigingen naar `using var context` en `tests/DawVcs.Application.Tests/Common/RepositoryContextOwnershipTests.cs` zijn behouden en meegenomen in validatie; deze wijzigingen zijn niet opnieuw aangebracht. Bootstraptests controleren verse contexts met onafhankelijke resources, provider-lokale singletons, alle 19 use-cases/helpers, transport-/quarantainesessies en caller-owned disposal buiten de provider.
- **Documentatie:** Deze takenlijst, `Technical Design.md` (sectie 6.1), `DAWVC_GUI_Framework_Selection_and_Implementation_Plan.md` (sectie 8.1) en `../README.md` bijgewerkt met gerealiseerde architectuur, lifetimes, disposal en beperkingen. Frameworkkeuze blijft een voorstel; er is nog geen Desktop-project.
- **Uitgevoerde validatie:**
  - `dotnet --info`: Windows x64, SDK 10.0.301, runtime 10.0.9.
  - `dotnet test tests/DawVcs.Bootstrap.Tests/DawVcs.Bootstrap.Tests.csproj -c Release --logger "console;verbosity=minimal"`: 26/26 geslaagd na correctie van een onjuiste aanname in de nieuwe test dat contextconstructie geen mappen aanmaakt; het bestaande productiegedrag is niet gewijzigd.
  - `dotnet build DawVcs.slnx -c Release --no-restore`: geslaagd zonder compilerwaarschuwingen of fouten.
  - `dotnet test DawVcs.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"`: 385/385 geslaagd, geen overgeslagen tests, inclusief 42 E2E-tests en de bestaande ownership- en architectuurtests.
  - `dotnet format DawVcs.slnx --verify-no-changes --no-restore --include src/DawVcs.Bootstrap tests/DawVcs.Bootstrap.Tests src/DawVcs.Cli/Program.cs`: geslaagd; uitsluitend de gewijzigde Bootstrap-/CLI-code gecontroleerd, bestaande lokale wijzigingen niet geformatteerd.
  - `git diff --check`: geen whitespacefouten; Git meldt alleen CRLF/LF-normalisatie voor `DawVcs.slnx`.
  - Python-documentatiecontrole: alle 27 lokale linkdoelen in de vier bijgewerkte documenten bestaan; alle vijf WP1-taken staan afgevinkt. Ankerinhoud is niet automatisch gevalideerd.
- **Beperkingen en vervolg:** Contextconstructie maakt `.dawvc/objects/.tmp` en bovenliggende mappen aan; `index.db` wordt pas op gebruik gemaakt. De bestaande singleton FL Studio-adapter houdt zoekpad-, index- en resolutiecaches zonder invalidatie vast. Dit gedrag is bewust behouden bij de DI-extractie en wordt vóór desktopgebruik in WP4-05 aangepakt. WP2-01 is de volgende uitvoeringstaak; packaging en GUI-validatie zijn nog niet uitgevoerd.

### 24 oktober 2026 — WP2-inventarisatie en eerste voortgangscontract

- **Taken:** WP2-01 en WP2-02 afgerond; WP2-03/WP2-04 gedeeltelijk uitgevoerd, WP2-05/06/07 blijven open.
- **Opgeleverd:** `src/DawVcs.Application/Common/OperationProgress.cs` met immutable getypeerde fasen, `long` tellers, onbekende/lege totalen en item-/byte-eenheden. `Scanning/ScanUseCase.cs` en `StatusUseCase.cs` bieden aanvullende per-call progress-overloads; oorspronkelijke signatures, method groups, `IUseCase` en CLI-call-sites blijven behouden. Progress bevat geen paden/vrije tekst; parallelle hashupdates zijn geserialiseerd. Scan/status checken caller cancellation vóór contextconstructie en vóór succes; status ook tijdens enumeratie/classificatie en scan na adapterwerk.
- **Tests:** `tests/DawVcs.Application.Tests/Common/OperationProgressTests.cs` en `Scanning/OperationProgressUseCaseTests.cs`: 27 nieuwe tests voor contractinvarianten, onbekende/lege totalen, grote tellers, legacy-aanroepen, semantisch gelijke resultaten met/zonder observer, oplopende parallelle hashtellers, cachehits, empty state, precancellation, faseannulering, adapter die cancellation negeert en observerfouten zonder foutmaskering.
- **Documentatie:** Nieuwe `GUI_Operation_Inventory.md` met broncontrole per langdurige use-case en ondersteunende beheeroperaties. `Technical Design.md` (6.2), GUI-ontwerp (9.1/9.2), `Known_Limitations.md`, `Recovery_Guide.md`, `Collaborator_Workflow.md` en deze takenlijst bijgewerkt. Bestaande overclaims over exact rollbackherstel aangescherpt; dry-runwrites en postpublicatiefouten expliciet gemaakt. Geen framework-/packagekeuze of persistent schema gewijzigd.
- **Uitgevoerde validatie:**
  - `dotnet test tests/DawVcs.Application.Tests/DawVcs.Application.Tests.csproj -c Release --filter "FullyQualifiedName~OperationProgress|FullyQualifiedName~ScanUseCaseTests|FullyQualifiedName~StatusUseCaseTests" --logger "console;verbosity=minimal"`: 33/33 geslaagd. Eerste uitvoeringen vereisten correcties in nieuwe testcode voor analyzerregels en een `DateTimeOffset`-matcher; die zijn opgelost vóór de geslaagde uitvoering.
  - `dotnet build DawVcs.slnx -c Release --no-restore`: geslaagd zonder compilerwaarschuwingen of fouten.
  - `dotnet test DawVcs.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"`: 412/412 geslaagd, geen overgeslagen tests, inclusief 42 E2E-tests.
  - `dotnet format DawVcs.slnx --verify-no-changes --no-restore --include` met de vijf gewijzigde/nieuwe Application- en testcodebestanden: geslaagd.
  - `git diff --check`: Markdown-eindspaties uit gewijzigde statusregels verwijderd; hercontrole zonder whitespacefouten.
  - Python-documentatiecontrole: alle 50 lokale linkdoelen in de zeven betrokken documenten bestaan; WP2-01/02 zijn afgevinkt en WP2-03 t/m WP2-07 blijven open. Ankerinhoud is niet automatisch gevalideerd.
- **Beperkingen en vervolg:** Alleen scan/status rapporteert nu voortgang; geen throttling, desktopdispatching of complete typed foutvertaling. Scan-timeout blijft coöperatief en adapter-only. Inventarisatie is broncodebewijs, geen fault-injectionvalidatie van open backendrisico's. WP2-03 is de volgende uitvoeringstaak. Annuleringsmaskering, rollback/backupretentie, afwezige-refherstel, gedeeltelijke clone/transfer en SQLite-stagingbehoud moeten nog worden geïmplementeerd/getest voordat WP2 of de viewer kan worden afgetekend.

### 24 oktober 2026 — WP2-03 afgerond: voortgang voor overige operaties

- **Taken:** WP2-03 afgerond; WP2-04 verder gedeeltelijk uitgevoerd. WP2-05/06/07 blijven open.
- **Opgeleverd:** `src/DawVcs.Application/Common/OperationProgress.cs` uitgebreid met getypeerde operatie-/transferfasen; bestaande enumwaarden behouden. `Common/NestedOperationProgress.cs` filtert child-`Completed`. Progress-overloads toegevoegd aan `Commits/CommitUseCase.cs`, `Checkouts/CheckoutUseCase.cs` (ook fast-forward), `CheckoutRestoreUseCase.cs`, `Diagnostics/DoctorUseCase.cs`, `FsckUseCase.cs`, `Remotes/FetchUseCase.cs`, `CloneUseCase.cs`, `PullUseCase.cs` en `ObjectGraphWalker.cs`. Bestaande signatures, optionele cancellationtokens, method groups, `IUseCase` en CLI-aanroepen blijven behouden.
- **Semantiek:** Bekende aantallen voor staging, objecten, controles en afgehandelde refs; onbekende totalen voor groeiende graphs en niet-instrumenteerbaar werk. Tellers gelden per fase-instantie en kunnen bij opnieuw binnengaan van een fase resetten. Nieuwe buitenste operaties melden completion na hun corewerk en resource-disposal. Samengestelde operaties geven childfasen door zonder child-completion. Geen observercallbacks toegevoegd tussen journalpublicatie, ref/HEAD/indexmutaties en commit of tussen commit-refupdate en index-clearing. Observers staan buiten diagnostische/best-effort catches. Dry-run- en rollbackgedrag blijven behouden.
- **Gerichte cancellationverbeteringen:** Entrychecks voor commit, doctor/fsck en transfers. Doctor/fsck sluiten `OperationCanceledException` uit van hun eigen inspectie-/verificatie-/fallbackcatches en checken vóór rapportafronding. Onderliggende tokenloze/breed afgevangen zoekpaden, publicatie en optionele doctorsamenvattingen blijven expliciet vervolgwerk; er zijn geen misleidende nieuwe postpublicatie-annuleringschecks toegevoegd.
- **Tests:** 98 nieuwe tests, verdeeld over `tests/DawVcs.Application.Tests/Checkouts/CheckoutProgressTests.cs`, `Commits/CommitProgressTests.cs`, bestaande en nieuwe diagnostiektests (`DiagnosticProgressTests.cs`) en de fetch/clone/pull-tests; gedeelde remote-recorder in `Remotes/RecordingRemoteProgress.cs`. Dekking voor behoud van legacy-aanroepen/resultaten, lege/bekende/onbekende totalen, nested completion, observability zonder foutmaskering, caller cancellation, disposal en fouten vóór/ná publicatie.
- **Documentatie:** Deze takenlijst, `GUI_Operation_Inventory.md`, `Technical Design.md` (6.2), GUI-ontwerp (9.1/9.2) en `Known_Limitations.md` geactualiseerd naar de geleverde scope. Geen nieuwe dependencies, persistente schemawijzigingen of frameworkkeuze.
- **Uitgevoerde validatie:**
  - `dotnet test tests/DawVcs.Application.Tests/DawVcs.Application.Tests.csproj -c Release --logger "console;verbosity=minimal"`: 264/264 geslaagd, geen overgeslagen tests.
  - `dotnet build DawVcs.slnx -c Release --no-restore`: geslaagd zonder compilerwaarschuwingen of fouten.
  - `dotnet test DawVcs.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"`: 510/510 geslaagd, geen overgeslagen tests, inclusief 42 E2E-tests.
  - `dotnet format DawVcs.slnx --verify-no-changes --no-restore --include` voor de gewijzigde Checkout-/Commit-/Common-/Diagnostics-/Remotes-code en bijbehorende tests: geslaagd.
  - `git diff --check`: geslaagd zonder whitespacefouten.
  - Python-documentatiecontrole: alle 46 lokale linkdoelen in de vijf bijgewerkte documenten bestaan; WP2-01 t/m WP2-03 afgevinkt en WP2-04 t/m WP2-07 open. Ankerinhoud niet automatisch gevalideerd.
- **Beperkingen en vervolg:** Volledige cancellation, typed timeout-/foutuitkomsten en aantoonbaar veilige rollback zijn nog niet geleverd; WP2-04 is de volgende taak. Geen UI-threadmarshalling/throttling, verse adaptercaches, strikt schrijfloze dry-runs of stagingbehoud bij SQLite-corruptieherstel beloofd. Een callbackfout of postpublicatiefout kan nog steeds optreden nadat al duurzame wijzigingen zijn gemaakt; de operatietaak/resultaat blijft leidend.

### 25 oktober 2026 — WP2-04 afgerond: cancellation en stream-disposal in alle use cases en storage

- **Taken:** WP2-04 afgerond; WP2-05 t/m WP2-07 blijven open.
- **Opgeleverd:**
  - `LooseObjectStore.OpenPayloadStreamAsync`: Stream-disposal gegarandeerd via `try ... catch { await stream.DisposeAsync(); throw; }` tijdens het lezen van object headers. Hiermee worden open file-handles op Windows voorkomen bij hash mismatches, corrupte headers of caller cancellation.
  - `PublicationJournal.ApplyAsync`: Vangt `OperationCanceledException` expliciet op, voert best-effort rollback uit via een uncancelled token (`CancellationToken.None`) zodat opruiming niet wordt afgebroken door de actieve cancellation, en hergooit direct `OperationCanceledException` (in plaats van deze in te kapselen in `CheckoutStagingException`).
  - `CloneUseCase` & `PullUseCase`: Optionele doctor-rapportage vangt en hergooit `OperationCanceledException` expliciet, zodat caller cancellation nooit meer wordt onderdrukt als een niet-fatale diagnostische fout.
  - `DependencyDiscoveryService.DiscoverAsync` & `DependencyResolverPipeline.SearchByHashAsync`: Cancellation-checks toegevoegd aan JSON-element loops, legacy plugin enumeratie en directory scanning. `OperationCanceledException` wordt niet meer ingeslikt of omgezet in ontbrekende dependencies/fallback.
  - Entry- en loop-cancellationchecks toegevoegd aan alle resterende use cases:
    - `CheckoutUseCase.cs`: Pre-context checks in `ExecuteCoreAsync` en `ExecuteFastForwardCoreAsync`.
    - `CommitUseCase.cs`: Loop checks bij dependency discovery/consolidation, tracked entries, staged entries en vóór snapshot creation.
    - `AddUseCase.cs`: Pre-context check, cancellation check vóór directory enumeratie en per kandidaatbestand.
    - `LogUseCase.cs`: Pre-context check (inclusief lege HEAD), loop check per commit in de first-parent history en vóór afronding.
    - `BindDependencyUseCase.cs`: Pre-context check en check vóór het wegschrijven van de lokale binding.
    - `SwitchUseCase.cs`: Pre-context check en check vóór early-return op reeds actieve branch.
    - `InitRepositoryUseCase.cs`: Pre-context check en cancellation rethrow in de idempotency controle.
    - `FsckUseCase.cs`: Loop checks bij snapshot entries en orphan objects.
    - `RemoteUseCase.cs`: Asynchrone overloads toegevoegd voor `AddRemoteAsync`, `RemoveRemoteAsync` en `ListRemotesAsync` met actieve `CancellationToken`-observatie.
    - `QuarantineObjectStore.cs`: Tokens geobserveerd in streamcreatie en sessiepromotie.
    - `SqliteStagingIndex.cs`: Token checks bij methode-entry op alle publieke API's en `CommandDefinition` met cancellation token doorgegeven aan Dapper queries en executions.
    - `DoctorUseCase.cs`: Unsealed en `ExecuteAsync` virtueel gemaakt; loop checks toegevoegd bij dependency inspecties, health probes en adapter enumeration.
- **Tests:** 17 nieuwe gerichte tests toegevoegd (totaal 527 tests in de solution, 100% geslaagd):
  - `LooseObjectStoreTests.cs`: Verificatie dat streams direct worden disposed bij hashmismatch en cancellation, zodat bestanden onmiddellijk kunnen worden verwijderd zonder file locks.
  - `PublicationJournalTests.cs`: Verificatie dat cancellation leidt tot rollback en directe `OperationCanceledException`.
  - `SqliteStagingIndexTests.cs`: Pre-cancellation test over alle index-methoden.
  - `OperationCancellationTests.cs`: 11 tests voor pre-cancellation op Checkout, Fast-Forward, Add, Log, BindDependency, Switch, InitRepository, RemoteUseCase, Clone/Pull doctor reporting, DependencyDiscovery en Resolver search.
- **Documentatie:** `GUI_Implementation_Tasks.md`, `GUI_Operation_Inventory.md` en `Technical Design.md` geactualiseerd met de geleverde garanties.
- **Uitgevoerde validatie:**
  - `dotnet build DawVcs.slnx -c Release --no-restore`: geslaagd met 0 waarschuwingen en 0 fouten.
  - `dotnet test DawVcs.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"`: 527/527 geslaagd, geen overgeslagen tests.
  - `dotnet format DawVcs.slnx --verify-no-changes --no-restore`: geslaagd zonder codestijlwijzigingen.
  - `git diff --check`: geslaagd zonder whitespacefouten.
- **Beperkingen en vervolg:** Rollback blijft best-effort; fouten na duurzame refpublicatie kunnen de repository in een inconsistente toestand achterlaten als rollback faalt. SQLite-corruptieherstel wist nog steeds staged entries (WP5-08). WP2-05 (typed fouten en centrale categorieën) is de volgende stap.

Gebruik bij volgende updates hetzelfde formaat: datum, taak-ID's, bestanden/resultaten, uitgevoerde validatie en open beperkingen.


### 13 november 2026 — WP2 afgerond

- **Taken:** WP2-05, WP2-06, WP2-07; bestaande voortgangs- en cancellationcontracten behouden.
- **Opgeleverd:** `Common/OperationFailure.cs` met centrale, typegebaseerde categorieën en veilige `Unknown`-fallback. `IncompleteRollbackException` bevat recoverypad en technische oorzaak. Lokale schemafouten en remote schema-/identiteitsfouten zijn typed; remote-fouten behouden hun bestaande CLI-exceptionbasis. Geen exceptiontekst wordt geparsed; timeout wordt niet uit cancellation geraden.
- **Rollback:** `PublicationJournal.cs` verzamelt herstelproblemen, probeert overige entries te herstellen en bewaart back-ups na mislukte rollback én disposal. `CheckoutUseCase.cs` meldt ook ref-/indexcompensatiefouten. Een incomplete rollback heeft voorrang op annulering; oorspronkelijke fouten blijven in aggregate causes beschikbaar. Succesvolle compensatie behoudt de oorspronkelijke exception. Cleanup en compensatie gebruiken geen geannuleerd callertoken.
- **Tests:** `OperationFailureTests.cs`, `PublicationJournalTests.cs`, `CheckoutProgressTests.cs` en `CheckoutAcTests.cs` controleren typegebaseerde mapping, hersteldata na disposal, metadatafouten, filecompensatie en annulering ná bestandspublicatie. Bestaande tests die een onderdrukte rollbackfout accepteerden zijn op het expliciete contract aangepast.
- **Documentatie:** Technical Design §6.3, operatie-inventarisatie en Recovery Guide beschrijven foutcategorieën, lokale gevoelige details, annuleringsgrenzen en gedeeltelijke duurzame effecten. No-op blijft een bestaand use-caseresultaat; cancellation of een exception is geen garantie dat niets gepubliceerd werd.
- **Uitgevoerde validatie:** Linux x64, .NET SDK 10.0.404; `dotnet test DawVcs.slnx -c Release --no-restore` — 534/534 geslaagd, 0 overgeslagen; `dotnet build DawVcs.slnx -c Release --no-restore` — 0 fouten en 0 waarschuwingen. `git diff --check` geslaagd. `dotnet format DawVcs.slnx --verify-no-changes --no-restore` — geslaagd voor de volledige solution.
- **Grenzen:** Geen GUI of frameworkkeuze geleverd; WP3 nog open. Commit/fetch/pull/clone zijn niet ineens transactioneel. Untyped legacy `InvalidOperationException` blijft `Unknown`; geen onbetrouwbare categorisatie op tekst. Een repositoryroot als recoverypad duidt metadataherstel aan en belooft geen volledige back-up. Windows-desktopvalidatie hoort bij latere werkpakketten en is hier niet uitgevoerd.


### 13 november 2026 — WP3-01 meetprotocol vastgesteld

- Budgetten, fixtures, meetherhalingen en Windows-knock-outchecks vooraf vastgelegd in `gui/WP3_Measurement_Protocol.md`.
- Nieuwe branch `spike/gui-wp3-framework-comparison` expliciet op verzoek aangemaakt vanaf `feat/gui-wp2-operation-errors` (`6976c5b`). Geen extra branches per prototype.
- Geen framework gekozen; WP3-02 t/m WP3-06 blijven open tot hun bewijs beschikbaar is.


### 13 november 2026 — WP3-prototypes geïmplementeerd; Windows-validatie open

- **Taken:** WP3-02/03/04 gedeeltelijk; WP3-05/06 nog open. Alleen WP3-01 is volledig afgevinkt.
- **Opgeleverd:** `spikes/gui/GuiSpikes.slnx` met Shared, Avalonia, Wpf, Photino, Harness en Tests; frameworkpackages alleen in de lokale `Directory.Packages.props`. Bestaande productieprojecten en productiepackages ongewijzigd. Echte status/scan/doctor, native folderpickers, 10.000 synthetische rijen, filtering/virtualisatie, thema en cancellation/close-coördinatie. Avalonia/WPF hebben folderdrop; Photino-drop blijft open.
- **Veiligheid/lifecycle:** Worker-thread inclusief synchronische backendstart, verse providers per operatie, gesamplede progress en operatie-ID voor late updates, geblokkeerde repositorywissels tijdens werk, invalid-foldercheck vóór contextconstructie, annuleren en afwachten bij sluiten. Photino-webviewfilesystemtoegang uitgeschakeld; upstream oorsprongvalidatie als reviewpunt vastgelegd.
- **Fixture/metingen:** Harness genereert uitsluitend synthetische FLP/WAV en optioneel een echte 256 MiB asset; bestaande doelen worden geweigerd. Backendfouten leveren een niet-nul exitcode. `--cancel-status` accepteert alleen de gemarkeerde grote fixture en meet vanaf vóór tokenannulering tot einde taak.
- **Windows-vervolg:** `publish.ps1` publiceert tweemaal en vergelijkt hashes; `smoke-windows.ps1` controleert native openen/sluiten. `.github/workflows/gui-spikes.yml` bouwt/test/publiceert op Windows en bewaart artifacts. Het workflow is geleverd, niet lokaal op Windows uitgevoerd; smoke-vensterhandle is geen first-render/performancebewijs.
- **Validatie:** 4/4 geïsoleerde tests geslaagd, inclusief headless Avalonia-virtualisatie (10.000 items, 1–200 gerealiseerde containers), filtering, veilig sluiten en echte status/lifecycle. Bestaande regressiesuite: 534/534 geslaagd. Definitieve build/format-, publicatie- en meetresultaten worden in de aansluitende bewijsregistratie vastgelegd.
- **Documentatie:** Ontwerpdocument, Technical Design, SBOM, SecurityThreatModelReview, spike-README en Proposed ADR-GUI-001 bijgewerkt. Native Windows-toetsenbord/DPI/folderpicker-/cancelchecks en frameworkkeuze zijn niet als voltooid gepresenteerd.


### 13 november 2026 — WP3 Linux-bewijs vastgelegd

- **Bron:** Prototypecommit `73d613b`; meetprotocol vooraf gecommit als `e9050ac`.
- **Checks:** Definitieve geïsoleerde Release-build geslaagd met 0 waarschuwingen en 0 fouten. 4/4 spike-tests en 534/534 bestaande regressietests geslaagd, geen skips. `dotnet format spikes/gui/GuiSpikes.slnx --no-restore --verify-no-changes` geslaagd voor de volledige spike-solution; `git diff --check` en nieuwe lokale documentatielinks gecontroleerd.
- **Metingen:** Model-filter p95 1,5975 ms (geen UI-renderlatency); echte status/scan/doctor zonder failurecategorie; tien echte 256 MiB-status-hashingcancellations voltooid, p95/max 26,823 ms (geen native scan/cancelacceptatie). Definitieve self-contained win-x64-crosspublicatie voor alle drie kandidaten tweemaal uitgevoerd met identieke niet-PDB-hashes. Publicatiegrootte: Avalonia 207,45 MiB, WPF 143,39 MiB, Photino 82,68 MiB exclusief externe WebView2. Ruwe data, omgeving, pakketmetadata en manifests staan in `gui/evidence/`; toelichting in `gui/WP3_Results.md`.
- **Mislukte poging:** Eén herhaalde crosspublicatieronde stopte met MSB4166 (buildnodes verdwenen tijdens overlappende .NET-tooling). Definitieve publicaties serieel opnieuw uitgevoerd zonder node reuse; alle zes geslaagd. Niet als compiler- of frameworkfout gepresenteerd.
- **Blijft open:** Native Windows-startup/procesboomgeheugen, filter-tot-render, UI-respons, scan/cancel, folderpicker, toetsenbord, DPI, contrast, native Photino-drop en runtime-/originreview. Het Windows-workflow en PowerShell-scripts zijn geleverd maar hier niet op Windows uitgevoerd. Geen ontwikkeltijdcijfers verzonnen. WP3-02 t/m WP3-06 en de frameworkkeuze blijven daarom open; WP4 niet gestart.


### 13 november 2026 — Gebruikersbesluit Avalonia en start WP4

- **Besluit/scope:** De gebruiker kiest expliciet Avalonia, vereist meerdere OS'en en autoriseert doorgaan met WP4. De oorspronkelijke vergelijkingsgate van WP3 is vervangen; ADR-GUI-001 is Accepted. Dit is geen PR-approval. Ontbrekende vergelijkingsmetingen zijn niet als geslaagd aangemerkt; native Avalonia-QA gaat naar WP8 voor Windows/macOS/Linux.
- **Branch:** `feat/gui-wp4-avalonia-shell` vanaf `spike/gui-wp3-framework-comparison` (`7e6cc1f`); geen WP5-features in deze wijziging.
- **Implementatie:** `DawVcs.Desktop` en `DawVcs.Desktop.Tests` aan hoofdsolution toegevoegd; Avalonia/FluentTheme/Inter 12.1.6 en CommunityToolkit.Mvvm 8.4.0 centraal beheerd. Spike-packageversies dedupliceren via dezelfde centrale bron; CLI-gedrag ongewijzigd.
- **Contract:** Nieuwe lichte `OpenRepositoryUseCase` via Bootstrap voorkomt directe Desktop→repositoryimplementatie en vraagt geen status/indexherstel op. Alle backendstartwerk draait op een worker. De coördinator serialiseert werk, wacht op cancellation/disposal, vernieuwt adapterproviders per operatie en filtert late sessieresultaten. Succesvolle backendpublicatie wordt bij een laat annuleerverzoek behouden.
- **Shell:** Navigatie en commandstates, thema en design tokens, native platformabstracties voor folders/dialogs/explorer, begrensde lokale MRU-opslag met tijdelijke file + atomic move. Productpagina's zijn placeholders tot WP5.
- **Vroege productisering:** Apart desktop-CI-workflow voor Windows/macOS/Linux met headless tests en self-contained artifacts. Remote uitvoering niet geverifieerd wegens API-toegang.
- **Validatie in uitvoering:** De eerste 12 gerichte tests slagen. Native Linux-opstarttest vond een ontbrekend Inter-lettertype; als gebundelde dependency gecorrigeerd. Tijdelijk user-local Xvfb en testtools gebruikt zonder systeeminstallatie; verdere native controles en definitieve suite volgen hieronder.


### 13 november 2026 — WP4 afgerond

- **Taken:** WP4-01 t/m WP4-08 voltooid. Viewerfeatures blijven WP5; geen automatische voortzetting naar WP5 in deze branch.
- **Validatie:** 554/554 tests in de hoofdsolution geslaagd, nul skips (16 Desktop, 288 Application, 27 Bootstrap); Release-build met 0 fouten en 0 waarschuwingen; volledige `dotnet format --verify-no-changes --no-restore` geslaagd; whitespace en documentatielinks gecontroleerd. SDK 10.0.404, Linux x64. MSBuild-nodehergebruik lokaal uitgeschakeld na de eerder bekende runner-nodefout.
- **Native bewijs:** Self-contained Linux-app onder user-local Xvfb daadwerkelijk bediend: repository openen, MRU opslaan onder gebruikersdata met Unix 0600, donker/licht wisselen en native sluiten met exitcode 0. Screenshots, assemblyhash en samenvattingen in `gui/evidence/wp4`; toelichting in `gui/WP4_Validation.md`.
- **Crosspublicatie:** win-x64, linux-x64, osx-arm64 en osx-x64 geslaagd. Inter en volledige OFL-fontlicentie meegepubliceerd. Dit is geen native Windows/macOS-acceptatie; die controles blijven in WP8.
- **Extra lifecycleborging:** Succesvolle backendpublicatie blijft zichtbaar bij een laat annuleerverzoek zolang dezelfde session actief is. Een geannuleerde repositorywissel meldt zich niet voltooid terwijl het vorige backendwerk nog loopt. Parallelle shell-openacties/MRU-writes zijn geserialiseerd; late startup-MRU-loads kunnen nieuwe lijsten niet terugzetten.
- **Documentatie:** Geaccepteerde framework-ADR, GUI-plan, takenlijst, Technical Design, Installation, QuickStart, README, Known Limitations, SBOM en securityreview bijgewerkt. Historische WP3-metingen blijven bewaard; hun onuitgevoerde checks zijn niet als geslaagd gemarkeerd.
- **Beperkingen:** Native Windows/macOS en fysieke DPI/toegankelijkheids-/performancemetingen niet uitgevoerd; CI-configuratie en artifacts voorzien, remote resultaten niet bereikbaar via de huidige GitHub API. Geen PR-approval of merge uitgevoerd.


### 16 november 2026 — WP4-05/WP4-08 regressie: Windows SQLite-bestandslock

- **Melding:** De Windows-spikepipeline faalt bij `Status_IsRealAndDisposeWaitsForCompletion`: na sessiedisposal kan cleanup `index.db` niet verwijderen.
- **Oorzaak:** Contexts en logische SQLite-connecties worden opgeruimd, maar `Microsoft.Data.Sqlite` houdt fysieke handles in de pool. `SqliteStagingIndex.Dispose` ruimde alleen zijn semaphore op.
- **Correctie:** Bij disposal de pool voor uitsluitend de eigen connectionstring legen (`ClearPool`), met idempotente disposal en een disposed-guard. Actieve verbindingen blijven geldig en sluiten bij terugkeer; andere databasepools blijven intact. Geen algemene `ClearAllPools`-oplossing in tests, sleeps, retries of genegeerde cleanupfouten. Het bestaande corruptieherstel/WP5-08 valt buiten deze fix.
- **Regressietests:** Native pooled handle expliciet volgen, sluiting controleren en de repository direct verwijderen; daarnaast actieve verbindingen en een andere pool op bruikbaarheid controleren. De bestaande spike-lifecycletest blijft ongewijzigd.
- **CI:** Het Windows-spikeworkflow reageert nu ook op WP4-pushes en deze Infrastructure-wijziging, zodat de gemelde test opnieuw wordt uitgevoerd. Remote uitkomst nog niet beschikbaar in deze sessie.
- **E2E-fixture:** De disposed-guard maakte zichtbaar dat de rollbacktest dezelfde context teruggaf aan checkout en geneste status. De factory maakt nu per aanroep een eigen context en ruimt ook de verificatiecontext op; rollbackasserties blijven behouden.
- **Validatie:** Nieuwe native-handle-regressietest faalt aantoonbaar tegen de oude code; met de fix 556/556 hoofdsolutiontests en 4/4 spike-tests geslaagd (exacte gemelde `--no-build --logger trx`-stap), nul skips. Release-builds van hoofdsolution en spikes: 0 fouten/waarschuwingen; volledige hoofdsolution-opmaakcontrole en `git diff --check` geslaagd. Ruwe samenvatting in `gui/evidence/wp4/sqlite-pool-fix.json`. Windows-CI moet de Windows-uitkomst nog bevestigen.
