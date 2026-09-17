# Git History Cleanup Guide: Purging Historical FLP Backups

## 1. Context & Purpose
Eerdere commits in de repository bevatten nog echte FLP-backupbestanden (`*.flp`). Hoewel de actieve werkdirectory (working tree) is opgeschoond, blijven deze binaire bestanden aanwezig in de Git-objectdatabase (`.git/objects`) en de commit-historie.

Als deze projectbestanden niet publiek in de geschiedenis mogen blijven staan, moet de Git-geschiedenis worden herschreven.

> [!CAUTION]
> **Belangrijke waarschuwing:**
> Het herschrijven van Git-geschiedenis verandert alle commit-hashes (SHA-1). 
> - Bestaande forks, clones en open pull requests worden ontkoppeld en moeten opnieuw gecloned worden.
> - Na het herschrijven is een `git push --force --all` en `git push --force --tags` vereist.
> - Zorg dat alle teamleden vooraf op de hoogte zijn en hun lokale wijzigingen hebben gecommit of gesaved.

---

## 2. Aanbevolen Procedure met `git-filter-repo`

De officiële Git-documentatie raadt [`git-filter-repo`](https://github.com/newren/git-filter-repo) aan (in plaats van het verouderde `git filter-branch`).

### Stap 1: Maak een volledige backup
Maak altijd een mirror backup van de repository voordat u de geschiedenis aanpast:

```bash
cd ..
git clone --mirror c:\Users\Voss\Development\tools\DAWVC DAWVC_backup.git
```

### Stap 2: Installeer `git-filter-repo`
Indien nog niet geïnstalleerd:
```bash
pip install git-filter-repo
```

### Stap 3: Analyseer de aanwezigheid van FLP-bestanden in de geschiedenis
Controleer welke FLP-bestanden voorkomen in de geschiedenis:
```bash
git log --all --name-only --format="" -- "*.flp" | sort -u
```

### Stap 4: Filter de geschiedenis
Verwijder alle FLP-backups uit alle branches en tags.
*Let op:* Als specifieke kleine test-fixtures (zoals in `tests/`) behouden moeten blijven, specificeer dan alleen de backup-mappen. Indien álle historische `.flp`-bestanden gewist moeten worden:

```bash
git filter-repo --invert-paths --path-glob "*.flp" --force
```

Of specifiek voor fixture/backup-paden:
```bash
git filter-repo --invert-paths --path-glob "tests/**/fixtures/**/*.flp" --force
```

### Stap 5: Verifieer de opschoning
Controleer of er nog FLP-bestanden in de geschiedenis voorkomen:
```bash
git log --all --name-only --format="" -- "*.flp"
```
*(Dit mag geen output meer opleveren)*

Controleer de repository-grootte en forceer garbage collection:
```bash
git reflog expire --expire=now --all
git gc --prune=now --aggressive
```

### Stap 6: Force-push naar remote
Nadat u lokaal hebt geverifieerd dat de code en tests nog steeds werken:

```bash
git remote add origin <remote-url> # git-filter-repo verwijdert remotes ter bescherming
git push origin --force --all
git push origin --force --tags
```
