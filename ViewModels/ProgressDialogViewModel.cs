using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>다운로드 등 진행률 표시 다이얼로그의 ViewModel.</summary>
public sealed partial class ProgressDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _status = "";

    /// <summary>0~100 진행률.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _progress;

    public string ProgressText => $"{Progress:0}%";

    /// <summary>취소(취소 버튼·Esc·창 닫기)를 요청할 때 발생. 호출 측이 다운로드 토큰을 취소하도록 구독한다.</summary>
    public event Action? CancelRequested;

    /// <summary>취소 버튼 + Esc + 창 닫힘(WindowClosingCommandBehavior)에서 호출된다. 닫기 자체는 막지 않는다.</summary>
    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();
}
