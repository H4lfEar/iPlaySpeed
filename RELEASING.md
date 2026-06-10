# 배포 / 업데이트 가이드 (Velopack + GitHub Releases)

iPlaySpeed는 **Velopack** 자동 업데이트를 사용합니다. 사용자는 `iPlaySpeed-win-Setup.exe`로 한 번 설치하면,
이후 새 버전이 나올 때 앱이 **자동으로 (델타) 업데이트**를 받아 적용합니다.

## 버전 규칙
- **3자리 SemVer**만 사용: `0.1.0`, `0.1.1`, `0.2.0` ...
- 항상 이전보다 높은 버전이어야 업데이트로 인식됩니다.
- (수동 빌드 때 쓰던 4자리 `0.1.0.1` 형식은 Velopack에서 불가)

## 새 버전 배포 (한 줄)
```powershell
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.1 -Token <GitHub_PAT>
```
스크립트가 테스트 → publish → 델타 생성 → 패키징 → GitHub Release 업로드까지 수행합니다.

- **GitHub PAT**: https://github.com/settings/tokens 에서 `repo` 권한으로 발급.
- 토큰은 절대 커밋하지 마세요(명령 인자로만 전달).

## 처음 1회: 사용자 배포물
- 설치 파일: GitHub Release의 **`iPlaySpeed-win-Setup.exe`** 를 배포(이걸 받아 설치).
- 설치하면 시작 메뉴/바탕화면 바로가기가 생기고, 이후 자동 업데이트가 동작합니다.

## 수동 업로드(토큰 없이, 웹 UI)
`scripts\release.ps1` 대신 직접 올리려면:
1. `dotnet publish ... -o publish` 후 `vpk pack --packId iPlaySpeed --packVersion <ver> --packDir publish --mainExe iPlaySpeed.exe`
2. 생성된 `Releases\` 폴더의 **모든 파일**(Setup.exe, *-full.nupkg, *-delta.nupkg, RELEASES, releases.win.json, assets.win.json)을
   GitHub → Releases → 새 릴리스(tag `v<ver>`)에 첨부.
   - ⚠️ `releases.win.json`/`RELEASES`/`*.nupkg`를 빠뜨리면 앱이 업데이트를 못 찾습니다.

## 참고
- 미서명 빌드라 SmartScreen 경고가 뜰 수 있습니다(코드 서명 인증서로 해결 가능).
- 앱은 관리자 권한으로 실행되며, 설치/업데이트 시 UAC가 표시될 수 있습니다.
