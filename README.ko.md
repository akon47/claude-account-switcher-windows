# Claude Account Switcher

> 여러 **Claude Code(CLI)** 계정을 Windows에서 전환·동시 실행 — 시스템 트레이에서.

*[English README](README.md)*

**Claude Account Switcher**는 Windows 시스템 트레이에 상주하며 여러 Claude Code(CLI) 로그인
계정을 관리합니다. 각 계정은 격리된 *프로필*로 보관되어, 활성 계정을 클릭 한 번으로 바꾸거나
여러 계정을 동시에 병렬로 사용할 수 있습니다.

## 무엇을 하나요

- **전환 (Switch)** — 저장된 계정을 클릭 한 번으로 활성 계정으로. 평소 쓰는 `claude` 명령이
  바로 그 계정으로 동작합니다.
- **동시 실행 (Concurrent)** — 계정마다 격리된 설정 폴더를 두고, 여러 터미널에서 서로 다른
  계정을 충돌 없이 동시에 사용합니다.

## 어떻게 동작하나요

Claude Code는 로그인 정보를 `~/.claude/.credentials.json`(OAuth 토큰)에, 계정 정보(이메일·플랜
등)를 `~/.claude.json`의 `oauthAccount`에 저장합니다. 이 앱은 이를 프로필별로 보관합니다.

- **전환**: 선택한 프로필의 `.credentials.json`을 `~/.claude/`로 복사하고, `~/.claude.json`의
  `oauthAccount`도 그 계정으로 패치해 `claude /status`가 올바른 계정을 표시하게 합니다.
  (전환 전 현재 자격증명을 백업하고, 떠나는 계정에는 갱신된 토큰을 되돌려 저장)
- **동시 실행**: 새 터미널을 `CLAUDE_CONFIG_DIR=<프로필 폴더>`로 띄웁니다. `~/.claude`를 건드리지
  않으므로 계정 간 충돌이 없습니다.

프로필 데이터는 `%APPDATA%\ClaudeAccountSwitcher\`에 저장됩니다. (저장소에는 포함되지 않음)

## 기능

- 시스템 트레이 상주, 다크 테마 UI, 커스텀 타이틀바.
- 현재 로그인 계정 캡처, 새 계정 추가(격리 로그인), 전환, 이름 변경, 삭제.
- 선택한 폴더에서 프로필을 새 터미널로 실행.
- **실행 옵션**: **PowerShell / cmd** 선택, `--dangerously-skip-permissions` 부여 여부 선택
  (*다시 묻지 않음*으로 선택값 기억 — **설정**에서 언제든 재설정).
- 탐색기 우클릭 **"Claude로 실행"** 서브메뉴(계정별, 선택).
- Windows 시작 시 자동 실행(선택).

## 설치

### winget (배포 후)

```powershell
winget install akon47.ClaudeAccountSwitcher
```

### 인스톨러

[Releases](https://github.com/akon47/claude-account-switcher-windows/releases)에서
`Claude-Account-Switcher-Setup.exe`를 받아 실행하세요. 자기완결·현재 사용자 설치
(`%LOCALAPPDATA%\Programs`, 관리자 불필요, ~54MB).

## 소스 빌드

**.NET 9 SDK** 필요. 인스톨러 빌드에는 NSIS(`makensis`)가 추가로 필요합니다.

```powershell
dotnet build Claude-Account-Switcher.csproj -c Debug     # 빌드
dotnet run --project Claude-Account-Switcher.csproj      # 실행(트레이)
powershell Installer\build-installer.ps1                 # 인스톨러 빌드 -> dist\Claude-Account-Switcher-Setup.exe
```

## 기술 스택

- .NET 9 / WPF (Windows 전용), MVVM(`Microsoft.Extensions.DependencyInjection` +
  `CommunityToolkit.Mvvm`).
- 자체 디자인 다크 테마; 시스템 트레이는 `H.NotifyIcon.Wpf`.

## 보안 참고

Claude Code 자체가 Windows에서 `.credentials.json`을 평문으로 저장합니다. 이 앱도 동일한 수준으로
계정별 자격증명을 사용자 프로필 폴더(`%APPDATA%`)에 보관합니다. 향후 DPAPI 기반 암호화 저장을 검토합니다.

## 라이선스

[MIT](LICENSE)
