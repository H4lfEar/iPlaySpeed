# iPlaySpeed release script (Velopack + GitHub Releases)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.1 -Token <GitHub_PAT>
#
#   -Version : 3-part SemVer (e.g. 0.1.1, 0.2.0). Must be higher than the previous release.
#   -Token   : GitHub fine-grained PAT with "Contents: Read and write" on this repo.
#
# Steps: test -> publish -> (download prev release for delta) -> vpk pack -> upload to GitHub Releases.
# Users then auto-update inside the app (delta = minimal download).

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Token
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$repo = "https://github.com/H4lfEar/iPlaySpeed"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be 3-part SemVer (e.g. 0.1.1). Got: $Version"
}

# Ensure vpk tool is available
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    dotnet tool install -g vpk
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}

Write-Host "[1/5] Test" -ForegroundColor Cyan
dotnet test src/IPlaySpeed.Core.Tests/IPlaySpeed.Core.Tests.csproj -c Release --nologo

Write-Host "[2/5] Publish ($Version)" -ForegroundColor Cyan
Remove-Item -Recurse -Force publish, Releases -ErrorAction SilentlyContinue
dotnet publish src/IPlaySpeed.App/IPlaySpeed.App.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o publish

Write-Host "[3/5] Download previous release (for delta)" -ForegroundColor Cyan
try { vpk download github --repoUrl $repo --token $Token } catch { Write-Host "No previous release (first publish) - skipping" -ForegroundColor Yellow }

Write-Host "[4/5] Pack" -ForegroundColor Cyan
vpk pack --packId iPlaySpeed --packVersion $Version --packDir publish --mainExe iPlaySpeed.exe --packTitle iPlaySpeed --icon src/IPlaySpeed.App/assets/app.ico

Write-Host "[5/5] Upload to GitHub Release (tag v$Version)" -ForegroundColor Cyan
vpk upload github --repoUrl $repo --token $Token --publish --releaseName "iPlaySpeed $Version" --tag "v$Version"

Write-Host "Done. Users will auto-update to v$Version on next launch." -ForegroundColor Green
