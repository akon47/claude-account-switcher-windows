using System.Collections.ObjectModel;
using System.IO;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>계정별 대화 세션을 훑어보고, 선택한 세션을 다른 계정으로 이어하기(resume)하는 창의 VM.</summary>
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

    [ObservableProperty] private AccountChoice? _selectedSource;
    [ObservableProperty] private AccountChoice? _selectedDest;

    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [ObservableProperty] private SessionItemViewModel? _selectedSession;

    [ObservableProperty] private bool _isBusy;

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

    partial void OnSelectedSourceChanged(AccountChoice? value) => _ = LoadAsync(value);

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>선택한 소스 계정의 세션을 백그라운드에서 훑어 목록을 채운다.</summary>
    private async Task LoadAsync(AccountChoice? source)
    {
        Sessions.Clear();
        SelectedSession = null;
        OnPropertyChanged(nameof(IsEmpty));
        if (source is null) return;

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
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private bool CanResume() => SelectedSession is not null;

    /// <summary>선택한 세션을 대상 계정으로 복사한 뒤 <c>claude --resume</c> 로 새 창에서 이어한다.</summary>
    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        var item = SelectedSession;
        var dest = SelectedDest;
        if (item is null || dest is null) return;

        var entry = item.Entry;

        // resume 은 반드시 원본 작업 폴더에서 실행돼야 세션 파일을 찾는다. 폴더가 없으면 중단.
        if (string.IsNullOrEmpty(entry.Cwd) || !Directory.Exists(entry.Cwd))
        {
            _dialogs.ShowError(L.Tr("SessResumeNoCwd", string.IsNullOrEmpty(entry.Cwd) ? "?" : entry.Cwd));
            return;
        }

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
            string id = _sessions.ImportInto(entry, dest.Profile);
            Launcher.LaunchInProfile(dest.Profile, entry.Cwd, _store.Data.Shell, skip.Value, _store.Data.StatusLine, resumeSessionId: id);
            dest.Profile.LastUsed = DateTime.Now;
            _store.Save();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
        }
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
}

/// <summary>계정 선택 콤보용 항목(프로필 + 표시 라벨).</summary>
public sealed record AccountChoice(Profile Profile, string Display);
