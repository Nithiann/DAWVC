# DAWVC — MVP Requirements Specification

> **Documentversie:** 1.0  
> **Status:** Approved baseline for implementation  
> **Productversie:** MVP v0.1  
> **Normatieve architectuur:** `DAWVC_Technical_Design.md` v1.1  
> **Primaire implementatie:** C# / .NET 10  
> **Doelplatform:** Windows 11 x64  
> **Eerste DAW-adapter:** FL Studio 2026.x  
> **Releasevorm:** Public technical preview

---

# 1. Doel van dit document

Dit document specificeert het vereiste gedrag van DAWVC MVP v0.1. Het Technical Design beschrijft de architectuur en interne modellen; dit document bepaalt wat de MVP functioneel en niet-functioneel moet leveren en hoe dat objectief wordt geaccepteerd.

De termen **MOET**, **MAG NIET**, **BEHOORT** en **MAG** zijn normatief:

- **MOET / MAG NIET:** harde MVP-eis;
- **BEHOORT:** gewenst gedrag waarvan afwijking expliciet moet worden gemotiveerd;
- **MAG:** optioneel gedrag dat de MVP-acceptatie niet blokkeert.

Alle requirements hebben een stabiele identifier. Het Implementation Plan en de tests verwijzen naar deze identifiers.

---

# 2. Productdoel

DAWVC is een lokaal version-control- en dependency-managementsysteem voor DAW-projecten. MVP v0.1 moet één FL Studio-project veilig kunnen registreren, inspecteren, versioneren, herstellen en op een andere Windows-machine reproduceerbaar voorbereiden.

De MVP bewijst vijf kernproposities:

1. Een native `.flp` kan immutable en byte-exact worden geversioneerd.
2. Projectassets kunnen op inhoud worden geïdentificeerd in plaats van op lokaal pad.
3. Lokale folderstructuren en plugininstallaties lekken niet naar gedeelde identities.
4. Een checkout publiceert nooit een gedeeltelijk of ongeverifieerd projectartifact.
5. FL Studio-specifieke inspectie blijft geïsoleerd achter een DAW-onafhankelijk adaptercontract.

---

# 3. Goedgekeurde productbesluiten

De volgende eerder open keuzes zijn voor MVP v0.1 bindend vastgesteld.

| ID | Besluit |
|---|---|
| DEC-MVP-001 | MVP v0.1 wordt een publieke technical preview en portfolio-waardige release. |
| DEC-MVP-002 | Eén repository vertegenwoordigt één logisch muziekproject met precies één primaire `.flp` per snapshot. |
| DEC-MVP-003 | Windows 11 x64 en FL Studio 2026.x worden officieel getest. Andere FLP-versies mogen opaque worden behandeld. |
| DEC-MVP-004 | DAWVC gebruikt hybride staging: de primaire `.flp` en reeds gevolgde assets worden automatisch meegenomen; nieuwe assets vereisen `dawvc add`. |
| DEC-MVP-005 | Samples, recordings en plugin-identiteiten worden best-effort gedetecteerd. Third-party plugincontent is best-effort of user-assisted. |
| DEC-MVP-006 | DAWVC herstelt assets en bindings, maar wijzigt in v0.1 geen FL Studio-instellingen en herschrijft geen `.flp`. Handmatige search-pathconfiguratie of relinking mag nodig zijn. |
| DEC-MVP-007 | Ontbrekende verplichte `Bundle`-dependencies blokkeren een normale commit; `--allow-incomplete` kan bewust overrulen. `ReferenceOnly`-requirements blokkeren niet. |
| DEC-MVP-008 | De v0.1-commandoset bestaat uit `init`, `scan`, `status`, `add`, `commit`, `log`, `checkout`, `branch`, `switch`, `doctor` en `fsck`. |
| DEC-MVP-009 | Checkout stopt bij lokale wijzigingen. `--force` maakt eerst een recovery copy; `--restore-to` herstelt zonder de actieve workspace te overschrijven. |
| DEC-MVP-010 | Objecten gebruiken een versieerbaar envelope. Canonical JSON-manifests en blobs worden in v0.1 ongecomprimeerd opgeslagen. |
| DEC-MVP-011 | De release wordt geleverd als self-contained Windows x64 executable/ZIP. |
| DEC-MVP-012 | De performancebaseline is 5.000 gevolgde assets en 100 GB bundled content, met streaming I/O en maximaal circa 512 MB normaal geheugengebruik. |
| DEC-MVP-013 | Het plan gaat uit van één ontwikkelaar en gebruikt ideale werkdagen zonder kalenderdeadline. |
| DEC-MVP-014 | De broncode wordt publiek op GitHub gepubliceerd onder de MIT-licentie; fixtures bevatten uitsluitend zelfgemaakte of vrij distribueerbare content. |

---

# 4. Scope

## 4.1 In scope

MVP v0.1 bevat:

- één lokale repository per logisch muziekproject;
- één primaire FL Studio `.flp` per snapshot;
- een DAW-onafhankelijk core-domein;
- `.flp` als `SingleFileArtifact`;
- immutable content-addressed object storage met BLAKE3;
- hybride staging;
- commits, log, branches en branch switching;
- read-only FLP-detectie, inspectie en validatie;
- opaque fallback voor onbekende of niet-ondersteunde FLP-versies;
- discovery van samples, recordings en pluginrequirements;
- assetbundling volgens portability policies;
- lokale dependencybindings;
- staged en geverifieerde checkout;
- recovery bij geforceerde checkout;
- project- en environmentdiagnose via `doctor`;
- repository- en artifactintegriteit via `fsck`;
- self-contained Windows x64-distributie;
- geautomatiseerde unit-, fixture-, integration- en end-to-endtests.

## 4.2 Out of scope

MVP v0.1 bevat nadrukkelijk niet:

- remotes, clone, fetch, pull of push;
- server, accounts, authenticatie of authorization;
- desktop- of webinterface;
- projectlocking tussen gebruikers;
- tagcommando’s;
- automatische merge van native DAW-projectbestanden;
- semantische diff of merge;
- native FLP-writing of path rewriting;
- automatische wijziging van FL Studio Browser/search folders;
- automatische installatie van plugins of plugincontent;
- volledige detectie van content die intern door third-party plugins wordt beheerd;
- FL Studio zipped-projectsupport als volwaardige artifactvorm;
- functionele ondersteuning voor Ableton, Logic, REAPER of andere DAWs;
- cross-DAW collaboration snapshots;
- objectcompressie, delta-compressie of content-defined chunking;
- telemetry, auto-update of een managed crash-reportingplatform.

---

# 5. Gebruikers en systeemactoren

## 5.1 Primaire gebruiker

Een producer/developer die lokaal via de CLI werkt en basiskennis heeft van bestanden, terminals en FL Studio-projecten.

## 5.2 Externe actoren

| Actor | Rol in MVP |
|---|---|
| Windows filesystem | Opslag van workspace, object store, staging en recovery copies. |
| FL Studio | Opent het door DAWVC herstelde native project; wordt niet door DAWVC aangestuurd of gewijzigd. |
| FL Studio-adapter | Detecteert, inspecteert en valideert `.flp` read-only. |
| Pluginfilesystem | Wordt gescand om lokale plugininstallaties te inventariseren; binaries worden niet geladen of uitgevoerd. |
| Gebruiker | Bevestigt uitzonderingen, kiest bindings en configureert zo nodig FL Studio search folders. |

---

# 6. Begrippen

| Term | Betekenis |
|---|---|
| Repository | Version-control-eenheid voor één logisch muziekproject. |
| Workspace | Lokale working tree plus machine-specifieke bindings en caches. |
| Primary artifact | De actieve `.flp` die door een snapshot wordt gerepresenteerd. |
| Blob | Immutable contentobject met BLAKE3-identiteit. |
| Snapshot | Immutable toestand van projectartifact, dependencies en environmentrequirements. |
| Bundle dependency | Dependency waarvan de bytes in de repository mogen en moeten worden opgeslagen. |
| ReferenceOnly dependency | Requirement dat wordt geregistreerd maar niet wordt gebundeld, zoals een pluginbinary. |
| Binding | Lokale koppeling van een gedeelde dependencyidentity aan een machine-specifieke locator. |
| Opaque artifact | Artifact dat byte-exact wordt opgeslagen zonder betrouwbare semantische interpretatie. |
| Recovery copy | Veilige kopie van lokale wijzigingen die vóór een geforceerde checkout wordt gemaakt. |
| Required dependency | Dependency die nodig is om het project correct te reconstrueren. |
| Optional dependency | Dependency waarvan afwezigheid het kernproject niet blokkeert. |

---

# 7. Globale invariants

- **INV-001:** De originele native artifactbytes zijn de primaire waarheid.
- **INV-002:** DAWVC MAG een `.flp` in MVP nooit herschrijven.
- **INV-003:** Dependencyidentity MAG niet afhankelijk zijn van een absoluut lokaal pad.
- **INV-004:** Commits, snapshots, manifests en blobs zijn immutable.
- **INV-005:** Een branchref MAG alleen naar een volledig geschreven en geverifieerde commit verwijzen.
- **INV-006:** Een checkout MAG nooit een gedeeltelijk artifact als succesvol publiceren.
- **INV-007:** Gedeelde metadata MAG geen credentials of lokale bindings bevatten.
- **INV-008:** Pluginbinaries en commerciële libraries worden standaard niet gebundeld.
- **INV-009:** Een parserfout is niet automatisch bewijs van repositorycorruptie.
- **INV-010:** Onbekende FLP-versies blijven byte-exact versioneerbaar.
- **INV-011:** De core MAG niet refereren aan FL Studio-specifieke implementatiecode.
- **INV-012:** Iedere destructive workspacehandeling vereist een veilige recoveryroute.

---

# 8. Functionele requirements — repository en configuratie

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-REP-001 | Must | `dawvc init` MOET een nieuwe repository initialiseren in de huidige of expliciet opgegeven directory. |
| FR-REP-002 | Must | Initialisatie MOET `.dawvc/` en een versieerbare `dawvc.yaml` aanmaken. |
| FR-REP-003 | Must | Initialisatie MOET weigeren wanneer een bovenliggende of huidige repository ambiguïteit veroorzaakt, tenzij de gebruiker een expliciete root opgeeft. |
| FR-REP-004 | Must | Eén repository MOET exact één logisch project-ID bevatten. |
| FR-REP-005 | Must | De configuratie MOET precies één primaire artifactlocator ondersteunen. |
| FR-REP-006 | Must | De repository MOET een default branch `main` aanmaken. |
| FR-REP-007 | Must | Een herhaalde `init` in dezelfde geldige repository MOET idempotent zijn en bestaande history behouden. |
| FR-REP-008 | Must | DAWVC MOET repositories met een nieuwere onbekende repositorieschemaversie read-only weigeren in plaats van ze stil te wijzigen. |
| FR-REP-009 | Should | `init` BEHOORT de gevonden `.flp` automatisch voor te stellen wanneer exact één kandidaat aanwezig is. |
| FR-REP-010 | Must | Bij nul of meerdere `.flp`-kandidaten MOET de gebruiker de primaire artifactlocator expliciet kiezen. |

## 8.1 Configuratieschema v1

`dawvc.yaml` MOET minimaal de volgende logische velden ondersteunen:

```yaml
schemaVersion: 1
repositoryId: "<uuid>"
projectName: "Apotheosis"
primaryArtifact: "Apotheosis.flp"
defaultBranch: "main"
policies:
  newDependencies: require-add
  missingBundledDependencies: block
  unknownProjectFormat: allow-opaque
  invalidProjectArtifact: require-confirmation
```

- **FR-CFG-001:** Paden in `dawvc.yaml` MOETEN relatief aan de repositoryroot zijn.
- **FR-CFG-002:** Absolute projectpaden in gedeelde configuratie zijn verboden.
- **FR-CFG-003:** Onbekende configuratievelden MOETEN bij lezen behouden blijven wanneer DAWVC de configuratie opnieuw schrijft.
- **FR-CFG-004:** Ongeldige of ontbrekende vereiste configuratie MOET een typed configuration error opleveren.

---

# 9. Functionele requirements — scan en projectdetectie

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-SCAN-001 | Must | `dawvc scan` MOET de primaire artifactlocator, working tree en bekende dependencies inspecteren zonder native bestanden te wijzigen. |
| FR-SCAN-002 | Must | Scan MOET FLP-detectie baseren op extensie plus herkenbare signature/structuur waar beschikbaar. |
| FR-SCAN-003 | Must | Scan MOET een detection status, confidence, adapterversie en bevindingen rapporteren. |
| FR-SCAN-004 | Must | Een herkende maar niet-ondersteunde FLP-versie MOET als `Unsupported` worden gerapporteerd en als opaque artifact bruikbaar blijven. |
| FR-SCAN-005 | Must | Onvoldoende detectieconfidence MOET `Unknown` opleveren; DAWVC MAG niet speculatief gaan parsen. |
| FR-SCAN-006 | Must | Concrete schending van bekende formatinvariants MOET `Invalid` opleveren. |
| FR-SCAN-007 | Must | Parserexceptions MOETEN worden vertaald naar typed adaptererrors en mogen het proces niet met een ongestructureerde stacktrace beëindigen. |
| FR-SCAN-008 | Must | De scan MOET nieuwe, gewijzigde, verwijderde en unresolved dependencies onderscheiden. |
| FR-SCAN-009 | Must | Scan MOET dezelfde bytes bij herhaling dezelfde identities geven. |
| FR-SCAN-010 | Must | Scan MAG geen pluginbinary laden of uitvoeren. |
| FR-SCAN-011 | Should | Ongewijzigde files BEHOREN via de lokale index zonder volledige rehash te worden herkend. |
| FR-SCAN-012 | Must | Een cancellation request MOET de scan gecontroleerd stoppen zonder repository- of workspacewijziging. |

---

# 10. Functionele requirements — staging en status

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-STG-001 | Must | DAWVC MOET een lokale staging index onderhouden die niet als gedeelde projectstate wordt behandeld. |
| FR-STG-002 | Must | De primaire `.flp` en reeds gevolgde assets MOETEN bij commit hun actuele toestand automatisch meenemen. |
| FR-STG-003 | Must | Nieuwe ontdekte assets MOGEN niet stil aan een commit worden toegevoegd. |
| FR-STG-004 | Must | `dawvc add <path-or-dependency>` MOET nieuwe assets expliciet registreren en stagen. |
| FR-STG-005 | Must | `dawvc add --all` MOET alle bundelbare nieuwe dependencies stagen, maar Forbidden en ReferenceOnly content overslaan. |
| FR-STG-006 | Must | Voor `UserChoice`-content MOET `add` expliciete toestemming vragen of een non-interactieve policyflag vereisen. |
| FR-STG-007 | Must | `dawvc status` MOET wijzigingen groeperen als modified, added, removed, unresolved en policy-blocked. |
| FR-STG-008 | Must | Status MOET staged en unstaged/new discoveries afzonderlijk tonen. |
| FR-STG-009 | Must | Status MOET aangeven of een commit volledig reproduceerbaar, incomplete of opaque zal zijn. |
| FR-STG-010 | Should | Renames BEHOREN op contentidentity te worden herkend en niet als inhoudswijziging te worden gepresenteerd. |

---

# 11. Functionele requirements — object store

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-OBJ-001 | Must | Iedere blob MOET worden geïdentificeerd met BLAKE3 over de oorspronkelijke payloadbytes. |
| FR-OBJ-002 | Must | Identieke bytes MOETEN binnen een repository naar dezelfde blobidentity verwijzen. |
| FR-OBJ-003 | Must | Blobs MOETEN streaming worden geschreven en gehasht. |
| FR-OBJ-004 | Must | Een object MOET eerst volledig tijdelijk worden geschreven, geverifieerd en daarna atomisch worden gepubliceerd. |
| FR-OBJ-005 | Must | Een bestaand object met dezelfde identity MAG nooit worden overschreven met afwijkende bytes. |
| FR-OBJ-006 | Must | Objecten MOETEN een versieerbaar envelope bevatten met magic bytes, objecttype, envelopeversie, payloadlengte, compressiemode en payloadhash. |
| FR-OBJ-007 | Must | Compressiemode is in v0.1 altijd `None`. Readers MOETEN onbekende compressiemodes gecontroleerd weigeren. |
| FR-OBJ-008 | Must | Metadataobjecten MOETEN canonical UTF-8 JSON gebruiken met expliciete `schemaVersion`. |
| FR-OBJ-009 | Must | Tijdstempels, mtimes en lokale file IDs MOGEN niet meetellen in contentidentity. |
| FR-OBJ-010 | Must | Een crash tijdens objectwriting MAG alleen unreachable temporary/orphan objects achterlaten en geen bereikbare corrupte history. |

## 11.1 Minimale object-envelopevelden

```text
Magic
EnvelopeVersion
ObjectType
CompressionMode
Flags
PayloadLength
PayloadHash (BLAKE3)
Payload
```

De precieze byte-offsets worden in ADR-OBJ-001 tijdens de eerste implementatiefase vastgelegd. Deze keuze mag bovenstaande velden en invariants niet veranderen.

---

# 12. Functionele requirements — dependencies en portability

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-DEP-001 | Must | De dependencygraph MOET assets, plugins, plugincontent en environmentrequirements als afzonderlijke dependencysoorten modelleren. |
| FR-DEP-002 | Must | Iedere dependency MOET een stabiele identity, requirementstatus, source, provenance en portability policy bevatten. |
| FR-DEP-003 | Must | Samples en recordings MOETEN waar mogelijk via contenthash worden geïdentificeerd. |
| FR-DEP-004 | Must | Pluginidentity MOET minimaal vendor, product, pluginformat en eventueel native identifier bevatten. |
| FR-DEP-005 | Must | Een plugin-installatiepad MAG geen onderdeel zijn van pluginidentity. |
| FR-DEP-006 | Must | Pluginbinaries zijn standaard `ReferenceOnly`. |
| FR-DEP-007 | Must | Commerciële of onbekend gelicentieerde libraries zijn standaard `ReferenceOnly` of `UserChoice`. |
| FR-DEP-008 | Must | Eigen samples en recordings MOGEN `Bundle` zijn. |
| FR-DEP-009 | Must | Third-party plugincontent MOET `Unknown` of user-assisted kunnen blijven wanneer betrouwbare detectie ontbreekt. |
| FR-DEP-010 | Must | Iedere inferred dependency MOET metadata-provenance en confidence bevatten. |
| FR-DEP-011 | Must | Een nieuwe `Bundle`-dependency MOET expliciet via `add` worden geaccepteerd. |
| FR-DEP-012 | Must | Een verplichte gebundelde dependency waarvan de bytes ontbreken MOET de normale commit blokkeren. |
| FR-DEP-013 | Must | `--allow-incomplete` MOET een bewuste incomplete commit toestaan en de snapshot als incomplete markeren. |
| FR-DEP-014 | Must | Ontbrekende `ReferenceOnly`-requirements MOGEN een commit niet blokkeren, maar MOETEN in `doctor` zichtbaar zijn. |
| FR-DEP-015 | Must | De CLI MOET vóór bundling de portability policy en verwachte byteomvang tonen. |

---

# 13. Functionele requirements — commits en history

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-COM-001 | Must | `dawvc commit -m <message>` MOET een immutable snapshot en commit maken. |
| FR-COM-002 | Must | Een commit MOET de actuele primaire `.flp`, gevolgde assets, dependencygraph en environmentrequirements vastleggen. |
| FR-COM-003 | Must | Een commit zonder inhoudelijke wijziging MOET worden geweigerd, tenzij een expliciete toekomstige allow-emptyoptie wordt toegevoegd. |
| FR-COM-004 | Must | Een commitmessage MOET na trim minimaal één zichtbaar teken bevatten. |
| FR-COM-005 | Must | De commit-ID MOET deterministisch uit de canonieke commitinhoud worden afgeleid. |
| FR-COM-006 | Must | De branchref MOET pas na volledige object- en referencevalidatie atomisch worden verplaatst. |
| FR-COM-007 | Must | Een opaque artifact MOET kunnen worden gecommit zonder semantic manifest. |
| FR-COM-008 | Must | Een `Invalid` of `Suspicious` artifact MOET confirmation of `--allow-invalid-artifact` vereisen. |
| FR-COM-009 | Must | Non-interactieve uitvoering zonder benodigde override MOET met een voorspelbare non-zero exitcode stoppen. |
| FR-COM-010 | Must | `dawvc log` MOET ten minste commit-ID, parents, auteur, timestamp en message tonen. |
| FR-COM-011 | Must | History MOET bruikbaar blijven als een nieuwere adapter afgeleide metadata anders interpreteert. |

---

# 14. Functionele requirements — branches en switch

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-BRA-001 | Must | Een nieuwe repository MOET een branch `main` bevatten. |
| FR-BRA-002 | Must | `dawvc branch <name>` MOET een branch op de huidige commit maken. |
| FR-BRA-003 | Must | Branchnamen MOETEN worden gevalideerd tegen lege namen, traversal en ref-collisions. |
| FR-BRA-004 | Must | `dawvc switch <name>` MOET de gekozen branch en bijbehorende snapshot veilig uitchecken. |
| FR-BRA-005 | Must | Switch MOET dezelfde dirty-workspacebeveiliging als checkout toepassen. |
| FR-BRA-006 | Must | De MVP MAG geen native auto-merge aanbieden. |
| FR-BRA-007 | Must | Divergerende branches blijven onafhankelijk; combineren is buiten scope. |

---

# 15. Functionele requirements — bindings en cross-machine recovery

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-BND-001 | Must | Bindings MOETEN lokaal en buiten commits worden opgeslagen. |
| FR-BND-002 | Must | Een binding MOET dependency-ID, locator, methode, status en optionele verified hash bevatten. |
| FR-BND-003 | Must | Een assetbinding MAG alleen `Verified` zijn nadat de bytes tegen de verwachte hash zijn gecontroleerd. |
| FR-BND-004 | Must | Resolvervolgorde MOET deterministisch zijn: repository asset, verified binding, relative path, original path, library mapping, asset index, hash discovery, user selection, unresolved. |
| FR-BND-005 | Must | Een locatie met dezelfde filename maar afwijkende hash MOET `Mismatch` zijn. |
| FR-BND-006 | Must | De gebruiker MOET een dependency handmatig aan een lokaal bestand kunnen binden. |
| FR-BND-007 | Must | Een bestaande identieke asset op een ander pad MOET via hash als dezelfde dependency kunnen worden herkend. |
| FR-BND-008 | Must | Originele source paths MOGEN uitsluitend als diagnostische locator worden opgeslagen en niet als identity. |
| FR-BND-009 | Must | Lokale bindings MOGEN niet in gedeelde manifests of objectidentities lekken. |

---

# 16. Functionele requirements — checkout en recovery

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-CHK-001 | Must | `dawvc checkout <commit-or-branch>` MOET alle vereiste bereikbare objecten vóór materialisatie controleren. |
| FR-CHK-002 | Must | Checkout MOET objecthashes, artifacttree en aggregate hash verifiëren. |
| FR-CHK-003 | Must | Checkout MOET weigeren bij ontbrekende of corrupte vereiste repositoryobjecten. |
| FR-CHK-004 | Must | Checkout MOET het project eerst volledig in een staginglocatie op hetzelfde filesystem materialiseren. |
| FR-CHK-005 | Must | De stagingcandidate MOET opnieuw byte- en tree-exact worden geverifieerd. |
| FR-CHK-006 | Must | Alleen een volledig geverifieerde candidate MAG atomisch in de workspace worden geïnstalleerd. |
| FR-CHK-007 | Must | Een failure vóór publicatie MAG de bestaande workspace niet wijzigen. |
| FR-CHK-008 | Must | Een checkout met lokale wijzigingen MOET standaard worden geweigerd. |
| FR-CHK-009 | Must | `--restore-to <path>` MOET een snapshot naar een afzonderlijke lege of expliciet geaccepteerde bestemming herstellen. |
| FR-CHK-010 | Must | `--force` MOET vóór wijziging een complete recovery copy van conflicterende lokale projectdata maken. |
| FR-CHK-011 | Must | De recoverylocatie MOET in de CLI-output worden gemeld en mag niet door dezelfde operatie worden verwijderd. |
| FR-CHK-012 | Must | Path traversal, absolute manifestpaden, duplicate normalized paths en onverwachte symlinks MOETEN vóór writing worden geweigerd. |
| FR-CHK-013 | Must | DAWVC MAG tijdens checkout de `.flp` niet herschrijven. |
| FR-CHK-014 | Must | Bundled assets MOETEN naar een door DAWVC beheerde projectassetroot worden gematerialiseerd. |
| FR-CHK-015 | Must | Checkout MOET een post-checkout report geven met artifacthealth, unresolved requirements en eventuele handmatige FL Studio-stappen. |

---

# 17. Functionele requirements — FL Studio-integratie

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-FLP-001 | Must | De v0.1-adapter MOET FL Studio 2026.x `.flp`-bestanden als `SingleFileArtifact` kunnen identificeren. |
| FR-FLP-002 | Must | De adapter MOET read-only werken en MAG geen writehandle naar de bron ontvangen. |
| FR-FLP-003 | Must | De adapter MOET projectformat/version metadata extraheren wanneer betrouwbaar beschikbaar. |
| FR-FLP-004 | Must | De adapter MOET sample- en recordingreferences best-effort extraheren. |
| FR-FLP-005 | Must | De adapter MOET plugin-identiteiten best-effort extraheren zonder plugins te laden. |
| FR-FLP-006 | Must | Niet-detecteerbare plugincontent MOET als `Unknown` of via user input kunnen worden geregistreerd. |
| FR-FLP-007 | Must | Iedere extractie MOET source, confidence, timestamp en adapterversie vastleggen. |
| FR-FLP-008 | Must | Onbekende nieuwere FLP-versies MOETEN zonder semantische parsing opaque kunnen worden opgeslagen en hersteld. |
| FR-FLP-009 | Must | `NativeWrite`, `NativeRoundTripValidation` en `NativeMerge` MOETEN voor de v0.1-adapter uitgeschakeld zijn. |
| FR-FLP-010 | Must | De adapter MAG FL Studio niet starten voor normale detectie, scan of validatie. |
| FR-FLP-011 | Must | De adapter MAG third-party plugins niet initialiseren. |
| FR-FLP-012 | Must | De feasibility spike MOET vóór definitieve adapterbouw een ondersteunde fixturematrix en aantoonbare parsergrenzen opleveren. |

## 17.1 Sample-resolutiongrens

FL Studio zoekt tijdens projectloading onder andere in geconfigureerde Browser extra search folders. DAWVC v0.1 wijzigt deze instellingen niet automatisch. Zie de officiële [FL Studio File Search & Browser Settings](https://www.image-line.com/fl-studio-learning/fl-studio-online-manual/html/envsettings_files.htm).

- **FR-FLP-013:** `doctor` MOET de lokale managed assetroot tonen die de gebruiker aan FL Studio kan toevoegen.
- **FR-FLP-014:** `doctor` MOET duidelijk aangeven wanneer handmatige relinking waarschijnlijk nodig is.
- **FR-FLP-015:** MVP-acceptatie vereist dat alle gebundelde bytes aanwezig en verifieerbaar zijn; automatisch path-resolven door FL Studio is geen harde v0.1-garantie.

---

# 18. Functionele requirements — doctor

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-DOC-001 | Must | `dawvc doctor` MOET artifact-, dependency- en environmenthealth afzonderlijk evalueren. |
| FR-DOC-002 | Must | Doctor MOET de geïnstalleerde FL Studio-versie vergelijken met de requirement wanneer detecteerbaar. |
| FR-DOC-003 | Must | Doctor MOET pluginrequirements als satisfied, version-mismatch, missing of unknown rapporteren. |
| FR-DOC-004 | Must | Doctor MOET assetbindings als verified, unresolved of mismatch rapporteren. |
| FR-DOC-005 | Must | Doctor MOET `Bundle`, `ReferenceOnly`, `UserChoice`, `Forbidden` en `Unknown` zichtbaar onderscheiden. |
| FR-DOC-006 | Must | Doctor MOET blocking issues en warnings afzonderlijk tonen. |
| FR-DOC-007 | Must | Doctor MOET concrete herstelacties geven waar die veilig bekend zijn. |
| FR-DOC-008 | Must | Doctor MAG geen reproductie garanderen wanneer ReferenceOnly of Unknown requirements niet volledig verifieerbaar zijn. |
| FR-DOC-009 | Must | Doctor MOET een machineleesbare JSON-output kunnen produceren via `--json`. |
| FR-DOC-010 | Must | De exitcode MOET aangeven of blocking issues bestaan. |

---

# 19. Functionele requirements — fsck

| ID | Prioriteit | Requirement |
|---|---|---|
| FR-FSC-001 | Must | `dawvc fsck` MOET standaard alle bereikbare repositoryobjecten en references controleren. |
| FR-FSC-002 | Must | `fsck` MOET objecthashes, commitparents, snapshotrefs, manifests, artifacttrees en refs controleren. |
| FR-FSC-003 | Must | `fsck --artifacts` MOET aanvullende native artifactvalidatie uitvoeren wanneer de adapter die ondersteunt. |
| FR-FSC-004 | Must | Repositorycorruptie en native artifact-invaliditeit MOETEN als verschillende health domains worden gerapporteerd. |
| FR-FSC-005 | Must | Een adapterparsefout MAG niet als `ObjectHashMismatch` worden gerapporteerd. |
| FR-FSC-006 | Must | `fsck` MOET machineleesbare JSON-output ondersteunen. |
| FR-FSC-007 | Must | `fsck` MAG in MVP geen automatische reparatie uitvoeren. |
| FR-FSC-008 | Must | Onreachable objects MOGEN als warning worden gerapporteerd maar zijn niet automatisch corruptie. |

---

# 20. CLI- en foutgedrag

## 20.1 Algemene CLI-requirements

- **FR-CLI-001:** Ieder commando MOET `--help` ondersteunen.
- **FR-CLI-002:** De rootcommand MOET `--version` ondersteunen.
- **FR-CLI-003:** Interactieve prompts MOGEN alleen worden gebruikt wanneer stdin interactief is.
- **FR-CLI-004:** Iedere promptbare actie MOET een non-interactieve flag of gecontroleerde failuremode hebben.
- **FR-CLI-005:** Normale output MOET menselijk leesbaar zijn; relevante diagnosecommando’s ondersteunen `--json`.
- **FR-CLI-006:** Secrets, volledige credentials en potentieel gevoelige content MOGEN niet in logs verschijnen.
- **FR-CLI-007:** `--verbose` MOET provenance en beslissingen kunnen tonen zonder stacktraces als standaardgebruikersoutput.
- **FR-CLI-008:** `--no-color` MOET kleurcodes uitschakelen.
- **FR-CLI-009:** Cancellation via Ctrl+C MOET gecontroleerd afhandelen en tijdelijke state opruimen of recovery-informatie tonen.

## 20.2 Exitcodecategorieën

| Exitcode | Categorie |
|---:|---|
| 0 | Succes; geen blocking issues. |
| 1 | Onverwachte algemene applicatiefout. |
| 2 | Ongeldige CLI-input of configuratie. |
| 3 | Repository/workspace niet gevonden of ongeldig. |
| 4 | Repository/object-integriteitsfout. |
| 5 | Incomplete of unresolved required dependencies. |
| 6 | Adapter-, format- of native validation failure. |
| 7 | Checkout/switch conflict of dirty workspace. |
| 8 | Filesystem-, permission- of atomic-installfout. |
| 9 | Operatie geannuleerd. |

- **FR-ERR-001:** Typed application errors MOETEN deterministisch naar één exitcodecategorie mappen.
- **FR-ERR-002:** Een bekende fout MOET een korte summary, oorzaak en mogelijke actie tonen.
- **FR-ERR-003:** Onverwachte fouten MOETEN een correlation ID krijgen voor lokale logs.
- **FR-ERR-004:** JSON-output MOET stable error codes bevatten en niet afhankelijk zijn van gelokaliseerde tekst.

---

# 21. Niet-functionele requirements — integriteit en betrouwbaarheid

| ID | Requirement |
|---|---|
| NFR-INT-001 | Een uitgecheckte `.flp` MOET byte-identiek zijn aan de gecommitte payload. |
| NFR-INT-002 | Een directory/package artifact MOET door de core deterministisch representabel zijn, ook al wordt het in v0.1 niet functioneel gebruikt. |
| NFR-INT-003 | Iedere branchrefupdate MOET atomisch plaatsvinden. |
| NFR-INT-004 | Iedere checkoutinstallatie MOET staged en atomic of aantoonbaar rollback-safe zijn. |
| NFR-INT-005 | Fault injection vóór publicatie MAG de vorige bereikbare state niet beschadigen. |
| NFR-INT-006 | Herhaald committen van dezelfde canonieke state MOET dezelfde snapshotcontent opleveren. |
| NFR-INT-007 | Repositoryobjects MOETEN bij read opnieuw op lengte en hash worden gevalideerd wanneer integrity verification is aangevraagd. |
| NFR-INT-008 | Opaque versioning MOET zonder adapter beschikbaar blijven. |

---

# 22. Niet-functionele requirements — performance en schaal

| ID | Requirement |
|---|---|
| NFR-PERF-001 | MVP MOET repositories met 5.000 gevolgde assets ondersteunen. |
| NFR-PERF-002 | MVP MOET ten minste 100 GB gebundelde content kunnen verwerken zonder volledige datasets in memory te laden. |
| NFR-PERF-003 | Normaal piekgeheugengebruik BEHOORT onder circa 512 MB te blijven bij de referentiefixture. |
| NFR-PERF-004 | Een unchanged `status` BEHOORT binnen 2 seconden te voltooien op een lokale SSD na een warme eerste scan. |
| NFR-PERF-005 | Initiële hashing MOET streaming en disk-throughput-bound zijn; er geldt geen vaste wall-clockgarantie. |
| NFR-PERF-006 | Hashing MOET bounded concurrency gebruiken en de machine responsief houden. |
| NFR-PERF-007 | Reeds bekende ongewijzigde content BEHOORT via file identity, size, timestamps en cache zonder rehash te worden geaccepteerd. Verdachte wijzigingen MOETEN worden gehasht. |
| NFR-PERF-008 | CLI-startup zonder repositoryscan BEHOORT binnen 500 ms op de referentiemachine te liggen. |

---

# 23. Niet-functionele requirements — security en privacy

| ID | Requirement |
|---|---|
| NFR-SEC-001 | Native projectdata en manifests MOETEN als onbetrouwbare input worden behandeld. |
| NFR-SEC-002 | Parsers MOETEN bounds checks, cancellation en resource limits toepassen. |
| NFR-SEC-003 | Path traversal en symlink escape MOETEN vóór materialisatie worden geblokkeerd. |
| NFR-SEC-004 | DAWVC MAG geen pluginbinary uitvoeren, laden of installeren. |
| NFR-SEC-005 | DAWVC MAG geen netwerkverbinding vereisen voor v0.1-functionaliteit. |
| NFR-SEC-006 | MVP MAG geen telemetry verzenden. |
| NFR-SEC-007 | Logs MOGEN geen credentials bevatten en BEHOREN lokale origin paths op normaal loglevel te redacteren. |
| NFR-SEC-008 | Temp- en recoverydirectories MOETEN uitsluitend onder gecontroleerde projectlocaties worden aangemaakt. |
| NFR-SEC-009 | Archive-extractioncode MAG niet bereikbaar zijn vanuit v0.1 `.flp`-flows, tenzij de archivefeature later expliciet wordt geactiveerd en beveiligd. |

---

# 24. Niet-functionele requirements — compatibility en maintainability

| ID | Requirement |
|---|---|
| NFR-CMP-001 | De public technical preview ondersteunt officieel Windows 11 x64. |
| NFR-CMP-002 | De FL Studio-adapter wordt gevalideerd tegen FL Studio 2026.x-fixtures. |
| NFR-CMP-003 | Oudere, nieuwere of onbekende FLP-versies BEHOREN opaque versioneerbaar te blijven. |
| NFR-CMP-004 | Persistente JSON-objecten MOETEN vanaf de eerste release een `schemaVersion` bevatten. |
| NFR-CMP-005 | Onbekende toekomstige velden BEHOREN bij read/write round-trips behouden te blijven waar het model herschrijfbaar is. |
| NFR-MNT-001 | Domain MAG niet refereren aan Infrastructure, CLI of FL Studio-adapterprojecten. |
| NFR-MNT-002 | CLI en toekomstige UI MOETEN dezelfde Application use-cases kunnen gebruiken. |
| NFR-MNT-003 | Iedere adaptercapability MOET afzonderlijk testbaar en expliciet gedeclareerd zijn. |
| NFR-MNT-004 | Publieke JSON-output en persisted schemas vereisen compatibilitytests. |
| NFR-MNT-005 | Belangrijke format- en persistencebesluiten MOETEN als ADR worden vastgelegd. |

---

# 25. Packaging, licentie en documentatie

| ID | Requirement |
|---|---|
| NFR-REL-001 | De release MOET als self-contained `win-x64` executable/ZIP beschikbaar zijn. |
| NFR-REL-002 | Een gebruiker MAG geen aparte .NET-runtime hoeven installeren. |
| NFR-REL-003 | De repository MOET een MIT-licentiebestand bevatten. |
| NFR-REL-004 | De publieke repository MOET een README met installatie, quick start, beperkingen en recoveryinstructies bevatten. |
| NFR-REL-005 | De release MOET checksums bevatten voor distributieartefacts. |
| NFR-REL-006 | Testfixtures MOGEN alleen zelfgemaakte of vrij distribueerbare projectcontent bevatten. |
| NFR-REL-007 | De documentatie MOET expliciet vermelden dat pluginbinaries en commerciële libraries niet worden gebundeld. |
| NFR-REL-008 | De documentatie MOET expliciet vermelden dat handmatige FL Studio search-pathconfiguratie of relinking nodig kan zijn. |

---

# 26. Acceptatiescenario’s

## AC-001 — Walking skeleton

**Gerelateerde requirements:** FR-REP-001, FR-OBJ-001, FR-COM-001, FR-COM-010, FR-CHK-001, NFR-INT-001.

```gherkin
Given een geldige nieuwe repository met één primaire .flp
When de gebruiker init, commit en log uitvoert
And de commit naar een lege restore-directory uitcheckt
Then bevat log de nieuwe commit
And is de herstelde .flp byte-identiek aan het origineel
And is het originele projectbestand nooit door DAWVC gewijzigd
```

## AC-002 — Deduplicatie

**Gerelateerde requirements:** FR-OBJ-001, FR-OBJ-002, FR-BND-007.

```gherkin
Given twee lokale paden met exact dezelfde assetbytes
When beide als dependency worden geregistreerd
Then verwijzen zij naar dezelfde blobidentity
And worden de bytes slechts eenmaal in de object store opgeslagen
```

## AC-003 — Gewijzigde content met dezelfde naam

**Gerelateerde requirements:** FR-DEP-003, FR-BND-005.

```gherkin
Given een gevolgde asset kick.wav
When de bytes veranderen maar de filename gelijk blijft
Then wordt een nieuwe contentidentity gemaakt
And wordt de oude binding niet als verified hergebruikt
```

## AC-004 — Nieuwe dependency vereist add

**Gerelateerde requirements:** FR-STG-003, FR-STG-004, FR-DEP-011.

```gherkin
Given een scan die een nieuwe bundelbare sample ontdekt
When de gebruiker direct commit uitvoert
Then wordt de sample niet stil toegevoegd
And rapporteert commit dat expliciete staging nodig is
When de gebruiker dawvc add uitvoert en opnieuw commit
Then wordt de sample gebundeld
```

## AC-005 — Incomplete dependency

**Gerelateerde requirements:** FR-DEP-012, FR-DEP-013, FR-COM-009.

```gherkin
Given een ontbrekende verplichte Bundle dependency
When de gebruiker een normale commit uitvoert
Then stopt het commando met exitcode 5
When de gebruiker commit --allow-incomplete uitvoert
Then wordt een incomplete snapshot gemaakt
And toont doctor de ontbrekende dependency als blocking issue
```

## AC-006 — ReferenceOnly plugin ontbreekt

**Gerelateerde requirements:** FR-DEP-006, FR-DEP-014, FR-DOC-003.

```gherkin
Given een project dat een niet-geïnstalleerde commerciële plugin vereist
When de gebruiker commit uitvoert
Then wordt de pluginbinary niet gebundeld
And slaagt de commit
And rapporteert doctor de plugin als missing ReferenceOnly requirement
```

## AC-007 — Onbekende FLP-versie

**Gerelateerde requirements:** FR-SCAN-004, FR-COM-007, FR-FLP-008, NFR-INT-008.

```gherkin
Given een herkenbare maar niet-ondersteunde FLP-versie
When de gebruiker scan en commit uitvoert
Then wordt geen semantische parsing afgedwongen
And kan het artifact opaque worden gecommit
And kan het later byte-identiek worden hersteld
```

## AC-008 — Ongeldige FLP

**Gerelateerde requirements:** FR-SCAN-006, FR-COM-008, FR-FSC-004.

```gherkin
Given een truncated FLP die een bekende harde formatinvariant schendt
When de gebruiker een normale commit uitvoert
Then vereist DAWVC expliciete bevestiging of --allow-invalid-artifact
And blijft repository-integriteit afzonderlijk van native artifacthealth
```

## AC-009 — Dirty checkout

**Gerelateerde requirements:** FR-CHK-008, FR-CHK-009, FR-CHK-010.

```gherkin
Given een workspace met niet-gecommitte wijzigingen
When de gebruiker een normale checkout uitvoert
Then stopt checkout met exitcode 7
And blijven alle lokale bytes onaangeraakt
When de gebruiker --restore-to gebruikt
Then wordt de snapshot in de gekozen aparte directory hersteld
```

## AC-010 — Geforceerde checkout en recovery

**Gerelateerde requirements:** FR-CHK-010, FR-CHK-011, INV-012.

```gherkin
Given een dirty workspace
When de gebruiker checkout --force uitvoert
Then wordt eerst een volledige recovery copy gemaakt
And wordt de recoverylocatie gerapporteerd
And wordt pas daarna de geverifieerde checkout gepubliceerd
```

## AC-011 — Crash tijdens checkout

**Gerelateerde requirements:** FR-CHK-004 tot en met FR-CHK-007, NFR-INT-005.

```gherkin
Given een bestaande geldige workspace
When het proces tijdens staged materialization wordt afgebroken
Then blijft de bestaande workspace byte-exact onaangeraakt
And wordt de candidate niet als succesvolle checkout beschouwd
```

## AC-012 — Repositorycorruptie versus parserfout

**Gerelateerde requirements:** FR-FSC-004, FR-FSC-005.

```gherkin
Given een blob met geldige opgeslagen bytes die de adapter niet kan parsen
When fsck wordt uitgevoerd
Then rapporteert repository integrity de blob als intact
And rapporteert artifact integrity de adapterfailure afzonderlijk
And wordt geen ObjectHashMismatch gerapporteerd
```

## AC-013 — Cross-machine binding

**Gerelateerde requirements:** FR-BND-003, FR-BND-007, FR-FLP-013 tot en met FR-FLP-015.

```gherkin
Given een repository die op machine A is gemaakt
And machine B heeft een andere samplefolderstructuur
When machine B checkout en doctor uitvoert
Then zijn alle gebundelde bytes aanwezig
And worden lokale identieke assets via hash herkend
And toont doctor de managed assetroot en eventuele handmatige FL Studio-stappen
```

## AC-014 — Performancebaseline

**Gerelateerde requirements:** NFR-PERF-001 tot en met NFR-PERF-007.

```gherkin
Given de referentierepository met 5.000 ongewijzigde assets op een lokale SSD
And een warme geldige index
When dawvc status wordt uitgevoerd
Then voltooit het commando bij voorkeur binnen 2 seconden
And blijft normaal geheugengebruik onder circa 512 MB
```

---

# 27. MVP release gate

MVP v0.1 mag als public technical preview worden uitgebracht wanneer:

- alle Must-requirements zijn geïmplementeerd en aantoonbaar getest;
- AC-001 tot en met AC-013 volledig automatisch of reproduceerbaar handmatig slagen;
- AC-014 is gemeten en eventuele afwijking openbaar is gedocumenteerd;
- alle supported FL Studio 2026.x-fixtures slagen;
- unknown en invalid fixtures gecontroleerd degraderen;
- fault-injectiontests aantonen dat commit en checkout geen bereikbare corrupte state publiceren;
- de publicatie self-contained op een schone Windows 11 x64-machine start zonder geïnstalleerde .NET-runtime;
- README, MIT-licentie, securitybeperkingen en known limitations aanwezig zijn;
- de distributiechecksums kloppen;
- er geen commerciële of niet-herdistribueerbare bytes in testfixtures of releaseartefacts zitten.

---

# 28. Traceability naar Technical Design

| Requirementgroep | Belangrijkste Technical Design-secties |
|---|---|
| Repository/config | 5, 9, 10, 22–26 |
| Artifact/object store | 11, 23–26, 42–46 |
| Dependencies/bindings | 12–14, 21, 29–30 |
| Adapter/FLP | 17–18, 53–54, 60 |
| Checkout/recovery | 27, 45–46 |
| Branching | 31–33 |
| Security/licensing | 48–50 |
| Errors/observability | 51–52 |
| Testing/acceptance | 53–56, 64 |

---

# 29. Uitgestelde requirements

De volgende onderwerpen worden pas in een latere requirementsversie normatief gemaakt:

- remotes en netwerkprotocol;
- multi-user authorization en locking;
- desktopinterface;
- native semantic diff/merge;
- adapter-native writing;
- tweede DAW-adapter;
- directory-, package- en archive-artifacts in productflows;
- collaboration snapshots en cross-DAW exchange;
- objectcompressie, chunking en partial checkout;
- telemetry, crash reporting en auto-update.

Deze onderwerpen mogen de implementatie van MVP v0.1 niet blokkeren en mogen niet via impliciete scope-uitbreiding worden toegevoegd.
