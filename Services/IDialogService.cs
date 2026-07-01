namespace ClaudeAccountSwitcher.Services;

/// <summary>스킵 권한 다이얼로그 결과. (취소하면 null 로 반환)</summary>
public sealed record SkipPermissionsResult(bool SkipPermissions, bool Remember);

/// <summary>테마가 적용된 다이얼로그를 띄우는 서비스. ViewModel은 WPF 창을 직접 참조하지 않는다.</summary>
public interface IDialogService
{
    /// <summary>텍스트 입력. 취소하면 null.</summary>
    string? ShowInput(string title, string message, string defaultValue = "");

    /// <summary>예/아니오 확인. 예면 true.</summary>
    bool Confirm(string title, string message);

    void ShowInfo(string title, string message);

    void ShowError(string message);

    /// <summary>--dangerously-skip-permissions 부여 여부를 묻는다. 취소하면 null.</summary>
    SkipPermissionsResult? AskSkipPermissions(string profileName);

    /// <summary>설정 창을 모달로 연다.</summary>
    void ShowSettings();

    /// <summary>계정별 세션 훑어보기 + 다른 계정으로 이어하기(resume) 창을 모달로 연다.</summary>
    void ShowSessionBrowser();
}
