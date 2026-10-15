[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsWindows) { throw 'The WPF startup smoke test requires Windows.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('gtav-startup-' + [Guid]::NewGuid().ToString('N'))
$process = $null
try {
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $work
    $process = Start-Process -FilePath (Join-Path $work 'GtaVSaveEditor.exe') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) { throw "The app exited during startup ($($process.ExitCode))." }
        if ($process.MainWindowHandle -ne 0) {
            if ($process.MainWindowTitle -ne 'GTA V Save Editor') {
                throw "Unexpected startup window: $($process.MainWindowTitle)"
            }
            Start-Sleep -Seconds 1
            $process.Refresh()
            if ($process.HasExited) { throw 'The app exited while loading its first page.' }
            Write-Output "Startup passed: $($process.MainWindowTitle)"
            return
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The app did not open its main window within 30 seconds.'
}
finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (!$process.HasExited) {
            $null = $process.CloseMainWindow()
            if (!$process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
        }
        $process.Dispose()
    }
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
