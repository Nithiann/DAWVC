param([Parameter(Mandatory)][string]$PublishRoot)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Native smoke requires Windows' }
$results = @()
foreach ($name in @('Avalonia', 'Wpf', 'Photino')) {
    $folder = Get-ChildItem $PublishRoot -Directory -Filter "$name-1-*" | Select-Object -First 1
    if (-not $folder) { throw "Missing published $name directory" }
    $executable = if ($name -eq 'Wpf') { 'Wpf.exe' } else { "DawVcs.GuiSpike.$name.exe" }
    $log = Join-Path $PublishRoot "$name-native-stdout.log"
    $err = Join-Path $PublishRoot "$name-native-stderr.log"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process (Join-Path $folder.FullName $executable) -WorkingDirectory $folder.FullName -PassThru -RedirectStandardOutput $log -RedirectStandardError $err
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 50
            $process.Refresh()
            if ($process.HasExited) { throw "$name exited before opening a window; inspect logs" }
        } until ($process.MainWindowHandle -ne 0 -or [DateTime]::UtcNow -gt $deadline)
        if ($process.MainWindowHandle -eq 0) { throw "$name did not open a native window" }
        # This is a smoke proxy, explicitly not a first-render acceptance measurement.
        $results += [pscustomobject]@{ framework = $name; kind = 'native-window-smoke'; timeToWindowHandleMs = $timer.Elapsed.TotalMilliseconds; processPrivateBytes = $process.PrivateMemorySize64; processWorkingSet = $process.WorkingSet64 }
        if (-not $process.CloseMainWindow()) { throw "$name did not accept close request" }
        if (-not $process.WaitForExit(10000)) { throw "$name did not exit after close" }
        if ($process.ExitCode -ne 0) { throw "$name exited with $($process.ExitCode)" }
    } finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PublishRoot 'native-smoke-results.json')
