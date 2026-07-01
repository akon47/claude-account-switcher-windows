# CLAUDE.md — Claude Account Switcher 작업 인수인계

> 이 파일은 다른 세션/계정에서 작업을 이어가기 위한 컨텍스트다. Claude Code는 이 폴더에서
> 작업할 때 이 파일을 자동으로 읽는다. (저장소: https://github.com/akon47/claude-account-switcher-windows)

## 무엇을 만드는가

**Claude Account Switcher** — 여러 **Claude Code(CLI) 계정**을 Windows에서
전환·동시 실행하는 **시스템 트레이 앱**.

- 각 계정 = "프로필" = 자기만의 격리 설정 폴더(`%APPDATA%\ClaudeAccountSwitcher\profiles\<id>\`).
- **전환(Switch)**: 프로필의 자격증명을 `~/.claude`로 복사 → 평소 `claude`가 그 계정으로 동작.
- **동시 실행(Concurrent)**: 새 터미널을 `CLAUDE_CONFIG_DIR=<프로필폴더>`로 띄워 병렬 사용.

## 기술 스택 / 핵심 사실

- **.NET 9 WPF** (`net9.0-windows`). 네임스페이스 `ClaudeAccountSwitcher`, 어셈블리/exe `Claude-Account-Switcher`.
- **MVVM**: `Microsoft.Extensions.DependencyInjection`(MS IoC) + `CommunityToolkit.Mvvm`
  (`ObservableObject`/`[ObservableProperty]`/`[RelayCommand]`). App.xaml.cs가 합성 루트(DI 컨테이너 구성).
- **다크 테마**(자체 디자인): `Theme/` 리소스 사전. 배경 `#1E1E1F` / 컨트롤 `#212121` / 액센트 Azure `#0078D4`.
  커스텀 타이틀바(WindowChrome)·다크 컨트롤 스타일 전부 스톡 WPF 대상으로 디커플드.
- 트레이: **H.NotifyIcon.Wpf `2.3.2` 핀 고정**. (주의) 2.4.x는 net9 자산을 빼고 net10만 제공 →
  net462로 폴백되어 런타임 로드 실패. **버전 올리지 말 것** (올리려면 TFM 확인 필수).
- Claude 인증 구조 (중요):
  - 토큰: `~/.claude/.credentials.json` 의 `claudeAiOauth` (accessToken/refreshToken…).
    accessToken은 `sk-ant-` **불투명 토큰(JWT 아님)** → 토큰에서 이메일 못 뽑음.
  - 계정 정보(이메일): `~/.claude.json` 의 `oauthAccount` (emailAddress/displayName/organizationName…).
  - `CLAUDE_CONFIG_DIR=X` 설정 시 **X 폴더 안에 전부** 생성됨 (`.credentials.json` + `.claude.json` + projects/…).
    → 격리 로그인 프로필의 이메일은 `<프로필폴더>/.claude.json` 에서 읽는다.
- **전환은 두 파일을 모두 손댄다**: `.credentials.json` 교체 + `~/.claude.json`의 `oauthAccount`도
  대상 계정 것으로 패치(백업 후). 안 그러면 Claude `/status` 표시가 어긋남.
- **다국어(14개)**: `Localization/<culture>.json`. 하드코딩 목록 없음 — `LocalizationManager`가 런타임에
  **exe 옆 `locale\` 폴더**의 `*.json`을 스캔(`AppContext.BaseDirectory\locale`). exe에 임베드하지 않는다 —
  csproj가 `Localization\*.json`을 `Content`(TargetPath=`locale\…`, `ExcludeFromSingleFile`)로 출력 폴더에
  복사하고, 인스톨러 `File /r`가 설치 폴더로 옮긴다. 각 JSON이 `_culture`/`_name` 메타를 품는다.
  **언어 추가 = 설치 폴더 `locale\`에 JSON 1개 떨궈 넣기(재컴파일 불필요)** 또는 `Localization\`에 추가 후 빌드.
  첫 실행 시 윈도우 UI 언어로 자동 선택.
- **자동 업데이트**: `Services/UpdateService.cs` 가 GitHub `releases/latest` 태그를 현재 어셈블리 버전과
  비교 → 새 버전이면 `*Setup.exe` 자산을 받아 인스톨러 실행(시작 시 1회 + 트레이 "업데이트 확인" 메뉴).
- **버전**: csproj `<Version>`(현재 0.3.0). CI는 `vX.Y.Z` 태그에서 버전을 주입(`build-installer.ps1 -Version`).

## 아키텍처 (파일 맵)

```
App.xaml(.cs)              합성 루트(DI). 트레이 상주 + 트레이 메뉴(빠른 전환/자동실행/탐색기/설정/종료),
                          --launch <id> --dir <path> 인자 처리(탐색기 메뉴에서 호출). 창은 DI로 resolve.
Controls/ThemedWindow.cs   커스텀 다크 타이틀바용 경량 Window 베이스(SystemCommands 바인딩만; DI/P-Invoke 결합 없음)
Views/MainWindow.xaml(.cs) 관리 창(ThemedWindow). 프로필 목록(플랜 색뱃지/세션 색/활성 녹색 표시등) + 캡처/추가/전환/실행/이름변경/삭제/새로고침/설정
                          이름칸 더블클릭=편집(EditableTextBlock이 셀 전체 더블클릭 처리·핸들드), 행 더블클릭=전환(이름칸 제외)
Views/InputDialog          이름 입력 다이얼로그(테마)
Views/MessageDialog        정보/오류/확인 다이얼로그(테마)
Views/SkipPermissionsDialog --dangerously-skip-permissions 부여 여부 + "다시 묻지 않음"
Views/SettingsWindow       설정: 실행 셸(PowerShell/cmd) + 스킵권한 기억값 재설정
Views/SessionBrowserWindow 세션 브라우저: 소스 계정 선택→세션 목록(프로젝트/마지막사용/미리보기), 대상 계정 골라 이어하기(resume)
ViewModels/*               MainViewModel / InputDialog / MessageDialog / SkipPermissionsDialog / Settings / ProfileItem
Services/IDialogService    테마 다이얼로그 추상화(ShowInput/Confirm/ShowInfo/ShowError/AskSkipPermissions/ShowSettings)
Services/DialogService     IDialogService 구현(ProfileStore 주입). 활성 창을 owner로 잡음.
Services/Launcher.cs        CLAUDE_CONFIG_DIR 격리로 PowerShell/cmd + claude 실행(셸·스킵권한 선택)
Services/ProfileStore.cs    캡처/전환/삭제/메타갱신 핵심 로직
Services/UpdateService.cs   GitHub Releases 기반 자동 업데이트(최신 확인/다운로드/인스톨러 실행)
Services/SessionStore.cs   프로필별 대화 세션(트랜스크립트) 열거 + 다른 계정으로 복사해 이어하기(resume) 지원.
                          세션 파일 `<configdir>\projects\<enc>\<id>.jsonl`(enc 는 cwd 로만 결정→계정 무관).
                          ListForProfile: 프로필 폴더(+활성이면 ~/.claude) 훑어 cwd/미리보기 파싱. ImportInto: 같은 enc 로 복사(있으면 보존)
Services/SessionKeepAliveService.cs  세션 자동 유지 백그라운드 감시(App 시작 시 가동). KeepSessionAlive 켜진 프로필을
                          60초 주기로 점검해 5시간 창 resets_at 이 지나면 Launcher.FireKeepAlive 로 `claude -p "hi"`
                          (headless) 발동 → 새 창 즉시 시작. 5분 쿨다운으로 중복 발동 방지
Services/StatusLineProvisioner.cs  동시 실행 시 프로필 settings.json 에 claude statusLine 설치(👤 이메일·플랜·이름·세션%). powershell -File <ps1>, ps1 은 prefix 바이트 + stdin five_hour 세션%
Services/PlanFormatter.cs   구독→플랜 라벨(Pro/Max 5x) 공유 포맷(목록 뱃지 + statusLine)
Services/AppPaths/ClaudeConfig/CredentialsReader/AutoStart/ExplorerMenu  경로/설정/자동실행/탐색기메뉴
Localization/              LocalizationManager(런타임 스캔) + LocExtension + <culture>.json 14개(_culture/_name 메타)
Models/Profile.cs          프로필 모델(계산 속성). SessionRemaining/SessionPercent(메모리 캐시) 포함
Models/AppData.cs          profiles.json 영속 데이터(Profiles, ActiveProfileId, LastWorkingDir, Shell, SkipPermissions, Language, StatusLine)
Models/ShellKind.cs        PowerShell | Cmd
ViewModels/ProfileItemVM   행 표시. PlanKind/PlanLabel(뱃지), StatusKind(표시등), SessionLevel(색 구간) 계산
Theme/                     Colors/Brushes/Effects + Theme.xaml(엔트리) + Controls/*.xaml(컨트롤 스타일 8종). Violet=Max 뱃지
Converters/                값 컨버터(markup-extension 패턴, 자체 구현). XAML에서 {conv:XxxConverter}로 사용
Installer/                 Setup.nsi(UTF-8 BOM!) + build-installer.ps1(-Version 주입 지원)
Resources/app.ico          트레이/앱 아이콘(클로드 클레이 원+스왑 화살표). generate-icon.ps1 로 재생성
.github/workflows/         build / bump-version / release(태그→인스톨러→릴리스) / winget(자동 제출)
winget/                    winget 매니페스트 3종 + README(식별자 akon47.ClaudeAccountSwitcher)
```

데이터: `%APPDATA%\ClaudeAccountSwitcher\` (profiles.json, profiles/<id>/, backups/). 저장소엔 안 들어감.

## 명령 / 빌드

```
dotnet build Claude-Account-Switcher.csproj -c Debug      # 빌드
dotnet run --project Claude-Account-Switcher.csproj       # 실행(트레이)
powershell Installer\build-installer.ps1                  # 인스톨러 빌드 -> dist\Claude-Account-Switcher-Setup.exe
powershell Installer\build-installer.ps1 -Version 0.3.0   # 버전 주입(CI는 태그값 전달)
powershell Resources\generate-icon.ps1                    # app.ico 재생성
```

- **재빌드 전 실행 중인 `Claude-Account-Switcher.exe`를 종료**해야 함 (exe 잠금).
  `Get-Process Claude-Account-Switcher | Stop-Process -Force`
- 인스톨러: 자기완결(.NET 불필요), per-user(`%LOCALAPPDATA%\Programs`, 관리자 불필요),
  ~61MB. NSIS(makensis) 필요.
- **릴리스**: `vX.Y.Z` 태그 푸시(또는 Actions의 `bump-version` 수동 실행) → `release` 워크플로가
  자기완결 인스톨러를 빌드해 GitHub Release 로 올림 → 공개되면 `winget` 워크플로가 winget-pkgs PR 생성.

## 진행 상황

- 트레이+창 뼈대 / 캡처·전환·삭제 + 트레이 빠른전환 / 이메일 표시 + oauthAccount 동기화
- 동시 실행(새 창) + 자동 실행 / 탐색기 우클릭 "Claude로 실행" 서브메뉴 / .sln + NSIS 인스톨러
- **다크 테마 + MVVM(DI) 전면 적용** / 값 컨버터 라이브러리 자체 구현
- **실행 옵션**: --dangerously-skip-permissions 묻는 다이얼로그(+다시 묻지 않음) / cmd·PowerShell 선택 / 설정 창
- **UI 폴리시**: 새 아이콘 / 플랜 색뱃지 / 세션 한도 색(녹색·주황·버밀리온) / 활성 녹색 표시등 / 이름 더블클릭 충돌 해결
- **다국어 14개** + 런타임 JSON 스캔(메타 기반) / **자동 업데이트**(GitHub Releases)
- **계정 상태줄**(claude statusLine): 동시 실행 시 하단에 `👤 이메일·플랜·이름·세션%` 고정. 세션%는 claude stdin(rate_limits.five_hour)에서 실시간. 설정 토글로 on/off(기본 on, 사용자 커스텀 statusLine 보존)
- **CI/CD**: GitHub Actions(build/bump-version/release/winget) + winget 매니페스트 + README(영/한)
- **계정 간 세션 이어하기(resume)**: 메인창 툴바 🕘 → 세션 브라우저. 소스 계정의 대화 세션 목록을 보고,
  대상 계정을 골라 "이어하기"하면 그 세션 `.jsonl`을 대상 프로필 폴더의 같은 `projects\<enc>`로 복사하고
  `claude --resume <id>`를 원본 cwd에서 새 창으로 실행. 사본이므로 원본은 소스 계정에 보존(포크). 원본 폴더가
  없으면 중단(resume 이 세션 파일을 못 찾음). `Launcher.LaunchInProfile(..., resumeSessionId)` 로 실행.
- **세션 자동 유지**: 프로필별 토글(목록 "세션 유지" 체크박스). 켜면 그 계정의 5시간 창이 리셋되는 즉시
  `claude -p "hi"`(headless, 창 없음)로 새 창을 시작시킴. 활성 프로필은 ~/.claude, 그 외는 CLAUDE_CONFIG_DIR
  격리본으로 발동. 트레이 앱 상주 중에만 동작(서비스 분리해도 PC 켜짐·로그인 전제는 동일해 앱 내부 감시자로 둠).
  판정: **유효한 usage 응답**인데 5시간 창 resets_at 가 **null(=100%, 활성 창 없음)이거나 지났으면 발동**
  (예전엔 null 을 놓쳐 100%+리셋없음에 고착되던 구멍이 있었음). usage 가 null(무료/조회실패)이거나 미래면 미발동.
  주간(seven_day) 한도 소진 시엔 어차피 못 쓰므로 미발동.
- **세션 사용량 표시(UsageService)**: oauth/usage 의 `five_hour`(5시간) + `seven_day`(주간) 파싱. 평소엔 5시간
  잔여%+리셋 카운트다운 표시. **주간 소진 시(seven_day 잔여 0) 0%로 표시하고 카운트다운을 주간 리셋까지로 전환**
  (SessionUsage.DisplayPercent/DisplayResetsAt). 주간 소진이어도 keep-alive 5시간 판정엔 원본 five_hour resets_at 사용.

## 남은 일 / 다음 후보

- winget **최초 등록**(`wingetcreate`)과 자동 제출용 `WINGET_TOKEN` 시크릿 등록 (워크플로/매니페스트는 준비됨)
- 코드 서명(SmartScreen 제거 — 인증서 필요)
- DPAPI로 저장 자격증명 암호화
- 자동 업데이트 옵션(자동 확인 끄기/주기) 설정 노출, 다국어 추가

## 알아둘 점 / 함정

- **WPF StaticResource 함정(중요)**: 스타일/템플릿 안의 `{StaticResource X}`는 **그 사전의 머지 클로저**에서
  해석된다(앱 리소스의 형제 사전을 못 봄). 그래서 `Theme/Controls/*.xaml` 각 파일은 자기 의존 브러시를 위해
  `Brushes.xaml`+`Effects.xaml`을 직접 머지한다. **뷰(Views/*.xaml)에서는 테마 리소스를 `DynamicResource`로**
  참조한다(런타임 FindResource는 앱 리소스까지 재귀 탐색). 새 컨트롤 스타일/뷰 추가 시 이 규칙을 지킬 것.
- Win11은 새 트레이 아이콘을 `^`(숨겨진 아이콘)에 넣음 — 안 보이는 게 정상. ForceCreate() 사용 중.
- 탐색기 메뉴는 Win11에선 "더 많은 옵션 표시"(Shift+우클릭) 안에 나타남(클래식 메뉴).
- 탐색기 `--launch` 실행: 스킵권한이 "매번 묻기"(SkipPermissions=null)면 실행 전에 스킵권한 다이얼로그를
  띄운다(취소하면 실행 안 함). 기억값이 있으면 UI 없이 그 값으로 바로 실행.
- 설치 후엔 자동실행/탐색기메뉴 토글을 껐다 켜서 레지스트리가 설치된 exe 경로를 가리키게.
- 커밋 작성자는 이 repo 한정 `Kim, Hwan <akon47@naver.com>` (로컬 git config).
- 외부 프로젝트명/브랜드는 코드·주석·식별자·경로 어디에도 넣지 말 것(사용자 요청).
- 작업 방식: 모호하면 먼저 묻고, 큰 변경은 작은 마일스톤으로 나눠 진행(사용자 선호).
- **릴리스 모델**: `main`에 **일반 커밋**을 선형으로 쌓는다(squash/force-push 안 함 — CONTRIBUTING.md 기준).
  배포 시 `<Version>`을 올린 커밋을 푸시하고 `vX.Y.Z` 태그를 밀면 `release` 워크플로가 자기완결 인스톨러를
  빌드해 GitHub Release로 올린다(공개되면 `winget` 워크플로가 PR 생성). 브랜치는 `main` 유지(master로 안 바꿈).
- **다국어 추가는 JSON만**: 빌드에 포함하려면 `Localization/xx-XX.json`에 `_culture`/`_name` 포함해 추가
  (csproj `Content` 글롭이 출력 `locale\`로 복사). 재컴파일 없이 늘리려면 **설치 폴더 `locale\`에 JSON을
  직접 떨궈** 넣으면 된다(런타임에 그 폴더를 스캔). 매니저엔 손대지 말 것. JSON은 UTF-8(BOM 무관),
  문자열 안 따옴표는 이스케이프하거나 전각 “ ” 사용.
- 탐색기 우클릭 메뉴 아이콘은 exe 아이콘(`"<exe>",0`)을 가리킴 → 아이콘 바꿔도 옛것 보이면 **윈도우 아이콘 캐시**.
- **코드 스타일**: `.editorconfig`(C#/네이밍/분석기 심각도) + `Settings.XamlStyler`(XAML 포맷) + `stylecop.json`.
  StyleCop/Roslynator 빌드 강제는 `Directory.Build.props`에서 **옵트인**(현재 간결한 한 줄 스타일이라 켜면 ~300 포맷 경고).
- **MVVM 노코드비하인드 지향**(사용자 선호): 뷰 상호작용은 커맨드(`[RelayCommand]`)+ `Behaviors/<타입>/` 첨부 동작으로,
  `*.xaml.cs` 이벤트 핸들러 지양. 단축키는 네이티브 `InputBindings`. 자세한 규약은 `CONTRIBUTING.md`.
  (기존 코드비하인드: MainWindow 드래그/더블클릭, ProgressDialog, EditableTextBlock → 추후 비헤이비어로 이관 후보.)
