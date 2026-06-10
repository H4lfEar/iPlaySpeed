<#
  iPlaySpeed 릴리스 스크립트 (Velopack + GitHub Releases)

  사용법:
    powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.1 -Token <GitHub_PAT>

  - Version : 3자리 SemVer (예: 0.1.1, 0.2.0). 반드시 이전보다 높아야 업데이트로 인식됨.
  - Token   : GitHub Personal Access Token (repo 권한). 릴리스 생성/업로드에 사용.
              (토큰은 https://github.com/settings/tokens 에서 발급)

  하는 일: 테스트 → publish → (기존 릴리스 받아 델타 생성) → vpk pack → GitHub Release 업로드.
  사용자는 앱에서 자동으로 새 버전을 받아 적용한다(델타라 전송량 최소).
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Token
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$repo = "https://github.com/H4lfEar/iPlaySpeed"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version은 3자리 SemVer여야 합니다 (예: 0.1.1). 입력값: $Version"
}

# vpk 도구 확인(없으면 설치)
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    dotnet tool install -g vpk
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}

Write-Host "[1/5] 테스트" -ForegroundColor Cyan
dotnet test src/IPlaySpeed.Core.Tests/IPlaySpeed.Core.Tests.csproj -c Release --nologo

Write-Host "[2/5] publish ($Version)" -ForegroundColor Cyan
Remove-Item -Recurse -Force publish, Releases -ErrorAction SilentlyContinue
dotnet publish src/IPlaySpeed.App/IPlaySpeed.App.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o publish

Write-Host "[3/5] 기존 릴리스 다운로드(델타 생성용)" -ForegroundColor Cyan
try { vpk download github --repoUrl $repo --token $Token } catch { Write-Host "이전 릴리스 없음(첫 배포) — 건너뜀" -ForegroundColor Yellow }

Write-Host "[4/5] 패키징" -ForegroundColor Cyan
vpk pack --packId iPlaySpeed --packVersion $Version --packDir publish --mainExe iPlaySpeed.exe --packTitle iPlaySpeed --icon src/IPlaySpeed.App/assets/app.ico

Write-Host "[5/5] GitHub Release 업로드 (tag v$Version)" -ForegroundColor Cyan
vpk upload github --repoUrl $repo --token $Token --publish --releaseName "iPlaySpeed $Version" --tag "v$Version"

Write-Host "완료! 사용자는 앱 실행 시 자동으로 v$Version 업데이트를 받습니다." -ForegroundColor Green
