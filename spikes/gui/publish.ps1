param([string]$Output = (Join-Path $PSScriptRoot '../../artifacts/gui-spikes'))
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $Output | Out-Null
$report = @()
foreach ($name in @('Avalonia', 'Wpf', 'Photino')) {
    $project = Join-Path $PSScriptRoot "$name/$name.csproj"
    $manifests = @()
    foreach ($pass in @(1, 2)) {
        # Fresh unique output; never recursively delete a caller-supplied directory.
        $destination = Join-Path $Output "$name-$pass-$([guid]::NewGuid().ToString('N'))"
        $timer = [Diagnostics.Stopwatch]::StartNew()
        & dotnet publish $project -c Release -r win-x64 --self-contained true `
            -p:PublishTrimmed=false -p:PublishSingleFile=false -o $destination
        if ($LASTEXITCODE -ne 0) { throw "$name publish failed" }
        $hashes = @(Get-ChildItem $destination -Recurse -File | Where-Object Extension -ne '.pdb' | ForEach-Object {
            [pscustomobject]@{ path = [IO.Path]::GetRelativePath($destination, $_.FullName); sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
        } | Sort-Object path)
        $hashes | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Output "$name-manifest-$pass.json")
        $manifests += ,$hashes
        $report += [pscustomobject]@{ framework = $name; pass = $pass; seconds = $timer.Elapsed.TotalSeconds; bytes = (Get-ChildItem $destination -Recurse -File | Measure-Object Length -Sum).Sum; directory = $destination }
    }
    $different = Compare-Object ($manifests[0] | ConvertTo-Json -Compress) ($manifests[1] | ConvertTo-Json -Compress)
    if ($different) { throw "$name publication hashes differ; inspect manifests" }
}
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Output 'publish-results.json')
