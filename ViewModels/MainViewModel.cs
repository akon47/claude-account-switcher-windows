using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>관리 창의 메인 ViewModel. 프로필 목록 + 캡처/추가/전환/실행/이름변경/삭제/새로고침/설정.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly IDialogService _dialogs;
    private readonly UsageService _usage;
    private int _usageGen;

    private static LocalizationManager L => LocalizationManager.Instance;

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

    [ObservableProperty]
    private ProfileItemViewModel? _selectedProfile;

    /// <summary>목록이 바뀌어 트레이 메뉴/탐색기 서브메뉴를 갱신해야 할 때 발생.</summary>
    public event Action? Changed;

    /// <summary>세션 사용량 조회가 끝나 트레이 메뉴 라벨만 갱신하면 될 때 발생.</summary>
    public event Action? UsageUpdated;

    public MainViewModel(ProfileStore store, IDialogService dialogs, UsageService usage)
    {
        _store = store;
        _dialogs = dialogs;
        _usage = usage;
    }

    /// <summary>스토어 상태로 목록을 다시 그린다. (외부 변경 후에도 호출)</summary>
    /// <param name="forceUsage">true면 사용량 캐시를 무시하고 새로 조회한다(새로고침 버튼).</param>
    public void ReloadFromStore(bool forceUsage = false)
    {
        _store.RefreshAll();
        var selectedId = SelectedProfile?.Profile.Id;
        Profiles.Clear();
        var activeId = _store.Data.ActiveProfileId;
        foreach (var p in _store.Data.Profiles)
        {
            bool isActive = p.Id == activeId;
            bool hasCreds = _store.HasCredentials(p);
            AccountStatus kind = isActive ? AccountStatus.Active : (hasCreds ? AccountStatus.SignedIn : AccountStatus.NeedLogin);
            string status = isActive ? L["StatusActive"] : (hasCreds ? L["StatusSignedIn"] : L["StatusNeedLogin"]);
            // 마지막 조회값이 있으면 즉시 보여주고, 없으면 로딩/미표시 상태로 시작
            string usage = !hasCreds ? "—" : (p.SessionRemaining ?? "…");
            Profiles.Add(new ProfileItemViewModel
            {
                Profile = p, IsActive = isActive, StatusKind = kind, Status = status,
                SessionRemaining = usage, SessionPercent = hasCreds ? p.SessionPercent : null, EditName = p.Name,
            });
        }
        if (selectedId is not null)
            SelectedProfile = Profiles.FirstOrDefault(r => r.Profile.Id == selectedId);

        _ = RefreshUsageAsync(forceUsage);
    }

    /// <summary>각 프로필의 세션 남은 사용량을 비동기로 조회해 행/트레이 라벨을 갱신한다.</summary>
    private async Task RefreshUsageAsync(bool force)
    {
        int gen = ++_usageGen;
        var items = Profiles.ToList();
        var activeId = _store.Data.ActiveProfileId;

        var tasks = items.Select(async item =>
        {
            var p = item.Profile;
            if (!_store.HasCredentials(p))
            {
                item.SessionRemaining = p.SessionRemaining = "—";
                item.SessionPercent = p.SessionPercent = null;
                return;
            }

            // 활성 프로필은 ~/.claude 의 살아있는 토큰을, 나머지는 프로필 보관본을 사용한다.
            string path = p.Id == activeId ? AppPaths.ClaudeCredentials : p.CredentialsPath;
            var usage = await _usage.GetSessionUsageAsync(path, p.Id, force);
            if (gen != _usageGen) return; // 더 최신 새로고침이 시작됨

            string text = usage is null ? "—" : $"{usage.RemainingPercent:0}%";
            item.SessionRemaining = p.SessionRemaining = text;
            item.SessionPercent = p.SessionPercent = usage?.RemainingPercent;
        });

        try { await Task.WhenAll(tasks); } catch { /* 개별 실패는 "—"로 처리됨 */ }
        if (gen == _usageGen) UsageUpdated?.Invoke();
    }

    /// <summary>창이 활성화될 때(앞으로 올 때) 목록을 다시 그린다. WindowActivated 동작에서 호출.</summary>
    [RelayCommand]
    private void Reload() => ReloadFromStore();

    /// <summary>드래그로 행 순서를 바꾼다(삽입선 위치 기준). ListView 드래그 동작에서 호출.</summary>
    [RelayCommand]
    private void Reorder(ReorderRequest? request)
    {
        if (request?.Item is not ProfileItemViewModel dragged) return;
        int from = Profiles.IndexOf(dragged);
        if (from < 0) return;

        int insertIndex = Math.Clamp(request.InsertIndex, 0, Profiles.Count);
        // dragged 를 먼저 빼면 그 뒤 항목들이 한 칸 당겨지므로 목적 인덱스를 보정한다.
        int to = from < insertIndex ? insertIndex - 1 : insertIndex;
        if (to == from || to < 0 || to >= Profiles.Count) return;

        Profiles.Move(from, to);
        _store.Reorder(Profiles.Select(p => p.Profile.Id)); // 저장 순서도 표시 순서에 맞춰 영속
        Changed?.Invoke();                                   // 트레이/탐색기 메뉴 순서도 갱신
    }

    private Profile? Selected => SelectedProfile?.Profile;

    [RelayCommand]
    private void Capture()
    {
        if (!File.Exists(AppPaths.ClaudeCredentials))
        {
            _dialogs.ShowInfo(L["MsgCaptureTitle"], L["MsgCaptureNoLogin"]);
            return;
        }

        var acct = ClaudeConfig.FromClaudeJson(ClaudeConfig.HomeConfigPath);

        // 같은 계정(이메일)이 이미 등록돼 있으면 중복 추가하지 않는다.
        if (!string.IsNullOrEmpty(acct?.Email) &&
            _store.Data.Profiles.Any(p => string.Equals(p.Email, acct!.Email, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.ShowInfo(L["MsgCaptureTitle"], L.Tr("MsgCaptureDuplicate", acct!.Email!));
            return;
        }

        string suggest = !string.IsNullOrEmpty(acct?.DisplayName) ? acct!.DisplayName!
            : !string.IsNullOrEmpty(acct?.Email) ? acct!.Email!
            : L.Tr("DefaultAccountName", _store.Data.Profiles.Count + 1);
        string msg = string.IsNullOrEmpty(acct?.Email)
            ? L["MsgCaptureNamePrompt"]
            : L.Tr("MsgCaptureNamePromptEmail", acct!.Email!);

        var name = _dialogs.ShowInput(L["MsgCaptureTitle"], msg, suggest);
        if (string.IsNullOrWhiteSpace(name)) return;

        try { _store.CaptureCurrent(name); ReloadFromStore(); Changed?.Invoke(); }
        catch (Exception ex) { _dialogs.ShowError(ex.Message); }
    }

    [RelayCommand]
    private void AddNew()
    {
        var name = _dialogs.ShowInput(L["MsgAddTitle"], L["MsgAddNamePrompt"], L.Tr("DefaultAccountName", _store.Data.Profiles.Count + 1));
        if (string.IsNullOrWhiteSpace(name)) return;

        var skip = ResolveSkipPermissions(name);
        if (skip is null) return; // 취소

        var p = _store.CreateForLogin(name);
        try
        {
            Launcher.LaunchInProfile(p, null, _store.Data.Shell, skip.Value, _store.Data.StatusLine);
            ReloadFromStore();
            Changed?.Invoke();
            _dialogs.ShowInfo(L["MsgAddTitle"], L["MsgAddInfo"]);
        }
        catch (Exception ex) { _dialogs.ShowError(ex.Message); }
    }

    [RelayCommand]
    private void Switch() => DoSwitch(Selected);

    /// <summary>지정 프로필로 전환. 트레이 빠른 전환에서도 호출된다.</summary>
    public void DoSwitch(Profile? p)
    {
        if (p is null) { _dialogs.ShowInfo(L["MsgSwitchTitle"], L["MsgSwitchSelect"]); return; }
        if (!_store.HasCredentials(p)) { _dialogs.ShowInfo(L["MsgSwitchTitle"], L["MsgSwitchNeedLogin"]); return; }
        if (p.Id == _store.Data.ActiveProfileId) { _dialogs.ShowInfo(L["MsgSwitchTitle"], L["MsgSwitchAlready"]); return; }

        if (IsClaudeRunning() &&
            !_dialogs.Confirm(L["WarnTitle"], L["RunningSwitchWarn"]))
        {
            return;
        }

        try { _store.SwitchTo(p); ReloadFromStore(); Changed?.Invoke(); }
        catch (Exception ex) { _dialogs.ShowError(ex.Message); }
    }

    [RelayCommand]
    private void Launch()
    {
        var p = Selected;
        if (p is null) { _dialogs.ShowInfo(L["MsgLaunchTitle"], L["MsgLaunchSelect"]); return; }
        if (!_store.HasCredentials(p)) { _dialogs.ShowInfo(L["MsgLaunchTitle"], L["MsgLaunchNeedLogin"]); return; }

        string start = _store.Data.LastWorkingDir ?? AppPaths.UserHome;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = L.Tr("MsgLaunchFolderTitle", p.Name),
            InitialDirectory = Directory.Exists(start) ? start : AppPaths.UserHome,
        };
        if (dlg.ShowDialog() != true) return;

        var skip = ResolveSkipPermissions(p.Name);
        if (skip is null) return; // 취소

        try
        {
            Launcher.LaunchInProfile(p, dlg.FolderName, _store.Data.Shell, skip.Value, _store.Data.StatusLine);
            _store.Data.LastWorkingDir = dlg.FolderName;
            p.LastUsed = DateTime.Now;
            _store.Save();
        }
        catch (Exception ex) { _dialogs.ShowError(ex.Message); }
    }

    /// <summary>인라인 편집(EditableTextBlock) 확정 시 호출. item.EditName 으로 이름을 바꾼다.</summary>
    [RelayCommand]
    private void RenameProfile(ProfileItemViewModel? item)
    {
        if (item is null) return;
        var p = item.Profile;
        var name = item.EditName?.Trim() ?? "";

        // 비었거나 변경 없음 → 표시값만 원래대로 되돌리고 종료
        if (string.IsNullOrWhiteSpace(name) || name == p.Name)
        {
            item.EditName = p.Name;
            return;
        }

        _store.Rename(p, name);
        ReloadFromStore();
        Changed?.Invoke();
    }

    /// <summary>각 행의 X 버튼에서 호출. 인자가 없으면 선택된 행을 삭제한다.</summary>
    [RelayCommand]
    private void DeleteProfile(ProfileItemViewModel? item)
    {
        var p = (item ?? SelectedProfile)?.Profile;
        if (p is null) { _dialogs.ShowInfo(L["MsgDeleteTitle"], L["MsgDeleteSelect"]); return; }

        if (!_dialogs.Confirm(L["MsgDeleteTitle"], L.Tr("MsgDeleteConfirm", p.Name)))
            return;

        _store.Delete(p);
        ReloadFromStore();
        Changed?.Invoke();
    }

    [RelayCommand]
    private void Refresh()
    {
        ReloadFromStore(forceUsage: true); // 사용자가 명시적으로 새로고침 → 캐시 무시
        Changed?.Invoke();
    }

    [RelayCommand]
    private void Settings() => _dialogs.ShowSettings();

    /// <summary>스킵 권한 값을 결정한다. 기억값이 있으면 그대로, 없으면 다이얼로그로 묻는다. 취소 시 null.</summary>
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

    internal static bool IsClaudeRunning()
    {
        try { return Process.GetProcessesByName("claude").Length > 0; }
        catch { return false; }
    }
}
