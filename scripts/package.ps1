<#
.SYNOPSIS
    Packages DAWVC as a self-contained Windows x64 distribution ZIP with SHA-256 checksums.

.PARAMETER Version
    The version tag to package (default: "0.1.0-preview.1").

.PARAMETER OutputDir
    Directory where release artifacts will be placed (default: "artifacts").
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0-preview.1",
    [string]$OutputDir = "artifacts"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ArtifactsDir = Join-Path $RepoRoot $OutputDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " DAWVC Packaging: v$Version (win-x64)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Prepare output folder
if (Test-Path $ArtifactsDir) {
    Remove-Item -Path $ArtifactsDir -Recurse -Force
}
New-Item -Path $ArtifactsDir -ItemType Directory -Force | Out-Null

# 2. Publish self-contained executable
$CliProject = Join-Path $RepoRoot "src/DawVcs.Cli/DawVcs.Cli.csproj"
Write-Host "`n[1/4] Publishing self-contained win-x64 binary..." -ForegroundColor Yellow
$PublishDir = Join-Path $ArtifactsDir "staging"

& dotnet publish $CliProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=none `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 3. Copy license and readme
Write-Host "`n[2/4] Staging documentation and license..." -ForegroundColor Yellow
Copy-Item (Join-Path $RepoRoot "LICENSE") -Destination $PublishDir
Copy-Item (Join-Path $RepoRoot "README.md") -Destination $PublishDir

# Remove any debug symbols (.pdb) from release bundle
Get-ChildItem -Path $PublishDir -Filter "*.pdb" -Recurse | Remove-Item -Force

# 4. Create ZIP archive
$ZipFileName = "dawvc-v$Version-win-x64.zip"
$ZipFilePath = Join-Path $ArtifactsDir $ZipFileName
Write-Host "`n[3/4] Creating distribution ZIP: $ZipFileName..." -ForegroundColor Yellow
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipFilePath -CompressionLevel Optimal

# Clean up staging directory
Remove-Item -Path $PublishDir -Recurse -Force

# 5. Compute SHA-256 checksums
Write-Host "`n[4/4] Computing SHA-256 checksum..." -ForegroundColor Yellow
$FileHash = (Get-FileHash -Path $ZipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
$ChecksumLine = "$FileHash  $ZipFileName"
$ChecksumFilePath = Join-Path $ArtifactsDir "SHA256SUMS.txt"
Set-Content -Path $ChecksumFilePath -Value $ChecksumLine -Encoding utf8

Write-Host "`nPackaging complete!" -ForegroundColor Green
Write-Host "  ZIP:      $ZipFilePath"
Write-Host "  SHA256:   $FileHash"
Write-Host "  Checksum: $ChecksumFilePath"
