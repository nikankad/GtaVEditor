[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$')]
    [string]$Version = '1.1.0',
    [string]$OutputDirectory = 'artifacts/release',
    [switch]$LockedMode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
$work = Join-Path ([IO.Path]::GetTempPath()) ('gtav-publish-' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $work 'publish'
$package = Join-Path $work 'package'

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    $arguments = @(
        'publish', 'src/SaveEditor.App/SaveEditor.App.csproj', '-c', 'Release',
        '-p:PublishProfile=Public', '-p:EnableWindowsTargeting=true',
        "-p:Version=$Version", '-o', $publish
    )
    if ($LockedMode) { $arguments += '-p:RestoreLockedMode=true' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

    $exe = Join-Path $publish 'GtaVSaveEditor.exe'
    if (!(Test-Path $exe)) { throw 'Publish did not produce GtaVSaveEditor.exe.' }
    $exeMB = (Get-Item $exe).Length / 1MB
    if ($exeMB -lt 30) { throw "The EXE is only {0:N1} MB; it looks framework-dependent. The public build must bundle the .NET runtime." -f $exeMB }
    if ($exeMB -gt 200) { throw "The EXE is {0:N1} MB, far larger than expected; check what was bundled." -f $exeMB }
    Copy-Item $exe $package
    foreach ($file in @('LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item (Join-Path $repoRoot $file) $package
    }
    Copy-Item (Join-Path $PSScriptRoot 'release-readme.txt') (Join-Path $package 'README.txt')
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $zip = Join-Path $output 'GtaVSaveEditor-win-x64.zip'
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -CompressionLevel Optimal -Force
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash  GtaVSaveEditor-win-x64.zip`n")
    [pscustomobject]@{
        Version = $Version
        ExeBytes = (Get-Item $exe).Length
        ZipBytes = (Get-Item $zip).Length
        Archive = $zip
        SHA256 = $hash
    }
}
finally {
    Pop-Location
    # Only remove this invocation's generated directory, never the caller's output.
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
