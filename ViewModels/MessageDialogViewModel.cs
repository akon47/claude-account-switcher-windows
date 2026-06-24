using System.Windows;
using ClaudeAccountSwitcher.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeAccountSwitcher.ViewModels;

public enum MessageDialogKind { Info, Error, Question }

/// <summary>정보/오류/확인 메시지 다이얼로그 ViewModel.</summary>
public partial class MessageDialogViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private MessageDialogKind _kind = MessageDialogKind.Info;

    public bool ShowCancel => Kind == MessageDialogKind.Question;
    public Visibility CancelVisibility => ShowCancel ? Visibility.Visible : Visibility.Collapsed;
    public string OkText => LocalizationManager.Instance[Kind == MessageDialogKind.Question ? "DlgYes" : "DlgOk"];
    public string CancelText => LocalizationManager.Instance["DlgNo"];

    partial void OnKindChanged(MessageDialogKind value)
    {
        OnPropertyChanged(nameof(ShowCancel));
        OnPropertyChanged(nameof(CancelVisibility));
        OnPropertyChanged(nameof(OkText));
    }
}
