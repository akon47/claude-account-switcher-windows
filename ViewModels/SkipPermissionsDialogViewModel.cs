using ClaudeAccountSwitcher.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>claude 실행 시 --dangerously-skip-permissions 부여 여부를 묻는 다이얼로그 VM.</summary>
public partial class SkipPermissionsDialogViewModel : ObservableObject
{
    public string ProfileName { get; init; } = "";

    /// <summary>체크 시 선택한 값을 기억하고 다음부터 묻지 않는다.</summary>
    [ObservableProperty] private bool _dontAskAgain;

    public string Message => LocalizationManager.Instance.Tr("SkipDlgMessage", ProfileName);
}
