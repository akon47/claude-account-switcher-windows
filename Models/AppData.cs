namespace ClaudeAccountSwitcher.Models;

/// <summary>앱 영속 데이터. %APPDATA%\ClaudeAccountSwitcher\profiles.json 에 저장된다.</summary>
public sealed class AppData
{
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>현재 ~/.claude 에 활성화된 프로필 Id (없으면 null).</summary>
    public string? ActiveProfileId { get; set; }

    /// <summary>동시 실행 시 마지막으로 선택한 작업 폴더.</summary>
    public string? LastWorkingDir { get; set; }

    /// <summary>새 창 실행에 사용할 셸.</summary>
    public ShellKind Shell { get; set; } = ShellKind.PowerShell;

    /// <summary>
    /// 새 창을 관리자 권한(UAC 승격)으로 띄울지. 앱 자신은 일반 권한이라 켜면 실행할 때마다 UAC 창이 뜬다.
    /// 로그인 실행(새 계정 추가/다시 로그인)은 브라우저 인증 때문에 승격하지 않는다.
    /// </summary>
    public bool RunAsAdmin { get; set; }

    /// <summary>
    /// --dangerously-skip-permissions 부여 여부 기억값.
    /// null 이면 실행할 때마다 사용자에게 묻는다. (true/false 면 그 값을 자동 적용)
    /// </summary>
    public bool? SkipPermissions { get; set; }

    /// <summary>표시 언어 컬처(ko-KR/en-US). null 이면 첫 실행 시 윈도우 언어로 결정한다.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Windows 시작 시 자동 실행 사용자 설정(진짜 값). null = 아직 미초기화(시작 시 레지스트리 상태로 시드).
    /// 레지스트리(HKCU Run)는 업데이트가 구버전 언인스톨러를 돌리면 지워지므로, 이 값을 원본으로 두고
    /// 시작 시 레지스트리를 이 값에 맞춰 복원한다.
    /// </summary>
    public bool? RunAtStartup { get; set; }

    /// <summary>탐색기 우클릭 메뉴 등록 사용자 설정(진짜 값). null = 아직 미초기화. (자동실행과 동일한 복원 로직)</summary>
    public bool? ExplorerMenu { get; set; }

    /// <summary>
    /// 탐색기 메뉴를 Windows 11 새 우클릭 메뉴에도 넣을지(스파스 패키지 등록). 처음 켤 때 UAC 가 한 번 필요해 기본은 꺼짐.
    /// 업데이트로 버전이 바뀌면 시작 시 이 값대로 패키지를 다시 등록한다.
    /// </summary>
    public bool ModernExplorerMenu { get; set; }

    /// <summary>"Windows 11 새 우클릭 메뉴에 표시할 수 있다"는 안내를 이미 보여줬는지(최초 1회만). 옵션을 직접 켜고 꺼도 true 가 된다.</summary>
    public bool ModernMenuHintShown { get; set; }

    /// <summary>동시 실행 시 claude 하단에 계정 상태줄(👤 이메일·플랜·이름·세션%)을 설치할지. 기본 켜짐.</summary>
    public bool StatusLine { get; set; } = true;

    /// <summary>시작 시·주기적으로 GitHub 릴리스에서 새 버전을 확인할지. 끄면 트레이/설정의 수동 확인만 동작.</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    /// <summary>관리 창의 마지막 위치/크기. 다음에 열 때 같은 자리에 복원한다.</summary>
    public WindowPlacement? MainWindowPlacement { get; set; }

    /// <summary>"창을 닫아도 트레이에서 계속 실행됩니다" 안내를 이미 보였는지(최초 1회만).</summary>
    public bool CloseToTrayHintShown { get; set; }
}
