# ADR-IO-001: Windows Atomic File Replace & Staging Primitives

- **Status:** Geaccepteerd
- **Datum:** 2026-09-13
- **Auteurs:** DAWVC Contributors
- **Gerelateerde Requirements:** FR-OBJ-004, FR-OBJ-010, NFR-INT-004, NFR-INT-005
- **Werkpakket:** WP-01 (Spike D) / WP-02 / WP-07

---

## Context & Probleemdefinitie

Bij het opslaan van immutable objecten (`.dawvc/objects/`), bij het updaten van referenties (`HEAD`, branches), en bij het uitrollen van projectbestanden (`checkout`) mag een crash, schijffout of geforceerde procesbeëindiging nooit resulteren in een half geschreven of corrupt bestand op de doellocatie (`FR-OBJ-010`).
Op Windows NTFS moeten bestandswijzigingen daarom atomisch worden uitgevoerd via een *safe-write staging* mechanisme.

## Overwogen Opties

1. **Direct In-Place Write (`FileStream` direct naar het doelpad):**
   - Als het proces crasht halverwege het schrijven, blijft een corrupt bestand achter.
   - Voldoet niet aan integriteitsinvariants `INV-008` en `FR-OBJ-004`.
2. **Schrijven naar `%TEMP%` en vervolgens verplaatsen naar repository:**
   - Als `%TEMP%` zich op een ander volume bevindt dan het project (bijvoorbeeld C: vs D:), is `File.Move` een copy-and-delete operatie en géén atomische directory entry swap.
3. **Same-Directory Temporary File met Flush to Disk & Atomic Replace (`AtomicFileWriter`):**
   - Tijdelijk bestand wordt aangemaakt in exact dezelfde map als het doelbestand (`.tmp_<name>_<guid>`). Hierdoor is gegarandeerd dat bron en doel zich op hetzelfde filesystem/NTFS-volume bevinden.
   - Na afronding van de payload wordt `FileStream.Flush(flushToDisk: true)` aangeroepen om fysieke persistentie op de schijfcontroller te garanderen.
   - Vervolgens wordt `File.Move(tempPath, destinationPath, overwrite: true)` uitgevoerd, wat onder Windows gebruikmaakt van de kernel primitive `MoveFileExW` met de vlag `MOVEFILE_REPLACE_EXISTING`.

## Besluit

We kiezen voor optie 3: **`AtomicFileWriter` met same-directory staging en `Flush(flushToDisk: true)`**.

### Werkwijze
1. Bepaal de absolute map van het doelpad en zorg dat de map bestaat;
2. Maak een uniek verborgen stagingbestand in die map: `.tmp_<filename>_<guid:N>`;
3. Schrijf de volledige stream of envelope naar dit stagingbestand;
4. Roep `Flush(flushToDisk: true)` aan vóór het sluiten van de handle;
5. Vervang/publiceer het bestand atomisch via `File.Move(temp, target, overwrite: true)`;
6. Bij een exceptie tijdens stap 2 t/m 4 wordt het tijdelijke bestand direct opgeruimd via een cleanup block. Bij een eventuele stroomuitval of kill blijft hoogstens een ongeïndexeerd `.tmp_*` weesbestand achter dat veilig door `dawvc doctor` of `fsck` kan worden geschoond.

## Gevolgen

### Positieve gevolgen
- **Geen corruptie bij crashes:** Het doelbestand behoudt gegarandeerd zijn vorige geldige inhoud totdat de nieuwe data volledig en correct is geflusht en atomisch verplaatst.
- **Volume-garantie:** Door dezelfde map te gebruiken is er nooit sprake van cross-volume operaties.
- **Robuustheid:** Getest met foutinjectie (abrupt afbreken tijdens de write).

### Negatieve gevolgen of risico's
- Vereist schrijfrechten in de doelmap om tijdelijke bestanden aan te maken (is inherent al nodig voor het doelbestand zelf).

## Verificatie & Bewijslast

De testsuite in `tests/DawVcs.Infrastructure.Tests/FileSystem/AtomicFileWriterTests.cs` bewijst:
- Correct aanmaken van nieuwe bestanden;
- Atomische vervanging van bestaande bestanden;
- Behoud van de originele bestandsinhoud wanneer halverwege het schrijven een `IOException` of crash wordt gesimuleerd;
- Schoon opruimen van stagingbestanden bij falen.
