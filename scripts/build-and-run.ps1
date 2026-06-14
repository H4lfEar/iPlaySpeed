# iPlaySpeed — test, build, kill running instance (UAC if needed), launch.
# Agent: run after App/Core changes instead of asking the user to restart.
#
#   powershell -ExecutionPolicy Bypass -File scripts\build-and-run.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\build-and-run.ps1 -SkipTest
#   powershell -ExecutionPolicy Bypass -File scripts\build-and-run.ps1 -BuildOnly

param(
    [switch]$SkipTest,
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Test-IPlaySpeedRunning {
    return $null -ne (Get-Process -Name iPlaySpeed -ErrorAction SilentlyContinue)
}

function Stop-IPlaySpeed {
    if (-not (Test-IPlaySpeedRunning)) {
        Write-Host "[*] iPlaySpeed is not running." -ForegroundColor DarkGray
        return
    }

    Write-Host "[*] Stopping iPlaySpeed..." -ForegroundColor Cyan
    Stop-Process -Name iPlaySpeed -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    if (-not (Test-IPlaySpeedRunning)) {
        Write-Host "[+] Stopped." -ForegroundColor Green
        return
    }

    Write-Host "[!] Admin instance — requesting UAC to stop iPlaySpeed..." -ForegroundColor Yellow
    Start-Process -FilePath taskkill -ArgumentList "/F /IM iPlaySpeed.exe" -Verb RunAs -Wait | Out-Null
    Start-Sleep -Milliseconds 500

    if (Test-IPlaySpeedRunning) {
        throw "Could not stop iPlaySpeed. Approve UAC or exit from the tray, then retry."
    }
    Write-Host "[+] Stopped (elevated)." -ForegroundColor Green
}

Stop-IPlaySpeed

if (-not $SkipTest) {
    Write-Host "[1/3] dotnet test" -ForegroundColor Cyan
    dotnet test src/IPlaySpeed.Core.Tests/IPlaySpeed.Core.Tests.csproj -c Release --nologo
}

Write-Host "[2/3] dotnet build" -ForegroundColor Cyan
dotnet build src/IPlaySpeed.App/IPlaySpeed.App.csproj -c Release --nologo

if ($BuildOnly) {
    Write-Host "Build only — done." -ForegroundColor Green
    return
}

Write-Host "[3/3] Launch iPlaySpeed (UAC may appear for admin elevation)..." -ForegroundColor Cyan
$proj = Join-Path $root "src\IPlaySpeed.App\IPlaySpeed.App.csproj"
Start-Process dotnet -ArgumentList @(
    "run", "--project", $proj, "-c", "Release", "--no-build"
) -WorkingDirectory $root

Write-Host "Launched. Approve UAC if prompted." -ForegroundColor Green
