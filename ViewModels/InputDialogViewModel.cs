using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>텍스트 입력 다이얼로그 ViewModel.</summary>
public partial class InputDialogViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "입력";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _input = "";

    /// <summary>창의 DialogResult로 흘려보낼 값(DialogResultBehavior). 확인 시 true.</summary>
    [ObservableProperty] private bool? _dialogResult;

    [RelayCommand]
    private void Accept() => DialogResult = true;
}
