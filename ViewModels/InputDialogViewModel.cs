using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>텍스트 입력 다이얼로그 ViewModel.</summary>
public partial class InputDialogViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "입력";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _input = "";
}
