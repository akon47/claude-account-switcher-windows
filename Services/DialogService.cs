using System.Windows;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.ViewModels;
using ClaudeAccountSwitcher.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 테마 다이얼로그 구현. 활성 창을 owner 로 잡아 중앙 정렬한다(관리 창이 트레이로 숨어 있으면 화면 중앙).
/// 창 VM 은 DI 컨테이너에서 꺼내므로(SettingsViewModel / SessionBrowserViewModel) 서비스 간 순환 참조가 생기지 않는다.
/// </summary>
public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public string? ShowInput(string title, string message, string defaultValue = "")
    {
        InputDialogViewModel vm = new() { Title = title, Message = message, Input = defaultValue };
        return Show(new InputDialog { DataContext = vm }) ? vm.Input.Trim() : null;
    }

    public bool Confirm(string title, string message) => ShowMessage(title, message, MessageDialogKind.Question);

    public void ShowInfo(string title, string message) => ShowMessage(title, message, MessageDialogKind.Info);

    public void ShowError(string message) => ShowMessage(LocalizationManager.Instance["DlgError"], message, MessageDialogKind.Error);

    public SkipPermissionsResult? AskSkipPermissions(string profileName)
    {
        SkipPermissionsDialogViewModel vm = new() { ProfileName = profileName };
        return Show(new SkipPermissionsDialog { DataContext = vm }) ? new SkipPermissionsResult(vm.SkipChosen, vm.DontAskAgain) : null;
    }

    public void ShowSettings() =>
        Show(new SettingsWindow { DataContext = services.GetRequiredService<SettingsViewModel>() });

    public KeepAliveSettingsResult? ShowKeepAliveSettings(Profile profile)
    {
        KeepAliveDialogViewModel vm = new() { ProfileName = profile.Name };
        vm.LoadFrom(profile.KeepAliveSchedule);
        return Show(new KeepAliveDialog { DataContext = vm }) ? new KeepAliveSettingsResult(vm.Result) : null;
    }

    public void ShowSessionBrowser() =>
        Show(new SessionBrowserWindow { DataContext = services.GetRequiredService<SessionBrowserViewModel>() });

    public string? ShowSaveFile(string title, string filter, string defaultFileName)
    {
        Microsoft.Win32.SaveFileDialog dlg = new()
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? ShowOpenFile(string title, string filter)
    {
        Microsoft.Win32.OpenFileDialog dlg = new() { Title = title, Filter = filter, CheckFileExists = true };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title, string? initialDir)
    {
        Microsoft.Win32.OpenFolderDialog dlg = new() { Title = title };
        if (!string.IsNullOrEmpty(initialDir) && System.IO.Directory.Exists(initialDir)) dlg.InitialDirectory = initialDir;
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public async Task<bool> RunWithProgress(string title, string status, Func<IProgress<double>, CancellationToken, Task> work)
    {
        using CancellationTokenSource cts = new();
        ProgressDialogViewModel vm = new() { Status = status };
        ProgressDialog dlg = new() { DataContext = vm, Title = title };
        SetOwner(dlg);
        vm.CancelRequested += cts.Cancel;
        dlg.Show();

        try
        {
            await work(new Progress<double>(p => vm.Progress = p), cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (dlg.IsVisible) dlg.Close(); // 사용자가 X 로 이미 닫았으면 중복 닫기 방지
        }
    }

    private static bool ShowMessage(string title, string message, MessageDialogKind kind) =>
        Show(new MessageDialog { DataContext = new MessageDialogViewModel { Title = title, Message = message, Kind = kind } });

    /// <summary>owner 를 잡고 모달로 띄운다. 확인/예로 닫혔으면 true.</summary>
    private static bool Show(Window dlg)
    {
        SetOwner(dlg);
        return dlg.ShowDialog() == true;
    }

    private static void SetOwner(Window dlg)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != dlg)
                    ?? Application.Current?.MainWindow;
        if (owner is not null && owner != dlg && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
