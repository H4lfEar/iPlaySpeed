# iPlaySpeed — 개발 설계서 (design.md)

> 멀티 게임 일일퀘스트 오버레이 런처 · Claude Code 핸드오프용 구현 설계서
> 이 문서는 **현재 코드베이스의 실제 상태**와 **이어서 구현할 작업**을 정리한다.
> 기획 의도/배경은 `iPlaySpeed_기술설계문서.md`, 사용자용 안내는 `README.md` 참고.

---

## 0. 한 줄 요약

여러 게임의 일일 숙제를 alt+tab 없이 한 화면(항상 위 오버레이)에서 추적하고, "다음" 버튼 하나로
정해진 순서대로 게임을 연속 실행해 주는 Windows 데스크탑 런처.

---

## 1. 기술 스택 / 빌드

| 항목 | 값 |
|---|---|
| 언어/런타임 | C# / .NET 8 |
| UI | WPF |
| Core 라이브러리 TFM | `net8.0` (Windows 비의존, 테스트 가능) |
| App TFM | `net8.0-windows10.0.19041.0` (SMTC 등 WinRT 사용 위해 SDK 버전 명시) |
| 테스트 | xUnit (`IPlaySpeed.Core.Tests`) |
| 외부 패키지 | `NAudio 2.2.1` (App 전용, CoreAudio 세션 볼륨 제어) |
| 빌드/실행 | 루트의 `빌드_및_실행.bat` (= `dotnet test` 후 `dotnet run`) |
| 솔루션 | `iPlaySpeed.sln` (3개 프로젝트) |

**중요 빌드 설정 — `Directory.Build.props` (루트)**: `<CodePage>65001</CodePage>` 로 모든 소스를
**UTF-8로 강제** 컴파일한다. 한국어 Windows(CP949)에서 한글 문자열이 깨지지 않게 하는 핵심 설정이니
삭제하지 말 것. (BOM 유무와 무관하게 동작)

데이터 저장 위치: `%AppData%\iPlaySpeed\` (아래 7장 참고).

---

## 2. 솔루션 구조 (파일 맵)

```
iPlaySpeed/
├─ iPlaySpeed.sln
├─ Directory.Build.props              # CodePage 65001 (UTF-8 강제)
├─ 빌드_및_실행.bat                    # ASCII 전용 배치(cmd 인코딩 이슈 방지)
├─ README.md / iPlaySpeed_기술설계문서.md / design.md
└─ src/
   ├─ IPlaySpeed.Core/                # ── 순수 로직(검증 대상). Windows 비의존 ──
   │  ├─ GameStatus.cs                # enum: NotStarted/InProgress/UpdateOnly/Completed
   │  ├─ Models.cs                    # GameEntry, GameDailyState, GamePatchInfo, DailyRecord, AppSettings
   │  ├─ CompletionJudge.cs           # 완료 판정 + 포그라운드 시간 누적 + 진행도%
   │  ├─ PatchPredictor.cs            # 패치 주기 학습/예측/정렬, 패치 이벤트 감지
   │  ├─ OrderResolver.cs             # 자동(패치)/수동(Order) 정렬 해석
   │  ├─ DailyResetClock.cs           # 일일 리셋 경계 판정
   │  ├─ LauncherFilter.cs            # 런처/업데이터/헬퍼 실행파일명 판별(타이머가 실제 게임만 카운트)
   │  └─ JsonStore.cs                 # JSON 로드/원자적 저장
   │
   ├─ IPlaySpeed.Core.Tests/          # xUnit. 위 로직 27개 테스트(모두 GREEN)
   │  └─ CoreLogicTests.cs
   │
   └─ IPlaySpeed.App/                 # ── WPF UI + Windows 연동 ──
      ├─ app.manifest                 # asInvoker(Velopack 훅 호환) + PerMonitorV2 DPI
      ├─ Program.cs                    # 진입점: Velopack 훅 우선 → 일반 실행 시 self-elevation(관리자 재실행)
      ├─ assets/app.png(.ico)         # 앱/창/트레이 아이콘(임베드, 없으면 폴백 생성)
      ├─ App.xaml(.cs)                # 색상 리소스 + IconButton 스타일 + 전역 예외 핸들
      ├─ MainWindow.xaml(.cs)         # 2패널(감지/등록), 툴바, 진행도, 드래그앤드롭
      ├─ OverlayWindow.xaml(.cs)      # 항상 위 게이지 오버레이 + 미디어 + 축소
      ├─ SettingsWindow.xaml(.cs)     # 리셋시각/단축키/기본주기/런처자동클릭 토글
      ├─ Models/GameCandidate.cs      # 감지된(미등록) 게임 후보 + 지연 아이콘
      ├─ ViewModels/GameRow.cs        # 등록 게임 1행(INotifyPropertyChanged)
      ├─ Native/
      │  ├─ NativeMethods.cs          # Win32 P/Invoke 모음
      │  ├─ ForegroundWatcher.cs      # 포그라운드 프로세스 (이름, 경로)
      │  ├─ GlobalHotkey.cs           # RegisterHotKey 전역 단축키
      │  └─ IconExtractor.cs          # exe→PNG 아이콘 추출(WPF 이미징)
      └─ Services/
         ├─ AppPaths.cs               # %AppData% 경로/파일명 상수
         ├─ FolderScanner.cs          # 폴더 총용량/최종수정일(패치 감지용)
         ├─ GameLibrary.cs            # games.json 관리, exe/후보로 등록
         ├─ GameDetector.cs           # 설치 게임 감지(레지스트리+Steam)
         ├─ EpicLauncher.cs           # 에픽 매니페스트 파싱→com.epicgames.launcher:// URI 실행 매핑
         ├─ KnownGames.cs             # 알려진 일퀘 게임 카탈로그/노이즈 필터
         ├─ UpdateService.cs          # Velopack 자동 업데이트(GitHub Releases 소스)
         ├─ AppIconFactory.cs         # 창/트레이 아이콘(assets 로고 로드 or 폴백 생성)
         ├─ OverlayHelpImage.cs       # 오버레이 조작법 안내 이미지 생성(설정 도움말)
         ├─ MediaController.cs        # SMTC 미디어 제어/메타/시간/출처
         ├─ VolumeController.cs       # CoreAudio(NAudio) 세션 볼륨을 PID 단위로 조절(게임/미디어 분리)
         └─ DailyRunEngine.cs         # ★ 런타임 심장(1초 틱, 정렬, 다음게임 등)
```

**아키텍처 원칙**: "버그가 숨는 계산 로직"은 전부 `IPlaySpeed.Core`(순수 함수)에 두고 xUnit으로 검증한다.
Windows API/UI는 `IPlaySpeed.App`에만 둔다. 새 로직 추가 시 가능하면 Core에 넣고 테스트를 먼저 쓴다.

---

## 3. 핵심 도메인 로직 (검증된 규칙)

모든 규칙은 `IPlaySpeed.Core`에 구현되어 있고 `CoreLogicTests.cs`로 검증된다. **이 규칙을 바꾸면 테스트도 함께 갱신.**

### 3-1. 완료 판정 — `CompletionJudge`
- 시간 누적은 **게임 창이 화면 맨 앞(foreground)일 때만** 1초씩(`TickForeground`). → 업데이트/딴짓 시간 제외.
- `Judge(state, thresholdMinutes)`:
  - `ManualOverride==true` → **Completed**, `==false` → InProgress/NotStarted.
  - `ActivePlaySeconds >= threshold*60` → **Completed**.
  - 업데이트 감지됨(`UpdateDetectedToday`) & 시간 미달 → **UpdateOnly**(=미완료 표시).
  - 그 외: 시간>0 → InProgress, else NotStarted.
- `ProgressPercent` = 완료 게임 수 / 전체. `ProgressRatio` = 게임별 완료까지 비율(아이콘 차오름).
- `SuggestThresholdMinutes(최근 플레이 초 목록)` = 중앙값 × 0.8 / 60(최소 1분). 평소보다 조금 일찍 완료되게
  완료 임계를 추천. `AutoLearnThreshold` 설정 시 매일 리셋 때 `playlog`에 기록하고 임계를 자동 갱신.

### 3-2. 패치 예측/정렬 — `PatchPredictor`
- `DetectPatchEvent(prev,new)`: 증가량 ≥ **200MB** **또는** 증가율 ≥ **3%** 면 패치로 간주(감소/0 제외).
- `LearnedIntervalDays`: 관측 패치 ≥2회면 간격들의 **중앙값**, 아니면 기본값(설정 `DefaultPatchIntervalDays`, 기본 42).
- `DaysUntilNextPatch` = (마지막패치 + 학습주기) − 오늘. (관측 없으면 폴더 최종수정일/오늘로 콜드스타트)
- `PlayOrder`: **다음 패치까지 남은 일수 내림차순**(=한가한 게임 앞, 패치 임박 게임 뒤), 동률 시 GameId 오름차순.

### 3-3. 정렬 해석 — `OrderResolver.Resolve(games, patches, autoSort, today, default)`
- `autoSort==true` → `PatchPredictor.PlayOrder`.
- `autoSort==false` → `Order` 필드 오름차순(수동).

### 3-4. 일일 리셋 — `DailyResetClock.ShouldReset(lastReset, hour, minute, now)`
- 사용자가 정한 (시,분)을 하루 시작 경계로 보고, 현재가 속한 리셋일의 경계를 마지막 리셋이 못 넘었으면 true.

---

## 4. 런타임 엔진 — `DailyRunEngine` (App)

1초 `DispatcherTimer`로 도는 중심부. `ObservableCollection<GameRow> Rows`가 **메인 목록·오버레이·다음게임이
공유하는 단일 표시 순서**다.

**매 틱(OnTick)**:
1. `DailyResetClock.ShouldReset` → 참이면 `ResetDay()`(진행도 0, 오버라이드 해제).
2. 포그라운드 프로세스 `(name, path)` 취득(`ForegroundWatcher.ForegroundProcessInfo`).
3. 각 게임에 대해 `MatchesForeground`로 **현재 플레이 중인지** 판정 → `TickForeground`로 시간 누적.
   - `MatchesForeground`는 폴더 안의 **실제 게임 exe만** 인정하고 런처/업데이터/헬퍼는 제외한다
     (`LauncherFilter.IsLauncherOrHelper` + 등록 런처 ExePath 제외). → 타이머가 런처가 아니라 실제 게임 기준으로 작동.
   - `CountWhileRunning`(전역 설정) ON이면 포그라운드가 아니어도 **실제 게임 exe가 실행 중이면 누적**
     (alt+tab 자동진행 턴제 게임용). `SnapshotProcessPaths`(전체 프로세스 열거)는 비싸서 **백그라운드 스레드에서
     ~2초마다 캐시 갱신**하고 틱은 캐시만 읽는다(매초 UI 멈춤=마퀴 끊김 방지).
4. 진행도% 재계산, `IsCurrent` 갱신, **`_currentRow`(포그라운드 게임) 저장**.
5. 15초마다 상태 저장.

**`MatchesForeground(entry, fgName, fgPath)`** — 둘 중 하나면 일치:
- (1) `EffectiveGameProcessName == fgName`, 또는
- (2) **포그라운드 실행파일 경로가 게임의 `GameFolder` 안**에 있음.
- → (2) 덕분에 **런처로 켜져 프로세스 이름이 달라지는 게임도** 시간이 정상 누적된다. (중요)
- 포그라운드 경로는 `NativeMethods.GetProcessImagePath`(QueryFullProcessImageName,
  PROCESS_QUERY_LIMITED_INFORMATION)로 얻는다. → **관리자 권한 게임(안티치트, HoYo 등)도 경로를 읽어**
  폴더 매칭이 동작한다. (구버전의 `MainModule.FileName`은 권한 부족으로 null이 되어 시간이 안 쌓였음)

**다음 게임 — `NextGame()`**:
- 오버레이(Topmost) 버튼을 누르면 게임이 포그라운드를 잃어 `_currentRow`가 null이 되므로,
  **`_lastGameRow`(가장 최근 포그라운드 게임, 우리 앱 창으로 포커스가 와도 유지)** 를 기준으로 한다.
- 단, `_lastGameRow`는 게임을 닫은 뒤에도 남으므로 **`IsGameRunning`으로 실제 실행 여부를 확인**한다.
  실행 중이면 닫고 그 **다음 인덱스부터 순환**해 첫 미완료 실행, **실행 중이 아니면 맨 위(1번)부터**.
  (이 확인이 없으면 '아무 게임도 안 떠 있을 때 다음을 눌러도 맨 위 게임을 건너뛰는' 버그가 난다.)
- 실행한 게임을 `_lastGameRow`로 즉시 설정해 연속 '다음'·볼륨 대상이 바로 따라간다.
- **직전 게임이 이미 꺼졌고 미완료(임계 시간 미만)면 → 같은 게임을 다시 실행**(업데이트 재시작 등 대응).
  완료된 게임이면 그 다음 미완료 게임으로 진행.
- **`CloseOnNext`(설정)**: ON이면 현재 게임을 바로 종료(`CloseGame(forceKill:true)`)한 뒤 다음 실행,
  OFF이면 **종료하지 않고 다음 게임만 실행**(여러 게임 동시 진행). `CountWhileRunning`과 함께 쓰면 동시 일퀘에 유용.
- **진행도 리셋 직후**(`ForceResetProgress`)에는 1회성 플래그로 실행 중 게임을 무시하고 **맨 위(첫 미완료)부터** 실행.
- 실행 중 판정은 `IsRealGameRunning`(런처/헬퍼 제외, 권한 높은 프로세스 경로도 조회).
- `FindNextToPlay(startIdx, exclude)`가 순환 탐색 담당.

**게임 종료 — `CloseGame(entry)`**: (1) 등록 프로세스 이름 + (2) **`GameFolder` 안에서 실행된 프로세스**를
모두 모아 `CloseMainWindow` 후 5초 뒤에도 살아있으면 `Kill`. → 런처로 켠 실제 게임까지 닫음.

**정렬**:
- `ApplyOrder()` — 설정에 따라 `Rows`를 자동/수동 순서로 `Move`.
- `AutoSortByPatch` 세터 — 끄면 **현재 보이는 순서를 Order에 고정(PersistManualOrder)** 해 점프 방지.
- `MoveUp/MoveDown` — `EnsureManualSort()`로 **자동정렬을 끄고** 이동(수동 전환).

**에픽게임즈 실행**: `LaunchGame`은 `EpicLauncher.ResolveLaunchUri`로 게임 폴더가 에픽 설치 게임이면
`com.epicgames.launcher://apps/<AppName>?action=launch`로 실행한다. 에픽 게임은 폴더의 exe(예:
`launcher_epic.exe`)를 직접 실행하면 동작하지 않기 때문(에픽 런처가 자식 프로세스를 띄워야 함). 에픽이
아니면 기존대로 ExePath 직접 실행. (실행 후 폴더 매칭으로 포그라운드/볼륨/종료는 동일하게 동작)

**기타**: `LaunchGame`(실행 전 패치 폴더 스캔 비동기), `SetThreshold`(게임별 완료 분), `ToggleManual`,
`AddGame/RemoveRow`, `ForceResetProgress`(버튼용 즉시 0%).

---

## 5. UI 구성

### 5-1. MainWindow (2패널)
- 상단: 제목 / 진행도 바·% / 현재 게임.
- 툴바: `오버레이 표시/숨김`, `▶ 다음 게임`, `⚙ 설정`, `↺ 진행도 초기화`, 우측 `자동 정렬` 체크박스.
- 좌패널 "감지된 게임": `GameDetector` 결과(미등록만), `↻ 리스캔`, 항목 드래그/`추가 ▶`.
- 우패널 "등록된 게임": `Rows` 바인딩. 행마다 ▲▼(수동정렬), 아이콘, 이름/상태, **완료 임계(분) 입력**,
  `실행`/`완료`(수동 토글)/`✕`. 좌→우 **드래그앤드롭**으로 등록(`DragFormat = "iPlaySpeed.GameCandidate"`).
- **버튼 스타일**: 창 전체에 `IconButton`(배경 박스 없는 흰색, hover/press 시 확대) 암시적 적용.

### 5-2. OverlayWindow (항상 위)
- 무테/투명/Topmost/SizeToContent, 드래그 이동, 위치 `settings.OverlayLeft/Top` 저장.
- 상단: **현재 게임명 + 누적 플레이 시간(분:초, 매초 갱신)**(또는 축소 시 "다음:대기게임") / % / `▶ 다음` / **축소(—/❐)** / `✕(숨김)`.
- 게이지 `Canvas`: 트랙 + 채움 + **게임 아이콘 체크포인트**(완료=초록링✓, 현재=파란링, 미완료=흐림).
  아이콘은 **원형으로 크롭**되고 `CompletionJudge.ProgressRatio`만큼 **아래에서 위로 차오르며 밝아진다**(물 채우기).
  아이콘 **좌클릭=해당 게임 실행**(살짝 확대 애니메이션, 종료 여부는 `CloseOnNext` 따름),
  **우클릭=완료 처리 ↔ 되돌리기 토글(`ToggleComplete`)**(실수 클릭 복구).
- 상단 표시 게임: 포그라운드 게임, 없으면 `CountWhileRunning` 중인 직전 실행 게임(`HasDisplayGame`/`DisplayGameName`).
- 미디어 행: `⏮⏯⏭` + **출처 아이콘** + **고정 150px 제목 구간**(길면 제목 2개를 나란히 배치해 끊김 없이
  순환하는 마퀴, `•` 구분자) + **재생시간 `pos/dur`**. 볼륨: 게임/미디어 2개(밝은 MDL2 아이콘 + 모던 슬라이더).
- **축소 모드**: 게이지/미디어 숨기고 %·다음 대기게임·다음 버튼만.

### 5-3. SettingsWindow
- 일일 리셋(시:분), 기본 패치 주기(일), 단축키(오버레이 토글/다음게임),
  **백그라운드도 시간 누적(CountWhileRunning)**, **'다음' 동작(종료 후 실행 / 종료 안 하고 실행, CloseOnNext)**,
  **런처 자동 클릭 토글(경고 동의 필요)**.
- 좌측 "감지된 게임" 항목마다 `✕`로 **숨기기**(hidden.json에 저장 → 다음 스캔에도 안 보임).
- **❓ 오버레이 조작법** 버튼 → `OverlayHelpImage.Generate()`로 코드로 그린 안내 이미지를 별도 창에 표시.

---

## 6. Windows 연동 (Native/Services)

- **전역 단축키**: `GlobalHotkey`(메시지 전용 창 + `RegisterHotKey`). 기본 `Ctrl+Alt+I`(오버레이), `Ctrl+Alt+N`(다음).
- **아이콘 추출**: `IconExtractor`(SHGetFileInfo→`CreateBitmapSourceFromHIcon`→PNG). System.Drawing.Common 미사용.
- **포그라운드**: `GetForegroundWindow`+`GetWindowThreadProcessId`, `Process.MainModule.FileName`(권한 실패시 null).
- **게임 감지**: `GameDetector`
  - 레지스트리 `...\Uninstall`(HKLM 64/32, HKCU) DisplayName/InstallLocation/DisplayIcon.
  - Steam `libraryfolders.vdf`→`appmanifest_*.acf`(name/installdir)→폴더 내 대표 exe 추정.
  - `KnownGames` 카탈로그로 알려진 일퀘 게임 우선/노이즈 필터.
- **미디어(SMTC)**: `MediaController`(`GlobalSystemMediaTransportControlsSessionManager`).
  - 제목/아티스트, `GetTimelineProperties`(Position/EndTime/LastUpdatedTime), `GetPlaybackInfo`(Playing 여부).
  - **재생 위치 보간**: SMTC가 위치를 띄엄띄엄 보고하므로 `Position = reported + (재생중 ? now-lastUpdated : 0)`로
    매초 부드럽게 표시.
  - 출처 앱 아이콘: `SourceAppUserModelId`→프로세스명 추정→exe→아이콘. **한계: 브라우저의 YouTube는
    브라우저(Chrome/Edge) 아이콘으로 표시됨**(SMTC가 "유튜브"까지 구분 못 함).
- **볼륨 분리(CoreAudio)**: `VolumeController`(NAudio `MMDeviceEnumerator`→기본 재생장치→`AudioSessionManager.Sessions`).
  - 게임: 현재 포그라운드 게임(`DailyRunEngine.CurrentEntry`)의 세션을 **프로세스명 또는 GameFolder 경로**로 매칭해
    `SimpleAudioVolume.Volume`(0~1) 설정. 미디어: SMTC 출처 프로세스명(`MediaController.ResolveSourceProcessName`)으로 매칭.
  - 오버레이의 두 슬라이더는 **게임/미디어 출처가 바뀔 때만** 실제 볼륨을 읽어 동기화(드래그 중 값 튐 방지).
  - **한계**: SMTC 출처가 UWP AUMID라 프로세스명과 다르면(예: UWP Spotify) 매칭 실패. 브라우저 YouTube는
    브라우저 전체 세션이 조절됨(출처 아이콘 한계와 동일).

---

## 7. 데이터 모델 / 저장 (`%AppData%\iPlaySpeed\`)

- `games.json` — `List<GameEntry>` (Id, Name, IconPath, ExePath, GameProcessName, GameFolder, LauncherType, Order, ThresholdMinutes)
- `states.json` — `Dictionary<string,GameDailyState>` (ActivePlaySeconds, UpdateDetectedToday, ManualOverride)
- `patches.json` — `List<GamePatchInfo>` (GameId, ObservedPatchDates, LastFolderWrite, LastTotalBytes)
- `settings.json` — `AppSettings` (ResetHour/Minute, DefaultPatchIntervalDays, AutoSortByPatch, DefaultThresholdMinutes, OverlayHotkey, NextGameHotkey, LastReset, LauncherAutoClickEnabled, **CountWhileRunning**, **CloseOnNext**, **AutoLearnThreshold**, OverlayLeft/Top)
- `hidden.json` — `List<string>` 감지 목록에서 사용자가 숨긴(게임 아님) 항목 키
- `playlog.json` — `Dictionary<string,List<int>>` 게임별 최근 14일 실제 플레이 시간(초). 완료 시간 자동 학습용.
- `history.json` — `List<DailyRecord>` (Date, CompletedGames, TotalGames, TotalMinutes, Start/EndTime)
- `icons/` — 추출한 PNG. JSON은 `System.Text.Json`(camelCase, DateOnly/TimeOnly 지원), 저장은 임시파일→교체(원자적).

---

## 8. 컨벤션 / 주의점 (회귀 방지)

1. **인코딩**: 한글 문자열 깨짐 방지를 위해 `Directory.Build.props`의 `CodePage 65001` 유지. 배치 파일은 ASCII만.
2. **로직은 Core에**: 시간/날짜/정렬/예측 계산은 Core 순수 함수로, xUnit 테스트 동반.
3. **`Path` 모호성**: App에서 `System.Windows.Shapes`와 `System.IO`를 함께 쓰면 `Path` 충돌 → `System.IO.Path` 명시.
4. **런처 게임 매칭**: 완료/현재/종료 판정은 프로세스 이름뿐 아니라 **GameFolder 경로 포함**으로 처리(핵심).
5. **다음 게임/볼륨 대상 기준**: 오버레이를 클릭하면 게임이 포그라운드를 잃으므로, "실행 중" 판정은
   `_currentRow`가 아니라 **`_lastGameRow`(가장 최근 포그라운드 게임)** 로 한다. (`CurrentEntry`도 동일)
6. **방어적 코드**: 레지스트리/프로세스/파일 접근은 전부 try/catch로 감싸 앱이 죽지 않게.
7. **오버레이 한계**: 독점 전체화면 위엔 안 뜸 → 게임을 테두리 없는 창모드로(사용자 안내).
8. **전역 예외**: `App.OnStartup`에서 `DispatcherUnhandledException`을 잡아 메시지로 표시.

---

## 9. 구현 완료 / 남은 작업

### ✅ 구현됨
게임 등록(직접/감지/드래그) · 항상위 오버레이 게이지+체크포인트 · 포그라운드(폴더매칭) 시간 누적 완료판정 ·
업데이트 오판정 방지 · 다음게임(순환·런처 보정) · 전역 단축키 · 일일 자동 리셋 + 수동 초기화 ·
패치 자동학습 정렬(자동/수동 토글, ▲▼) · 게임별 완료 임계(분) · SMTC 미디어 제어 · 재생시간(보간)·제목 마퀴·
출처 아이콘 · 오버레이 축소 · IconButton UI · **볼륨 분리 슬라이더(게임/미디어, CoreAudio)**.

### ⬜ 남은 작업 (우선순위 제안)
1. **리더보드 화면** — `history.json` 시각화(일별 소요시간 추이). LiveCharts2 등.
2. **런처 자동 클릭(실 동작)** — 현재는 설정 토글만. 동의 시 UI Automation으로 "플레이" 버튼 탐색 →
   실패 시 좌표 보정 → `SendInput`으로 **실제 마우스 클릭**. 비번 자동화 금지. ToS/계정 리스크 경고 유지.
3. **게임별 리셋 시각/프로세스명 편집 UI** — 감지가 런처 exe를 잡는 경우 실제 게임 프로세스명을 사용자가 보정.
4. **시작 시 자동 실행/트레이 상주**, 다중 모니터 오버레이 위치 보정 등 편의 기능.

### 알려진 개선 여지
- `CloseGame`/출처아이콘이 전체 프로세스를 열거 → Next 누를 때 짧은 지연 가능(필요 시 백그라운드화).
- 감지 결과 노이즈(설치 프로그램 다수) → 카탈로그/필터 정교화 여지.
- 패치 자동학습은 관측이 쌓여야 정확(콜드스타트는 폴더 수정일+기본주기).

---

## 10. 빌드 & 테스트 빠른 참조

```bash
# 전체 빌드+테스트+실행 (Windows)
빌드_및_실행.bat

# 로직 테스트만
dotnet test src/IPlaySpeed.Core.Tests/IPlaySpeed.Core.Tests.csproj

# 앱 실행
dotnet run --project src/IPlaySpeed.App/IPlaySpeed.App.csproj -c Release
```

새 기능 작업 흐름: **Core에 규칙 추가 → xUnit 테스트 GREEN → App에서 호출/UI 연결 → 빌드 확인.**
