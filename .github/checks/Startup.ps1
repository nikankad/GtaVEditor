param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsWindows) { throw 'The app startup check requires Windows.' }
$app = Start-Process -FilePath (Resolve-Path $Executable).Path -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $app.Refresh()
        if ($app.HasExited) { throw "The app exited during startup ($($app.ExitCode))." }
        if ($app.MainWindowHandle -ne 0) {
            if ($app.MainWindowTitle -ne 'GTA V Save Editor') { throw "Unexpected window: $($app.MainWindowTitle)" }
            Start-Sleep -Seconds 1
            $app.Refresh()
            if ($app.HasExited) { throw 'The app exited while loading the start page.' }
            Write-Output 'Packaged app startup passed.'
            return
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The app did not show its main window within 30 seconds.'
}
finally {
    $app.Refresh()
    if (!$app.HasExited) {
        $null = $app.CloseMainWindow()
        if (!$app.WaitForExit(5000)) { Stop-Process -Id $app.Id -Force }
    }
    $app.Dispose()
}
