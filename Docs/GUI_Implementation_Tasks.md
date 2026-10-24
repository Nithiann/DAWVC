# DAWVC GUI — Takenlijst en voortgang

**Laatst bijgewerkt:** 24 oktober 2026  
**Status:** WP1 voltooid; gedeelde Bootstrap in gebruik door CLI, desktopimplementatie nog niet gestart  
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

**Eerstvolgende taak:** WP2-01 — cancellation, synchronische werkpaden, fouttypen en dry-run-neveneffecten per langdurige use-case inventariseren.

**Lopende taken:** Geen.  
**Blokkades:** Geen voor WP2; Windows x64 met .NET SDK 10.0.301, Release-build en volledige testsuite zijn gevalideerd.  
**Aandachtspunten:** Contextconstructie maakt al metadatamappen aan. De singleton FL Studio-adapter heeft geen cache-invalidatie; neem cacheverversing/lifetimebeleid mee in WP4-05 vóór actuele desktoprescans worden opgeleverd.

| Werkpakket | Status | Afhankelijkheden | Oplevering |
| --- | --- | --- | --- |
| WP0 — Planning en documentatie | Voltooid | Geen | Takenlijst, verwijzingen en onderhoudsafspraken |
| WP1 — Gedeelde composition root | Voltooid | WP0 | Bootstrap en ongewijzigd CLI-gedrag |
| WP2 — Operatiecontracten | Open | WP1 | Voortgang, cancellation en typed fouten |
| WP3 — Frameworkspikes en ADR | Open | WP1; WP2 voor progress/cancellation | Onderbouwde frameworkkeuze |
| WP4 — Desktop-shell en session | Open | WP2, WP3 | Responsieve shell en veilige lifecycle |
| WP5 — Viewer-release | Open | WP4 | Geen mutaties aan projectinhoud, staging, branches of refs |
| WP6 — Lokale mutaties | Open | WP5 | Preflight, bevestiging en recovery |
| WP7 — Clone en remotes | Open | WP6 | Backendconforme samenwerking |
| WP8 — Productisering | Open | Start bij WP3/WP4; afronding na WP7 | Gevalideerde Windows-release |

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

- [ ] WP2-01 — Bestaande cancellation, synchronische werkpaden, fouttypen en dry-run-neveneffecten inventariseren per langdurige use-case.
- [ ] WP2-02 — `OperationProgress` in Application introduceren; optioneel per aanroep ontsluiten met behoud van bestaande CLI-call-sites.
- [ ] WP2-03 — Voortgang aansluiten op scan/status, commit, checkout/restore, diagnostics en transfers waar zinvol; onbekende totalen ondersteunen.
- [ ] WP2-04 — Cancellation tijdens enumeratie, hashing, adapterwerk en I/O controleren en ontbrekende checks toevoegen.
- [ ] WP2-05 — Typed fouten aanvullen en centrale gebruikerscategorieën definiëren zonder exceptiontekst te parsen; timeout, annulering en incomplete rollback onderscheiden.
- [ ] WP2-06 — Veilige annuleringsgrenzen en rollbackbeleid vastleggen en testen; cleanup mag niet door een reeds geannuleerd token worden overgeslagen.
- [ ] WP2-07 — Contract-, cancellation- en regressietests uitvoeren; Application-contracten en beperkingen documenteren.

**Acceptatie:** Bestaande clients blijven werken zonder progressconsument. Annulering is pas afgerond zodra het werk stopt; fouten zijn typed presenteerbaar.

### WP3 — Frameworkspikes en ADR

- [ ] WP3-01 — Gelijke fixtures, meetprocedure en vooraf vastgelegde acceptatiegrenzen bepalen voor startup, geheugen, filterrespons en cancellation.
- [ ] WP3-02 — Avalonia-spike bouwen: folderpicker, echte status, 10.000 assets, filtering, annuleerbare scan, doctor, thema en Windows-publicatie.
- [ ] WP3-03 — Dezelfde verticale slice in WPF uitvoeren.
- [ ] WP3-04 — Dezelfde slice in Photino of Uno uitvoeren conform het ontwerpdocument; een eventuele scopewijziging expliciet onderbouwen en vastleggen.
- [ ] WP3-05 — Virtualisatie, drag-and-drop, toetsenbord, DPI, testbaarheid, licenties, ontwikkeltijd en reproduceerbare packaging vergelijken.
- [ ] WP3-06 — Meetresultaten documenteren en de frameworkkeuze vastleggen in `Docs/adr`; ontwerpstatus en gekozen dependencies bijwerken.

**Acceptatie:** De keuze is met metingen onderbouwd, voldoet aan de knock-outcriteria en introduceert geen onbedoelde commerciële controlafhankelijkheid. Prototypes mogen geïsoleerd worden uitgevoerd; branches alleen op expliciet verzoek aanmaken.

### WP4 — Desktop-shell en repositorysession

- [ ] WP4-01 — `src/DawVcs.Desktop` en `tests/DawVcs.Desktop.Tests` toevoegen met de gekozen stack; packages centraal beheren.
- [ ] WP4-02 — Startup, Bootstrap-integratie, navigatie, thema en design tokens bouwen.
- [ ] WP4-03 — Interfaces voor folderpicker, dialogs en openen in de bestandsverkenner implementeren.
- [ ] WP4-04 — Recente repositories in gebruikersgebonden opslag implementeren, zonder machinepaden in gedeelde repositorydata op te slaan.
- [ ] WP4-05 — Repositorysession met sessie-identiteit, cancellation en disposal implementeren; late resultaten van een vorige session negeren. Adaptercacheverversing of een expliciet lifetimebeleid implementeren en testen, inclusief gewijzigde/ontbrekende samples en gewijzigde zoekpaden.
- [ ] WP4-06 — Operatiecoördinator bouwen met busy state, begrensde progressupdates, foutpresentatie en serialisatie van conflicterende acties.
- [ ] WP4-07 — UI-threadresponsiviteit borgen, inclusief synchronisch werk vóór de eerste `await`.
- [ ] WP4-08 — Architectuurtests toevoegen voor Desktop → Application en het verbod op Desktop → CLI; lifecycle-, commandstate- en navigatietests uitvoeren.

**Acceptatie:** Repositorywissels tonen geen oude resultaten; langdurig werk blokkeert de UI niet. GUI-coördinatie vervangt geen backendbeveiliging tegen gelijktijdig CLI-gebruik.

### WP5 — Viewer-release zonder projectinhoudmutaties

- [ ] WP5-01 — Startscherm met openen, recente repositories en afhandeling van ongeldige repositories opleveren.
- [ ] WP5-02 — Dashboard met branch, HEAD, statusaantallen en afzonderlijke laad-/fouttoestanden bouwen.
- [ ] WP5-03 — Changes-view met groepering, zoeken, selectie en virtualisatie bouwen, zonder stagingacties.
- [ ] WP5-04 — Begrensde/gepagineerde history en commit-/snapshotdetails aanbieden; ontbrekende queries via Application toevoegen.
- [ ] WP5-05 — Scan, doctor en fsck met voortgang, annulering en geredacteerde rapportexport aansluiten.
- [ ] WP5-06 — Beschikbare dependencygegevens tonen en filteren zonder bindacties.
- [ ] WP5-07 — Loading-, empty-, success- en errorstates testen; CLI/GUI-resultaten semantisch vergelijken met gedeelde fixtures.
- [ ] WP5-08 — Cachewrites en overige metadata-neveneffecten controleren en documenteren; viewer-smoketest en Windows-previewpublicatie uitvoeren.

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

### WP8 — Productisering

- [ ] WP8-01 — Vroeg een reproduceerbare self-contained Windows x64-publicatie inrichten en packageversies/licenties registreren.
- [ ] WP8-02 — Vanaf de desktop-shell CI-build, tests en artifacts toevoegen.
- [ ] WP8-03 — Signing en distributiestrategie vastleggen; automatische updates pas na expliciet beveiligings- en distributiebesluit implementeren.
- [ ] WP8-04 — High-DPI-, toetsenbord-, focus- en toegankelijkheidstests uitvoeren.
- [ ] WP8-05 — Grote-projecttests met echte hashing en grote samples uitvoeren; resultaten vergelijken met de vastgelegde budgetten.
- [ ] WP8-06 — Logs, crashdetails en exports op pad-/privacyredactie controleren.
- [ ] WP8-07 — Installatie, QuickStart, beperkingen, recovery, screenshots en afhankelijkhedenoverzicht bijwerken voor de werkelijk geleverde GUI.
- [ ] WP8-08 — Gepubliceerde Windows-app op een schone omgeving valideren en releasecriteria met bewijs aftekenen.

**Acceptatie:** De distributie is reproduceerbaar en de ondersteunde Windows-workflows zijn aantoonbaar getest; documentatie beschrijft geen nog ongeïmplementeerde functies als beschikbaar.

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

Gebruik bij volgende updates hetzelfde formaat: datum, taak-ID's, bestanden/resultaten, uitgevoerde validatie en open beperkingen.
