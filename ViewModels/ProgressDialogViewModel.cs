using CommunityToolkit.Mvvm.ComponentModel;

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
}
