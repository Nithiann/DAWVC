## Samenvatting van de wijziging

<!-- Geef een korte beschrijving van wat deze PR toevoegt, fixt of wijzigt. -->

## Gekoppelde Requirements & Issues

- Requirements: `FR-...`, `NFR-...`, `INV-...`, `AC-...`
- Werkpakket: `WP-...` (bv. WP-02)
- Issue / Taak: `IMP-...`

## Type Wijziging

- [ ] Nieuwe functionaliteit (`feat`)
- [ ] Bugfix (`fix`)
- [ ] Refactoring / Architecture (`refactor`)
- [ ] Testsuite uitbreiding (`test`)
- [ ] Documentatie of ADR (`docs`)

## Checklist Kwaliteit & DoD

- [ ] Build slaagt zonder warnings (`dotnet build -c Release`)
- [ ] Alle tests slagen (`dotnet test -c Release`)
- [ ] Formatter controleert zonder wijzigingen (`dotnet format --verify-no-changes`)
- [ ] Geen commerciële of niet-herdistribueerbare audio/VST assets toegevoegd
- [ ] Typed errors gebruikt voor failure cases
- [ ] Geen architectuurgrenzen overschreden (gevalideerd via architecture tests)
- [ ] ADR bijgewerkt indien persisted dataformat of hashschema is gewijzigd
