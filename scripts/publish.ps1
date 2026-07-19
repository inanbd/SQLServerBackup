<#
.SYNOPSIS
    Publishes the service and the desktop app for the installer.

.DESCRIPTION
    Produces publish/Service and publish/App (self-contained win-x64 by default,
    so target machines need no .NET runtime), then — if Inno Setup 6 is installed —
    compiles installer/SqlBackup.iss into installer/Output.

.EXAMPLE
    pwsh scripts/publish.ps1
    pwsh scripts/publish.ps1 -SelfContained:$false   # framework-dependent (smaller, needs .NET 8)
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Publishing service..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/SqlBackup.Service/SqlBackup.Service.csproj") `
    -c $Configuration -r $Runtime --self-contained $SelfContained `
    -o (Join-Path $root "publish/Service")
if ($LASTEXITCODE -ne 0) { throw "Service publish failed." }

Write-Host "Publishing desktop app..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/SqlBackup.App/SqlBackup.App.csproj") `
    -c $Configuration -r $Runtime --self-contained $SelfContained `
    -o (Join-Path $root "publish/App")
if ($LASTEXITCODE -ne 0) { throw "App publish failed." }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
    Write-Host "Compiling installer..." -ForegroundColor Cyan
    & $iscc (Join-Path $root "installer/SqlBackup.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
    Write-Host "Installer written to installer/Output" -ForegroundColor Green
}
else {
    Write-Host "Inno Setup 6 not found — skipped installer compilation." -ForegroundColor Yellow
    Write-Host "Install it from https://jrsoftware.org/isinfo.php and run: ISCC.exe installer\SqlBackup.iss"
}

Write-Host "Done. Publish output: $root\publish" -ForegroundColor Green
