using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>계정 선택 콤보용 항목(프로필 + 표시 라벨).</summary>
public sealed record AccountChoice(Profile Profile, string Display);

/// <summary>
/// 계정별 대화 세션을 훑어보고, 선택한 세션을 다른 계정으로 이어하기(resume)하는 창의 VM.
/// 세션을 단일 파일로 내보내거나, 다른 PC 에서 만든 번들 파일을 열어(가져오기) 이 PC 계정으로 이어할 수 있다.
/// </summary>
public sealed partial class SessionBrowserViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly SessionStore _sessions;
    private readonly IDialogService _dialogs;
    private readonly LaunchService _launch;

    private static LocalizationManager L => LocalizationManager.Instance;

    /// <summary>세션을 훑어볼 소스 계정 목록(자격증명 있는 프로필).</summary>
    public ObservableCollection<AccountChoice> SourceAccounts { get; } = [];

    /// <summary>이어하기를 실행할 대상 계정 목록.</summary>
    public ObservableCollection<AccountChoice> DestAccounts { get; } = [];

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    /// <summary>목록에서 다중 선택된 항목(내보내기 대상). ListView 의 SelectedItems 를 첨부 동작이 밀어넣는다.</summary>
    public ObservableCollection<object> SelectedSessions { get; } = [];

    [ObservableProperty] private AccountChoice? _selectedSource;
    [ObservableProperty] private AccountChoice? _selectedDest;

    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [ObservableProperty] private SessionItemViewModel? _selectedSession;

    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [ObservableProperty] private bool _isBusy;

    /// <summary>목록이 계정이 아니라 열어둔 번들 파일에서 온 상태인지(가져오기 모드).</summary>
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [ObservableProperty] private bool _isFileSource;

    /// <summary>가져오기 모드일 때 표시할 번들 파일명(없으면 null).</summary>
    [ObservableProperty] private string? _fileSourceName;

    /// <summary>목록이 비었을 때(로딩 끝) 안내를 보여줄지.</summary>
    public bool IsEmpty => !IsBusy && Sessions.Count == 0;

    public SessionBrowserViewModel(ProfileStore store, SessionStore sessions, IDialogService dialogs, LaunchService launch)
    {
        _store = store;
        _sessions = sessions;
        _dialogs = dialogs;
        _launch = launch;

        string? activeId = _store.Data.ActiveProfileId;
        foreach (var p in _store.Data.Profiles.Where(_store.HasCredentials))
        {
            SourceAccounts.Add(new AccountChoice(p, Label(p)));
            DestAccounts.Add(new AccountChoice(p, Label(p)));
        }

        SelectedSource = SourceAccounts.FirstOrDefault(a => a.Profile.Id == activeId) ?? SourceAccounts.FirstOrDefault();
        SelectedDest = DestAccounts.FirstOrDefault(a => a.Profile.Id == activeId) ?? DestAccounts.FirstOrDefault();
    }

    private static string Label(Profile p) =>
        string.IsNullOrEmpty(p.Email) ? p.Name : $"{p.Name}  ({p.Email})";

    partial void OnSelectedSourceChanged(AccountChoice? value)
    {
        if (value is not null) _ = LoadAsync(value); // 파일 모드로 들어가며 선택 해제한 경우엔 재로딩하지 않음
    }

    /// <summary>선택한 소스 계정의 세션을 백그라운드에서 훑어 목록을 채운다.</summary>
    private async Task LoadAsync(AccountChoice source)
    {
        IsFileSource = false;
        FileSourceName = null;
        ReplaceSessions([]);

        IsBusy = true;
        var p = source.Profile;
        bool isActive = p.Id == _store.Data.ActiveProfileId;
        try
        {
            ReplaceSessions(await Task.Run(() => _sessions.ListForProfile(p, isActive)));
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReplaceSessions(IEnumerable<SessionEntry> list)
    {
        Sessions.Clear();
        SelectedSession = null;
        foreach (var e in list) Sessions.Add(new SessionItemViewModel { Entry = e });
        ExportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool CanExport() => !IsFileSource && Sessions.Count > 0;

    /// <summary>선택한 세션(없으면 목록 전체)을 단일 번들 파일로 내보낸다. 작업 폴더 포함 여부를 물어본다.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        var picked = SelectedSessions.OfType<SessionItemViewModel>().ToList();
        var toExport = (picked.Count > 0 ? picked : Sessions.ToList()).Select(vm => vm.Entry).ToList();
        if (toExport.Count == 0) return;

        // 이 PC 에 존재하는 작업 폴더가 있으면, 크기와 함께 포함 여부를 물어본다(무거운 폴더 제외 기준).
        bool includeWorkdirs = false;
        var (folders, bytes) = await Task.Run(() => SessionBundle.ComputeWorkdirInfo(toExport));
        if (folders > 0)
            includeWorkdirs = _dialogs.Confirm(L["SessExportTitle"], L.Tr("SessIncludeFolderAsk", folders, FormatSize(bytes)));

        string suggested = SuggestFileName(toExport.Count);
        string filter = $"{L["SessBundleFilter"]}|*{SessionBundle.FileExtension}";
        if (_dialogs.ShowSaveFile(L["SessExportTitle"], filter, suggested) is not { } dest) return;

        try
        {
            int n = 0;
            bool ok = await _dialogs.RunWithProgress(L["SessExportTitle"], L["SessExporting"],
                (progress, ct) => Task.Run(() => { n = SessionBundle.Export(toExport, dest, AppVersion(), includeWorkdirs, progress, ct); }, ct));
            if (ok) _dialogs.ShowInfo(L["SessExportTitle"], L.Tr("SessExportDone", n, dest));
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    /// <summary>번들 파일(.claudesession)을 열어 안의 세션들을 목록에 채운다(가져오기 모드).</summary>
    [RelayCommand]
    private async Task ImportFromFile()
    {
        string filter = $"{L["SessBundleFilter"]}|*{SessionBundle.FileExtension}|{L["SessAllFiles"]}|*.*";
        if (_dialogs.ShowOpenFile(L["SessImportTitle"], filter) is not { } path) return;

        try
        {
            IReadOnlyList<SessionEntry>? list = null;
            bool ok = await _dialogs.RunWithProgress(L["SessImportTitle"], L["SessImportingFile"],
                (progress, ct) => Task.Run(() => { list = SessionBundle.Read(path, progress, ct); }, ct));
            if (!ok || list is null) return;

            SelectedSource = null; // 계정 선택 해제 → 파일 모드
            IsFileSource = true;
            FileSourceName = Path.GetFileName(path);
            ReplaceSessions(list);

            if (Sessions.Count == 0) _dialogs.ShowInfo(L["SessImportTitle"], L["SessImportEmpty"]);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(L.Tr("SessImportBad", ex.Message));
        }
    }

    private bool CanResume() => SelectedSession is not null;

    /// <summary>선택한 세션을 대상 계정으로 이어한다(폴더 결정·복사·실행은 LaunchService).</summary>
    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task Resume()
    {
        if (SelectedSession is not { } item || SelectedDest is not { } dest) return;
        try { await _launch.ResumeAsync(item.Entry, dest.Profile); }
        catch (Exception ex) { _dialogs.ShowError(ex.Message); }
    }

    /// <summary>내보내기 기본 파일명(계정명 + 날짜 + 세션 수).</summary>
    private string SuggestFileName(int count)
    {
        string acct = SelectedSource?.Profile.Name ?? "sessions";
        string safe = string.Concat(acct.Split(Path.GetInvalidFileNameChars()));
        return $"{safe}-{count}sessions-{DateTime.Now:yyyyMMdd}{SessionBundle.FileExtension}";
    }

    private static string? AppVersion() => Assembly.GetExecutingAssembly().GetName().Version?.ToString();

    /// <summary>바이트 크기를 사람이 읽기 좋은 단위로.</summary>
    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }
}
