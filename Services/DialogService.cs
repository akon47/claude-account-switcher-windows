using System.Windows;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.ViewModels;
using ClaudeAccountSwitcher.Views;

namespace ClaudeAccountSwitcher.Services;

/// <summary>테마 다이얼로그 구현. 활성 창을 owner로 잡아 중앙 정렬한다.</summary>
public sealed class DialogService : IDialogService
{
    private readonly ProfileStore _store;
    private readonly SessionStore _sessions;

    public DialogService(ProfileStore store, SessionStore sessions)
    {
        _store = store;
        _sessions = sessions;
    }

    public string? ShowInput(string title, string message, string defaultValue = "")
    {
        var vm = new InputDialogViewModel { Title = title, Message = message, Input = defaultValue };
        var dlg = new InputDialog { DataContext = vm };
        SetOwner(dlg);
        return dlg.ShowDialog() == true ? vm.Input.Trim() : null;
    }

    public bool Confirm(string title, string message) => ShowMessage(title, message, MessageDialogKind.Question);

    public void ShowInfo(string title, string message) => ShowMessage(title, message, MessageDialogKind.Info);

    public void ShowError(string message) => ShowMessage(LocalizationManager.Instance["DlgError"], message, MessageDialogKind.Error);

    public SkipPermissionsResult? AskSkipPermissions(string profileName)
    {
        var vm = new SkipPermissionsDialogViewModel { ProfileName = profileName };
        var dlg = new SkipPermissionsDialog { DataContext = vm };
        SetOwner(dlg);
        return dlg.ShowDialog() == true
            ? new SkipPermissionsResult(vm.SkipChosen, vm.DontAskAgain)
            : null;
    }

    public void ShowSettings()
    {
        var dlg = new SettingsWindow { DataContext = new SettingsViewModel(_store) };
        SetOwner(dlg);
        dlg.ShowDialog();
    }

    public void ShowSessionBrowser()
    {
        var dlg = new SessionBrowserWindow { DataContext = new SessionBrowserViewModel(_store, _sessions, this) };
        SetOwner(dlg);
        dlg.ShowDialog();
    }

    private static bool ShowMessage(string title, string message, MessageDialogKind kind)
    {
        var vm = new MessageDialogViewModel { Title = title, Message = message, Kind = kind };
        var dlg = new MessageDialog { DataContext = vm };
        SetOwner(dlg);
        return dlg.ShowDialog() == true;
    }

    private static void SetOwner(Window dlg)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != dlg)
                    ?? Application.Current?.MainWindow;
        if (owner is not null && owner != dlg && owner.IsVisible)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
