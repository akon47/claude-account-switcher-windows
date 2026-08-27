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
Views/SettingsWindow       설정: 실행 셸(PowerShell/cmd)·관리자 권한으로 실행 + 스킵권한 기억값 재설정
Views/SessionBrowserWindow 세션 브라우저: 소스 계정 선택→세션 목록(프로젝트/이름/마지막사용/미리보기), 대상 계정 골라 이어하기(resume).
                          다중선택 후 "내보내기"로 번들(.claudesession) 저장 / "파일에서 가져오기"로 다른 PC 번들 열어 이어하기
ViewModels/*               MainViewModel / InputDialog / MessageDialog / SkipPermissionsDialog / Settings / ProfileItem
Services/IDialogService    테마 다이얼로그 추상화(ShowInput/Confirm/ShowInfo/ShowError/AskSkipPermissions/ShowSettings)
Services/DialogService     IDialogService 구현(ProfileStore 주입). 활성 창을 owner로 잡음.
Services/Launcher.cs        CLAUDE_CONFIG_DIR 격리로 PowerShell/cmd + claude 실행(셸·스킵권한·관리자권한 선택).
                          CLAUDE_CONFIG_DIR 은 ProcessStartInfo 가 아니라 셸 명령 안에서 설정한다 — 관리자 승격은
                          ShellExecute(Verb=runas) 라 EnvironmentVariables 를 못 쓰기 때문(두 경로를 한 방식으로 통일).
                          LaunchLogin = `claude auth login` 만 띄우는 로그인 전용(브라우저 인증 때문에 승격하지 않음)
Services/ProfileStore.cs    캡처/전환/삭제/메타갱신 핵심 로직
Services/UpdateService.cs   GitHub Releases 기반 자동 업데이트(최신 확인/다운로드/인스톨러 실행)
Services/SessionStore.cs   프로필별 대화 세션(트랜스크립트) 열거 + 다른 계정으로 복사해 이어하기(resume) 지원.
                          세션 파일 `<configdir>\projects\<enc>\<id>.jsonl`(enc 는 cwd 로만 결정→계정 무관).
                          ListForProfile: 프로필 폴더(+활성이면 ~/.claude) 훑어 cwd/미리보기/이름 파싱. ImportInto: enc 로 복사(있으면 보존).
                          서브에이전트(sidechain) 트랜스크립트는 제외(agent-*.jsonl 파일명 + isSidechain). 미리보기는
                          summary 우선, 없으면 첫 '실제' 사용자 메시지(isMeta·<태그> 합성 메시지는 건너뜀 → 영어 보일러플레이트 방지).
                          세션 이름은 트랜스크립트의 `ai-title`(대화 진행 중 갱신 → 파일 꼬리에서 마지막 값 = 현재 이름; ReadLastAiTitle).
                          서브에이전트는 신구조 모두 복사: (신) 세션 사이드카 폴더 `<enc>\<id>\`(subagents 등) 통째 + (구) 평면 agent-*.jsonl(내부 sessionId==부모).
                          ImportInto(overrideProjectFolder): 다른 PC 로 옮겨 작업 폴더가 바뀌면 새 cwd 로 다시 인코딩한 enc 폴더로 복사(resume 가 찾게)
Services/SessionBundle.cs  세션 내보내기/가져오기(단일 파일 `.claudesession`, 실체는 zip). 저장소 구조(projects\<enc>\)를 그대로 미러링
                          (본문 + 사이드카/서브에이전트) + manifest.json(id/enc/cwd/이름/미리보기/원본계정). Read 는 임시폴더에 풀어 ImportInto 가능.
                          선택 시 작업 폴더(cwd)도 workdirs\<enc>\ 로 담음(무거운/재생성 폴더 제외: node_modules·.git·bin/obj·dist 등).
                          Read 는 담긴 작업 폴더를 SessionEntry.BundleWorkdirPath 로 노출 → 가져올 때 사용자가 고른 위치에 RestoreWorkdir.
                          Export/Read/RestoreWorkdir 모두 IProgress+CancellationToken(진행률 다이얼로그, 큰 폴더 대비)
Services/ProjectFolderEncoder.cs  cwd → projects\<enc> 폴더명 인코딩(영숫자 아닌 문자 → '-', 문자당 1개). 다른 PC resume 시 새 폴더 기준 재인코딩용
Services/SessionKeepAliveService.cs  세션 자동 유지 백그라운드 감시(App 시작 시 가동). KeepSessionAlive 켜진 프로필을
                          60초 주기로 점검해 5시간 창 resets_at 이 지나면 Launcher.FireKeepAlive 로 `claude -p "hi"`
                          (headless) 발동 → 새 창 즉시 시작. 5분 쿨다운으로 중복 발동 방지
Services/StatusLineProvisioner.cs  동시 실행 시 프로필 settings.json 에 claude statusLine 설치(👤 이메일·플랜·이름·세션%). powershell -File <ps1>, ps1 은 prefix 바이트 + stdin five_hour 세션%
Services/PlanFormatter.cs   구독→플랜 라벨(Pro/Max 5x) 공유 포맷(목록 뱃지 + statusLine)
Services/AppPaths/ClaudeConfig/CredentialsReader/AutoStart/ExplorerMenu  경로/설정/자동실행/탐색기메뉴
Localization/              LocalizationManager(런타임 스캔) + LocExtension + <culture>.json 14개(_culture/_name 메타)
Models/Profile.cs          프로필 모델(계산 속성). SessionRemaining/SessionPercent(메모리 캐시) 포함
Models/AppData.cs          profiles.json 영속 데이터(Profiles, ActiveProfileId, LastWorkingDir, Shell, RunAsAdmin, SkipPermissions, Language, StatusLine)
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
powershell Installer\build-installer.ps1                  # 인스톨러 빌드 -> dist\Claude-Account-Switcher-Setup_v<버전>-x64.exe
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
- **winget 정식 등록 완료**(2026-07): `winget install akon47.ClaudeAccountSwitcher` 동작(0.5.0부터 게시).
  이후 릴리스는 release 워크플로의 `winget` 잡(winget-releaser)이 winget-pkgs 업데이트 PR을 자동 생성
- **계정 간 세션 이어하기(resume)**: 메인창 툴바 🕘 → 세션 브라우저. 소스 계정의 대화 세션 목록(이름=ai-title 포함)을 보고,
  대상 계정을 골라 "이어하기"하면 그 세션 `.jsonl`을 대상 프로필 폴더의 같은 `projects\<enc>`로 복사하고
  `claude --resume <id>`를 원본 cwd에서 새 창으로 실행. 사본이므로 원본은 소스 계정에 보존(포크).
  **원본 cwd 가 이 PC 에 없으면 폴더를 물어(ProjectFolderEncoder 로 재인코딩) 그 폴더에서 이어하기**(예전엔 중단).
  `Launcher.LaunchInProfile(..., resumeSessionId)` 로 실행.
- **세션 내보내기/가져오기(다른 PC 이식)**: 세션 브라우저에서 다중선택(Ctrl/Shift) 후 "내보내기" → 단일 번들
  `*.claudesession`(zip: 본문+사이드카/서브에이전트+manifest) 저장(선택 없으면 목록 전체). 다른 PC 에서 "파일에서
  가져오기"로 그 번들을 열면 안의 세션들(이름/미리보기)이 목록에 뜨고, 로컬 계정을 골라 이어하기 가능. 원본 폴더가
  이 PC 에 없으면 위 폴더 선택 흐름으로 진행(SessionBundle.Export/Read + ProjectFolderEncoder).
  **작업 폴더 포함(옵션)**: 내보낼 때 "작업 폴더도 포함할까요?"를 크기와 함께 물어봄(무거운/재생성 폴더 제외). 포함하면
  번들이 cwd 내용을 담고, 가져와 이어할 때 원본 폴더가 없으면 "복원할까요?"→위치 선택→그 자리에 풀고 그 폴더에서 resume.
  목록에서 폴더 포함 세션은 📁 로 표시. 내보내기/가져오기/복원은 진행률 다이얼로그(취소 가능)로 처리(큰 폴더 대비).
- **세션 자동 유지**: 프로필별 토글(목록 "세션 유지" 체크박스). 켜면 그 계정의 5시간 창이 리셋되는 즉시
  `claude -p "hi"`(headless, 창 없음)로 새 창을 시작시킴. 활성 프로필은 ~/.claude, 그 외는 CLAUDE_CONFIG_DIR
  격리본으로 발동. 트레이 앱 상주 중에만 동작(서비스 분리해도 PC 켜짐·로그인 전제는 동일해 앱 내부 감시자로 둠).
  판정: **유효한 usage 응답**인데 5시간 창 resets_at 가 **null(=100%, 활성 창 없음)이거나 지났으면 발동**
  (예전엔 null 을 놓쳐 100%+리셋없음에 고착되던 구멍이 있었음). usage 가 null(무료/조회실패)이거나 미래면 미발동.
  주간(seven_day) 한도 소진 시엔 어차피 못 쓰므로 미발동.
- **세션 사용량 표시(UsageService)**: oauth/usage 의 `five_hour`(5시간) + `seven_day`(주간) 파싱. 평소엔 5시간
  잔여%+리셋 카운트다운 표시. **주간 소진 시(seven_day 잔여 0) 0%로 표시하고 카운트다운을 주간 리셋까지로 전환**
  (SessionUsage.DisplayPercent/DisplayResetsAt). 주간 소진이어도 keep-alive 5시간 판정엔 원본 five_hour resets_at 사용.
- **다시 로그인(재로그인)**: 하단 버튼 바 "다시 로그인". 구독/플랜이 바뀌면 재로그인해야 새 값이 읽히는데,
  삭제 후 재추가 대신 **프로필을 유지한 채 재로그인**한다. `ProfileStore.PrepareRelogin` 가 프로필 폴더의
  `.credentials.json` 을 백업 후 삭제하고 **`.claude.json` 의 `oauthAccount` 도 제거**(온보딩/폴더 신뢰 상태는 보존)
  → `Launcher.LaunchLogin` 이 격리(`CLAUDE_CONFIG_DIR`)로 **`claude auth login`** 을 띄워 로그인 화면이 곧바로 열린다.
  (예전엔 자격증명만 지우고 평범한 `claude` 를 띄웠는데, `oauthAccount` 가 남아 있어 claude 가 "로그인됨"으로 보고
  REPL 로 들어가 → 첫 대화에서야 "Run /login" 오류가 났다. 그래서 두 가지를 같이 손본다.)
  스킵권한은 로그인 실행과 무관하므로 묻지 않는다. 활성 프로필이어도 ~/.claude(라이브 토큰)는 안 건드림
  (격리 폴더 사본만 비움 → 현재 활성 세션 유지). 화면 표시용 이메일은 `oauthAccount.json` 사본에서 계속 읽는다.
  로그인 후 [새로고침]하면 갱신된 플랜/이메일이 반영.
- **토큰 만료 표시("다시 로그인 필요")**: `UsageService` 가 결과를 `UsageResult(Usage, UsageState)` 로 돌려준다 —
  `Ok` / `Unauthorized`(리프레시 토큰이 **4xx 로 거부**됨·토큰 없음 = 재로그인만이 답) / `Unavailable`(오프라인·429·5xx·무료
  플랜 등 불확실). Unauthorized 면 목록 상태가 **활성/로그인됨이 아니라 "다시 로그인 필요"**(주황 표시등,
  `AccountStatus.Expired`)로 바뀌고 트레이 라벨에도 같은 문구가 붙는다(`Profile.NeedsRelogin` 메모리 캐시).
  캐시는 TTL(5분) 외에 **자격증명 파일의 수정 시각**도 키로 삼는다 → 재로그인/토큰 회전 직후 바로 반영된다.
- **관리자 권한으로 실행**: 설정 > 실행 셸 아래 체크박스(`AppData.RunAsAdmin`). 켜면 새 창(전환 후 실행/새 창 실행/
  탐색기 메뉴/세션 이어하기)을 `Verb=runas` 로 띄운다 — 앱 자신은 일반 권한이라 **실행할 때마다 UAC 창**이 뜨고,
  취소(ERROR_CANCELLED 1223)는 오류로 취급하지 않고 조용히 넘어간다. 로그인 실행(새 계정 추가/다시 로그인)은
  브라우저 인증이 끼어들어 승격하지 않는다.

## 남은 일 / 다음 후보

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
- **관리자 권한 실행과 CLAUDE_CONFIG_DIR**: `Verb=runas`(ShellExecute) 경로에선 `ProcessStartInfo.EnvironmentVariables`
  를 쓸 수 없다(설정해 두면 Start 에서 예외). 그래서 격리 폴더는 **셸 명령 안에서** 건다
  (`cmd: set "CLAUDE_CONFIG_DIR=…" & …` / `pwsh: $env:CLAUDE_CONFIG_DIR='…'; …`). 일반 실행도 같은 방식으로
  통일했으니 한쪽만 되돌리지 말 것(경로에 공백·`&`·작은따옴표가 들어가도 이 인용 방식으로 안전).
- 설치 후엔 자동실행/탐색기메뉴 토글을 껐다 켜서 레지스트리가 설치된 exe 경로를 가리키게.
- 커밋 작성자는 이 repo 한정 `Kim, Hwan <akon47@naver.com>` (로컬 git config).
- 외부 프로젝트명/브랜드는 코드·주석·식별자·경로 어디에도 넣지 말 것(사용자 요청).
- 작업 방식: 모호하면 먼저 묻고, 큰 변경은 작은 마일스톤으로 나눠 진행(사용자 선호).
- **릴리스 모델**: `main`에 **일반 커밋**을 선형으로 쌓는다(squash/force-push 안 함 — CONTRIBUTING.md 기준).
  배포 시 `<Version>`을 올린 커밋을 푸시하고 `vX.Y.Z` 태그를 밀면 `release` 워크플로가 자기완결 인스톨러를
  빌드해 GitHub Release로 올린다(공개되면 `winget` 워크플로가 PR 생성). 브랜치는 `main` 유지(master로 안 바꿈).
- **winget 자동 제출은 패키지가 winget-pkgs에 이미 존재해야 성공**(winget-releaser는 업데이트만 처리).
  최초 등록(0.5.0) 심사 중에 낸 릴리스(0.6.0~0.7.3)는 winget 잡이 실패했음(continue-on-error라 런은 초록으로 보임).
  이런 경우 버전을 새로 올릴 필요 없이 해당 release 런의 **winget 잡만 재실행**하면 그 태그 버전으로 제출된다.
- **winget 매니페스트 아키텍처(중요)**: NSIS 스텁은 x86 PE 라 komac(winget-releaser)이 파일 분석만으로는
  아키텍처를 x86 으로 오검지 → 게시된 x64 매니페스트와 불일치로 winget-pkgs 검증이 거부("Missing x64 installer",
  0.7.3 PR 에서 실제 발생 → fork 브랜치에서 x86→x64 수동 수정으로 해결). komac 은 **URL 에 x64 가 있으면 그 값을
  우선**하므로 인스톨러 파일명에 `-x64` 를 붙였다(Setup.nsi OutFile + release.yml installers-regex, v0.7.3 이후).
  파일명을 바꾸거나 접미사를 빼지 말 것. `Target amd64-unicode`(진짜 x64 스텁)는 표준 NSIS 배포판에 스텁이
  없어(로컬 3.11·choco 동일) 쓸 수 없다. 구버전 자동 업데이트는 무영향(UpdateService 는 "Setup" 포함 + .exe 로 매칭).
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
