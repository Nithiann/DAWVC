# Bijdragen aan DAWVC

Bedankt voor je interesse in het bijdragen aan **DAWVC**! Dit document beschrijft de ontwikkelingsstandaarden, git-conventies en kwaliteitsrichtlijnen die binnen dit project worden gehanteerd.

---

## 1. Algemene Uitgangspunten

- **Normatieve Documentatie**: Alle code moet voldoen aan de specificaties in [Docs/Technical Design.md](Docs/Technical%20Design.md) en [Docs/MVP Requirements.md](Docs/MVP%20Requirements.md).
- **Scope Discipline**: Voeg geen functies toe die buiten de MVP v0.1-scope vallen zonder een goedgekeurd Architecture Decision Record (ADR) of gewijzigde requirement.
- **Geen Commerciële Content**: Testfixtures mogen NOOIT beschermde, commerciële sample-bibliotheken of gelicenseerde VST-data bevatten. Gebruik uitsluitend zelfgemaakte of expliciet rechtenvrije audio-assets.

---

## 2. Git & Branching Conventies

- `main` is de beschermde productietak en moet altijd releasable zijn.
- Werk in feature-branches met een duidelijke prefix:
  - `feat/issue-123-opaque-commit`
  - `fix/issue-456-atomic-rename`
  - `test/issue-789-flp-fixtures`
  - `docs/issue-012-adr-update`
- Commitberichten volgen [Conventional Commits](https://www.conventionalcommits.org/):
  - `feat(domain): add AggregateContentHash calculation`
  - `fix(infra): prevent partial blob write on process termination`
  - `test(flstudio): add regression test for truncated flp header`
  - `docs(adr): add ADR-OBJ-001 for envelope format`

---

## 3. Definition of Ready & Definition of Done

### Definition of Ready (DoR)
Een taak mag worden gestart wanneer:
1. Gekoppelde requirement-ID's bekend zijn (`FR-...`, `NFR-...`, `INV-...`).
2. Input, output en foutsituaties helder zijn gedefinieerd.
3. Benodigde testfixtures gereed of gepland zijn.

### Definition of Done (DoD)
Een pull request kan pas worden gemerged wanneer:
1. Alle unit-, integratie- en architectuurtests slagen (`dotnet test`).
2. De build slaagt met 0 errors en 0 warnings (`TreatWarningsAsErrors=true`).
3. Code formatting voldoet aan `.editorconfig` (`dotnet format --verify-no-changes`).
4. Foutafhandeling gebruikmaakt van typed errors (geen ongetypte generics).
5. Wijzigingen in persisted formats of architectuurgrenzen zijn vastgelegd in een ADR.

---

## 4. Ontwikkelomgeving & Testen

```powershell
# Restore en build
dotnet build -c Release

# Uitvoeren van alle tests
dotnet test -c Release --logger "console;verbosity=normal"

# Formatter controleren
dotnet format --verify-no-changes
```
