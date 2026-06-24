using ClaudeAccountSwitcher.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>claude 실행 시 --dangerously-skip-permissions 부여 여부를 묻는 다이얼로그 VM.</summary>
public partial class SkipPermissionsDialogViewModel : ObservableObject
{
    public string ProfileName { get; init; } = "";

    /// <summary>체크 시 선택한 값을 기억하고 다음부터 묻지 않는다.</summary>
    [ObservableProperty] private bool _dontAskAgain;

    /// <summary>"권한 건너뛰고 실행"을 선택했으면 true, "그냥 실행"이면 false.</summary>
    [ObservableProperty] private bool _skipChosen;

    /// <summary>창의 DialogResult로 흘려보낼 값(DialogResultBehavior). 두 실행 버튼 모두 true.</summary>
    [ObservableProperty] private bool? _dialogResult;

    public string Message => LocalizationManager.Instance.Tr("SkipDlgMessage", ProfileName);

    [RelayCommand]
    private void ChooseSkip()
    {
        SkipChosen = true;
        DialogResult = true;
    }

    [RelayCommand]
    private void ChooseNoSkip()
    {
        SkipChosen = false;
        DialogResult = true;
    }
}
