using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>
/// 계정별 대화 세션을 훑어보고, 선택한 세션을 다른 계정으로 이어하기(resume)하는 창의 VM.
/// 세션을 단일 파일로 내보내거나, 다른 PC 에서 만든 번들 파일을 열어(가져오기) 이 PC 계정으로 이어할 수 있다.
/// </summary>
public partial class SessionBrowserViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly SessionStore _sessions;
    private readonly IDialogService _dialogs;

    private static LocalizationManager L => LocalizationManager.Instance;

    /// <summary>세션을 훑어볼 소스 계정 목록(자격증명 있는 프로필).</summary>
    public ObservableCollection<AccountChoice> SourceAccounts { get; } = new();

    /// <summary>이어하기를 실행할 대상 계정 목록.</summary>
    public ObservableCollection<AccountChoice> DestAccounts { get; } = new();

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = new();

    /// <summary>목록에서 다중 선택된 항목(내보내기 대상). ListView 의 SelectedItems 를 첨부 동작이 밀어넣는다.</summary>
    public ObservableCollection<object> SelectedSessions { get; } = new();

    [ObservableProperty] private AccountChoice? _selectedSource;
    [ObservableProperty] private AccountChoice? _selectedDest;

    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [ObservableProperty] private SessionItemViewModel? _selectedSession;

    [ObservableProperty] private bool _isBusy;

    /// <summary>목록이 계정이 아니라 열어둔 번들 파일에서 온 상태인지(가져오기 모드).</summary>
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [ObservableProperty] private bool _isFileSource;

    /// <summary>가져오기 모드일 때 표시할 번들 파일명(없으면 null).</summary>
    [ObservableProperty] private string? _fileSourceName;

    /// <summary>목록이 비었을 때(로딩 끝) 안내를 보여줄지.</summary>
    public bool IsEmpty => !IsBusy && Sessions.Count == 0;

    public SessionBrowserViewModel(ProfileStore store, SessionStore sessions, IDialogService dialogs)
    {
        _store = store;
        _sessions = sessions;
        _dialogs = dialogs;

        var activeId = _store.Data.ActiveProfileId;
        foreach (var p in _store.Data.Profiles)
        {
            if (!_store.HasCredentials(p)) continue;
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
        if (value is null) return; // 파일 모드로 들어가며 선택 해제한 경우엔 재로딩하지 않음
        _ = LoadAsync(value);
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>선택한 소스 계정의 세션을 백그라운드에서 훑어 목록을 채운다.</summary>
    private async Task LoadAsync(AccountChoice source)
    {
        IsFileSource = false;
        FileSourceName = null;
        Sessions.Clear();
        SelectedSession = null;
        OnPropertyChanged(nameof(IsEmpty));

        IsBusy = true;
        var p = source.Profile;
        bool isActive = p.Id == _store.Data.ActiveProfileId;
        try
        {
            var list = await Task.Run(() => _sessions.ListForProfile(p, isActive));
            foreach (var e in list)
                Sessions.Add(new SessionItemViewModel { Entry = e });
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
            ExportCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private bool CanExport() => !IsFileSource && Sessions.Count > 0;

    /// <summary>선택한 세션(없으면 목록 전체)을 단일 번들 파일로 내보낸다. 작업 폴더 포함 여부를 물어본다.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        var picked = SelectedSessions.OfType<SessionItemViewModel>().ToList();
        var toExport = (picked.Count > 0 ? picked : Sessions.ToList())
            .Select(vm => vm.Entry).ToList();
        if (toExport.Count == 0) return;

        // 이 PC 에 존재하는 작업 폴더가 있으면, 크기와 함께 포함 여부를 물어본다(무거운 폴더 제외 기준).
        bool includeWorkdirs = false;
        var (folders, bytes) = await Task.Run(() => SessionBundle.ComputeWorkdirInfo(toExport));
        if (folders > 0)
        {
            includeWorkdirs = _dialogs.Confirm(
                L["SessExportTitle"], L.Tr("SessIncludeFolderAsk", folders, FormatSize(bytes)));
        }

        string suggested = SuggestFileName(picked.Count > 0 ? picked.Count : Sessions.Count);
        string filter = $"{L["SessBundleFilter"]}|*{SessionBundle.FileExtension}";
        string? dest = _dialogs.ShowSaveFile(L["SessExportTitle"], filter, suggested);
        if (dest is null) return;

        try
        {
            int n = 0;
            bool ok = await _dialogs.RunWithProgress(L["SessExportTitle"], L["SessExporting"],
                (progress, ct) => Task.Run(() => { n = SessionBundle.Export(toExport, dest, AppVersion(), includeWorkdirs, progress, ct); }, ct));
            if (ok)
                _dialogs.ShowInfo(L["SessExportTitle"], L.Tr("SessExportDone", n, dest));
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
        string? path = _dialogs.ShowOpenFile(L["SessImportTitle"], filter);
        if (path is null) return;

        try
        {
            IReadOnlyList<SessionEntry>? list = null;
            bool ok = await _dialogs.RunWithProgress(L["SessImportTitle"], L["SessImportingFile"],
                (progress, ct) => Task.Run(() => { list = SessionBundle.Read(path, progress, ct); }, ct));
            if (!ok || list is null) return;

            SelectedSource = null;       // 계정 선택 해제 → 파일 모드
            IsFileSource = true;
            FileSourceName = Path.GetFileName(path);
            Sessions.Clear();
            SelectedSession = null;
            foreach (var e in list)
                Sessions.Add(new SessionItemViewModel { Entry = e });
            ExportCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsEmpty));

            if (Sessions.Count == 0)
                _dialogs.ShowInfo(L["SessImportTitle"], L["SessImportEmpty"]);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(L.Tr("SessImportBad", ex.Message));
        }
    }

    private bool CanResume() => SelectedSession is not null;

    /// <summary>선택한 세션을 대상 계정으로 복사한 뒤 <c>claude --resume</c> 로 새 창에서 이어한다.</summary>
    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task Resume()
    {
        var item = SelectedSession;
        var dest = SelectedDest;
        if (item is null || dest is null) return;

        var entry = item.Entry;

        // resume 은 어떤 작업 폴더에서 실행되고, 세션 파일은 그 폴더의 enc 폴더에서 찾는다.
        // 원본 cwd 가 이 PC 에 있으면 그대로, 없으면(다른 PC 로 옮겨온 경우 등) 번들에 담긴 폴더를 복원하거나
        // 사용자가 폴더를 고르고, 그 폴더 기준으로 enc 를 다시 계산해 복사한다.
        var target = await ResolveTargetFolder(entry);
        if (target is null) return; // 취소

        // 다른 계정으로 이어하면 이후 대화는 대상 계정 사본에 쌓인다(원본 보존). 한 번 확인.
        bool crossAccount = dest.Profile.Id != entry.ProfileId;
        if (crossAccount &&
            !_dialogs.Confirm(L["SessResumeTitle"], L.Tr("SessResumeConfirm", entry.ProfileName, dest.Profile.Name)))
        {
            return;
        }

        var skip = ResolveSkipPermissions(dest.Profile.Name);
        if (skip is null) return; // 취소

        try
        {
            string id = _sessions.ImportInto(entry, dest.Profile, target.Value.ProjectFolder);
            Launcher.LaunchInProfile(dest.Profile, target.Value.Cwd, _store.Data.Shell, skip.Value, _store.Data.StatusLine, resumeSessionId: id, runAsAdmin: _store.Data.RunAsAdmin);
            dest.Profile.LastUsed = DateTime.Now;
            _store.Save();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// 이어할 작업 폴더와 그에 맞는 enc 폴더명을 정한다. 원본 cwd 가 있으면 그대로;
    /// 없고 번들에 폴더가 담겼으면 사용자가 고른 위치로 복원해 그 폴더에서; 그 외엔 폴더를 직접 물어
    /// enc 를 다시 계산한다. 취소하면 null.
    /// </summary>
    private async Task<(string Cwd, string ProjectFolder)?> ResolveTargetFolder(SessionEntry entry)
    {
        // 원본 폴더가 이 PC 에 있으면 그대로 사용(로컬 파일 재사용).
        if (!string.IsNullOrEmpty(entry.Cwd) && Directory.Exists(entry.Cwd))
            return (entry.Cwd, entry.ProjectFolder);

        // (A) 번들에 작업 폴더가 함께 담김 → 복원할지 물어보고, 예면 위치를 골라 복원.
        if (!string.IsNullOrEmpty(entry.BundleWorkdirPath) && Directory.Exists(entry.BundleWorkdirPath))
        {
            string folderName = FolderNameOf(entry.Cwd);
            if (_dialogs.Confirm(L["SessResumeTitle"], L.Tr("SessRestoreFolderAsk", folderName)))
            {
                string? parent = _dialogs.PickFolder(L["SessRestoreParentTitle"], AppPaths.UserHome);
                if (string.IsNullOrEmpty(parent)) return null;

                string destFolder = Path.Combine(parent, folderName);
                try
                {
                    bool ok = await _dialogs.RunWithProgress(L["SessResumeTitle"], L.Tr("SessRestoring", folderName),
                        (progress, ct) => Task.Run(() => SessionBundle.RestoreWorkdir(entry.BundleWorkdirPath!, destFolder, progress, ct), ct));
                    if (!ok) return null; // 복원 취소
                }
                catch (Exception ex)
                {
                    _dialogs.ShowError(ex.Message);
                    return null;
                }
                return (destFolder, ProjectFolderEncoder.Encode(destFolder));
            }
            // 아니오 → 아래 '폴더 직접 선택'으로 넘어간다.
        }

        // (B) 폴더가 안 담겼거나 복원을 원치 않음 → 이어할 폴더를 직접 고르게 안내.
        string shown = string.IsNullOrEmpty(entry.Cwd) ? "?" : entry.Cwd;
        if (!_dialogs.Confirm(L["SessResumeTitle"], L.Tr("SessPickFolderAsk", shown)))
            return null;

        string? folder = _dialogs.PickFolder(L["SessPickFolderTitle"], AppPaths.UserHome);
        if (string.IsNullOrEmpty(folder)) return null;

        return (folder, ProjectFolderEncoder.Encode(folder));
    }

    /// <summary>스킵 권한 값을 결정한다(기억값 우선, 없으면 물어봄). 취소 시 null.</summary>
    private bool? ResolveSkipPermissions(string profileName)
    {
        if (_store.Data.SkipPermissions is bool remembered) return remembered;

        var res = _dialogs.AskSkipPermissions(profileName);
        if (res is null) return null;
        if (res.Remember)
        {
            _store.Data.SkipPermissions = res.SkipPermissions;
            _store.Save();
        }
        return res.SkipPermissions;
    }

    /// <summary>내보내기 기본 파일명(계정명 + 날짜 + 세션 수).</summary>
    private string SuggestFileName(int count)
    {
        string acct = SelectedSource?.Profile.Name ?? "sessions";
        string safe = string.Concat(acct.Split(Path.GetInvalidFileNameChars()));
        return $"{safe}-{count}sessions-{DateTime.Now:yyyyMMdd}{SessionBundle.FileExtension}";
    }

    private static string? AppVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString();

    /// <summary>작업 폴더 이름(cwd 의 마지막 구성요소). 복원 시 상위 폴더 아래 이 이름으로 만든다.</summary>
    private static string FolderNameOf(string cwd)
    {
        string n = Path.GetFileName((cwd ?? "").TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(n) ? "project" : n;
    }

    /// <summary>바이트 크기를 사람이 읽기 좋은 단위로.</summary>
    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }
}

/// <summary>계정 선택 콤보용 항목(프로필 + 표시 라벨).</summary>
public sealed record AccountChoice(Profile Profile, string Display);
