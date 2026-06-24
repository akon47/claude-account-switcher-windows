namespace ClaudeAccountSwitcher.Models;

/// <summary>
/// 앱 영속 데이터. %APPDATA%\ClaudeAccountSwitcher\profiles.json 에 저장된다.
/// </summary>
public class AppData
{
    public List<Profile> Profiles { get; set; } = new();

    /// <summary>현재 ~/.claude 에 활성화된 프로필 Id (없으면 null).</summary>
    public string? ActiveProfileId { get; set; }

    /// <summary>동시 실행 시 마지막으로 선택한 작업 폴더.</summary>
    public string? LastWorkingDir { get; set; }

    /// <summary>새 창 실행에 사용할 셸.</summary>
    public ShellKind Shell { get; set; } = ShellKind.PowerShell;

    /// <summary>
    /// --dangerously-skip-permissions 부여 여부 기억값.
    /// null 이면 실행할 때마다 사용자에게 묻는다. (true/false 면 그 값을 자동 적용)
    /// </summary>
    public bool? SkipPermissions { get; set; }

    /// <summary>표시 언어 컬처(ko-KR/en-US). null 이면 첫 실행 시 윈도우 언어로 결정한다.</summary>
    public string? Language { get; set; }
}
