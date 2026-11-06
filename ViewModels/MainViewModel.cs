using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>관리 창의 메인 ViewModel. 프로필 목록 + 캡처/추가/전환/실행/이름변경/삭제/새로고침/설정 + 행 메뉴(최근 세션 이어하기).</summary>
public sealed partial class MainViewModel(ProfileStore store, IDialogService dialogs, UsageService usage, SessionStore sessions, LaunchService launch)
    : ObservableObject
{
    private const int RecentSessionsInMenu = 10;
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(3.5);

    private int _usageGen;
    private CancellationTokenSource? _toastCts;

    private static LocalizationManager L => LocalizationManager.Instance;

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchCommand), nameof(LaunchCommand), nameof(ReloginCommand), nameof(DeleteProfileCommand))]
    private ProfileItemViewModel? _selectedProfile;

    /// <summary>등록된 계정이 하나도 없음(빈 상태 안내 표시).</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>사용량 조회가 진행 중(목록 위 얇은 진행 표시줄).</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>창 아래쪽에 잠깐 떠서 사라지는 안내(전환 완료 등).</summary>
    [ObservableProperty]
    private string? _toast;

    [ObservableProperty]
    private bool _isToastVisible;

    /// <summary>관리 창 위치/크기 기억값(WindowPlacementBehavior 가 읽고 쓴다).</summary>
    public WindowPlacement WindowPlacement => store.Data.MainWindowPlacement ??= new();

    /// <summary>목록이 바뀌어 트레이 메뉴/탐색기 서브메뉴를 갱신해야 할 때 발생.</summary>
    public event Action? Changed;

    /// <summary>세션 사용량 조회가 끝나 트레이 메뉴 라벨만 갱신하면 될 때 발생.</summary>
    public event Action? UsageUpdated;

    /// <summary>스토어 상태로 목록을 다시 그린다. (외부 변경 후에도 호출)</summary>
    /// <param name="forceUsage">true면 사용량 캐시를 무시하고 새로 조회한다(새로고침 버튼).</param>
    public void ReloadFromStore(bool forceUsage = false)
    {
        store.RefreshAll();
        string? selectedId = SelectedProfile?.Profile.Id;
        Profiles.Clear();
        string? activeId = store.Data.ActiveProfileId;
        foreach (var p in store.Data.Profiles)
        {
            bool isActive = p.Id == activeId;
            bool hasCreds = store.HasCredentials(p);
            var (kind, status) = ResolveStatus(hasCreds, isActive, p.NeedsRelogin);
            var (kaSummary, kaTip) = DescribeKeepAlive(p);
            ProfileItemViewModel item = new()
            {
                Profile = p, Owner = this, IsActive = isActive, HasCredentials = hasCreds,
                StatusKind = kind, Status = status, EditName = p.Name,
                KeepAlive = p.KeepSessionAlive, KeepAliveSummary = kaSummary, KeepAliveTip = kaTip,
            };
            // 마지막 조회값이 있으면 즉시 보여주고, 없으면 로딩/미표시 상태로 시작
            if (hasCreds) ApplyUsage(item, p.LastUsage, loading: p.LastUsage is null);
            else ApplyUsage(item, null, loading: false);
            Profiles.Add(item);
        }
        IsEmpty = Profiles.Count == 0;
        // 이전 선택을 유지하고, 처음 열렸으면 활성 계정을 기본 선택한다(버튼 활성 상태가 곧바로 의미를 가지게).
        SelectedProfile = Profiles.FirstOrDefault(r => r.Profile.Id == selectedId) ?? Profiles.FirstOrDefault(r => r.IsActive);

        _ = RefreshUsageAsync(forceUsage);
    }

    /// <summary>각 프로필의 사용량을 비동기로 조회해 행/트레이 라벨을 갱신한다.</summary>
    private async Task RefreshUsageAsync(bool force)
    {
        int gen = ++_usageGen;
        var items = Profiles.ToList();
        string? activeId = store.Data.ActiveProfileId;
        IsRefreshing = true;

        var tasks = items.Select(async item =>
        {
            var p = item.Profile;
            if (!store.HasCredentials(p))
            {
                p.LastUsage = null;
                p.NeedsRelogin = false; // 자격증명 자체가 없음 = "로그인 필요"(재로그인 상태가 아님)
                ApplyUsage(item, null, loading: false);
                return;
            }

            // 활성 프로필은 ~/.claude 의 살아있는 토큰을 먼저 보고, 안 되면 프로필 보관본으로 넘어간다.
            var result = await usage.GetSessionUsageAsync(store.CredentialSources(p), p.Id, force);
            if (gen != _usageGen) return; // 더 최신 새로고침이 시작됨

            // 토큰 갱신이 거부되면(만료·폐기) 사용량을 못 읽는 데서 끝나지 않고 계정을 쓸 수 없다
            // → "활성"이 아니라 "다시 로그인 필요"로 보여준다.
            p.NeedsRelogin = result.NeedsRelogin;
            (item.StatusKind, item.Status) = ResolveStatus(true, p.Id == activeId, p.NeedsRelogin);

            p.LastUsage = result.Usage;
            ApplyUsage(item, result.Usage, loading: false);
        });

        try { await Task.WhenAll(tasks); } catch { /* 개별 실패는 "—"로 처리됨 */ }
        if (gen == _usageGen)
        {
            IsRefreshing = false;
            UsageUpdated?.Invoke();
        }
    }

    /// <summary>사용량 결과를 행 표시로 옮긴다. 주간 소진 시 0% + 주간 리셋, 세션 창이 없는 플랜은 주간 값을 주 표시로.</summary>
    private static void ApplyUsage(ProfileItemViewModel item, SessionUsage? u, bool loading)
    {
        item.SessionRemaining = loading ? "…" : UsageFormatter.Inline(u);
        item.SessionPercent = u?.DisplayPercent;
        (item.SessionResetsIn, item.SessionResetsTip) = UsageFormatter.Reset(u);
        item.UsageTip = u is null ? null : UsageFormatter.Detail(u);
        item.HasUsageWarning = u?.HasScopedWarning == true;
        item.HasSessionWindow = u?.HasSessionWindow;
    }

    /// <summary>
    /// 계정 상태(표시등 종류 + 텍스트)를 계산한다.
    /// 자격증명 없음 &gt; 만료(다시 로그인 필요) &gt; 활성 &gt; 로그인됨 순으로 우선한다 —
    /// 토큰이 죽은 계정을 "활성"으로 보여주면 왜 안 되는지 알 수 없기 때문.
    /// </summary>
    private static (AccountStatus Kind, string Text) ResolveStatus(bool hasCredentials, bool isActive, bool needsRelogin)
    {
        if (!hasCredentials) return (AccountStatus.NeedLogin, L["StatusNeedLogin"]);
        if (needsRelogin) return (AccountStatus.Expired, L["StatusExpired"]);
        return isActive
            ? (AccountStatus.Active, L["StatusActive"])
            : (AccountStatus.SignedIn, L["StatusSignedIn"]);
    }

    /// <summary>창이 활성화될 때(앞으로 올 때) 목록을 다시 그린다. WindowActivated 동작에서 호출.</summary>
    [RelayCommand]
    private void Reload() => ReloadFromStore();

    /// <summary>창 위치/크기를 저장한다(WindowPlacementBehavior 가 닫힐 때 호출).</summary>
    [RelayCommand]
    private void SavePlacement() => store.Save();

    /// <summary>세션 자동 유지 토글(체크박스)을 프로필에 반영하고 저장한다. 감시자는 다음 틱에 새 값을 본다.</summary>
    [RelayCommand]
    private void ToggleKeepAlive(ProfileItemViewModel? item)
    {
        if (item is null) return;
        item.Profile.KeepSessionAlive = item.KeepAlive;
        store.Save();
    }

    /// <summary>
    /// 세션 유지 방식(항상 / 시간표) 설정 다이얼로그. 확인하면 저장하고 토글도 켠다
    /// (방식을 정했다는 건 쓰겠다는 뜻이므로 체크까지 따로 하게 하지 않는다). 감시자는 다음 틱에 새 값을 본다.
    /// </summary>
    [RelayCommand]
    private void KeepAliveSettings(ProfileItemViewModel? item)
    {
        if (item is null) return;
        var p = item.Profile;
        if (dialogs.ShowKeepAliveSettings(p) is not { } result) return; // 취소

        p.KeepAliveSchedule = result.Schedule;
        p.KeepSessionAlive = true;
        store.Save();
        ReloadFromStore();
    }

    /// <summary>
    /// 세션 유지 방식 요약("항상" / "11:00 ×4")과 툴팁. 시간표면 창 시작·리셋 시각과 다음 창 시작 예정을 담아
    /// 사용자가 "지금 무엇이 언제 일어나는지"를 목록에서 바로 알 수 있게 한다.
    /// </summary>
    private static (string Summary, string Tip) DescribeKeepAlive(Profile p)
    {
        if (p.KeepAliveSchedule is not { } s) return (L["KeepAliveAlways"], L["KaTipAlways"]);

        var n = s.Normalized();
        string summary = $"{KeepAliveScheduler.FormatTime(n.FirstResetAt)} ×{n.WindowsPerDay}";
        string tip = L.Tr("KaTipSchedule",
            KeepAliveScheduler.FormatTime(KeepAliveScheduler.FirstStartOf(n)),
            KeepAliveScheduler.FormatResetTimes(n),
            KeepAliveScheduler.NextSlotStart(n, DateTime.Now).ToString("g"));
        return (summary, tip);
    }

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
        store.Reorder(Profiles.Select(p => p.Profile.Id)); // 저장 순서도 표시 순서에 맞춰 영속
        Changed?.Invoke();                                  // 트레이/탐색기 메뉴 순서도 갱신
    }

    /// <summary>명령 인자(행 메뉴) 또는 현재 선택(버튼) 중 대상을 정한다.</summary>
    private ProfileItemViewModel? Target(ProfileItemViewModel? item) => item ?? SelectedProfile;

    [RelayCommand]
    private void Capture()
    {
        if (!File.Exists(AppPaths.ClaudeCredentials))
        {
            dialogs.ShowInfo(L["MsgCaptureTitle"], L["MsgCaptureNoLogin"]);
            return;
        }

        var acct = ClaudeConfig.FromClaudeJson(ClaudeConfig.HomeConfigPath);

        // 같은 계정(이메일)이 이미 등록돼 있으면 중복 추가하지 않는다.
        if (acct?.Email is { Length: > 0 } email
            && store.Data.Profiles.Any(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            dialogs.ShowInfo(L["MsgCaptureTitle"], L.Tr("MsgCaptureDuplicate", email));
            return;
        }

        string suggest = acct?.DisplayName is { Length: > 0 } dn ? dn
            : acct?.Email is { Length: > 0 } em ? em
            : L.Tr("DefaultAccountName", store.Data.Profiles.Count + 1);
        string msg = string.IsNullOrEmpty(acct?.Email)
            ? L["MsgCaptureNamePrompt"]
            : L.Tr("MsgCaptureNamePromptEmail", acct.Email);

        string? name = dialogs.ShowInput(L["MsgCaptureTitle"], msg, suggest);
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            store.CaptureCurrent(name);
            ReloadFromStore();
            Changed?.Invoke();
            ShowToast(L.Tr("ToastCaptured", name));
        }
        catch (Exception ex) { dialogs.ShowError(ex.Message); }
    }

    [RelayCommand]
    private void AddNew()
    {
        string? name = dialogs.ShowInput(L["MsgAddTitle"], L["MsgAddNamePrompt"], L.Tr("DefaultAccountName", store.Data.Profiles.Count + 1));
        if (string.IsNullOrWhiteSpace(name)) return;

        var p = store.CreateForLogin(name);
        try
        {
            // 평범한 claude 대신 `claude auth login` 으로 띄운다 — 로그인 화면이 곧바로 열린다.
            Launcher.LaunchLogin(p, store.Data.Shell);
            ReloadFromStore();
            Changed?.Invoke();
            dialogs.ShowInfo(L["MsgAddTitle"], L["MsgAddInfo"]);
        }
        catch (Exception ex) { dialogs.ShowError(ex.Message); }
    }

    private bool CanRelogin(ProfileItemViewModel? item) => Target(item) is not null;

    /// <summary>프로필을 다시 로그인시킨다. 구독/플랜이 바뀌었을 때 갱신용.
    /// 저장된 자격증명을 백업 후 비우고, 격리 로그인으로 claude 를 띄워 로그인 프롬프트를 자동으로 낸다.</summary>
    [RelayCommand(CanExecute = nameof(CanRelogin))]
    private void Relogin(ProfileItemViewModel? item)
    {
        if (Target(item)?.Profile is not { } p) return;
        if (!dialogs.Confirm(L["MsgReloginTitle"], L.Tr("MsgReloginConfirm", p.Name))) return;

        try
        {
            store.PrepareRelogin(p);
            Launcher.LaunchLogin(p, store.Data.Shell);
            ReloadFromStore();
            Changed?.Invoke();
            dialogs.ShowInfo(L["MsgReloginTitle"], L["MsgReloginInfo"]);
        }
        catch (Exception ex) { dialogs.ShowError(ex.Message); }
    }

    private bool CanSwitch(ProfileItemViewModel? item) => Target(item) is { HasCredentials: true, IsActive: false };

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private void Switch(ProfileItemViewModel? item) => DoSwitch(Target(item)?.Profile);

    /// <summary>지정 프로필로 전환. 트레이 빠른 전환에서도 호출된다. 성공하면 true.</summary>
    public bool DoSwitch(Profile? p)
    {
        if (p is null) { dialogs.ShowInfo(L["MsgSwitchTitle"], L["MsgSwitchSelect"]); return false; }
        if (!store.HasCredentials(p)) { dialogs.ShowInfo(L["MsgSwitchTitle"], L["MsgSwitchNeedLogin"]); return false; }
        if (p.Id == store.Data.ActiveProfileId) { dialogs.ShowInfo(L["MsgSwitchTitle"], L["MsgSwitchAlready"]); return false; }

        if (IsClaudeRunning() && !dialogs.Confirm(L["WarnTitle"], L["RunningSwitchWarn"])) return false;

        try
        {
            store.SwitchTo(p);
            ReloadFromStore();
            Changed?.Invoke();
            ShowToast(L.Tr("ToastSwitched", p.Name));
            return true;
        }
        catch (Exception ex)
        {
            dialogs.ShowError(ex.Message);
            return false;
        }
    }

    private bool CanLaunch(ProfileItemViewModel? item) => Target(item) is { HasCredentials: true };

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Launch(ProfileItemViewModel? item)
    {
        if (Target(item)?.Profile is { } p) DoLaunch(p);
    }

    /// <summary>폴더를 물어 이 계정으로 새 창을 띄운다. 트레이 메뉴에서도 호출된다.</summary>
    public void DoLaunch(Profile p)
    {
        if (!store.HasCredentials(p)) { dialogs.ShowInfo(L["MsgLaunchTitle"], L["MsgLaunchNeedLogin"]); return; }
        try { launch.LaunchNewWindow(p); }
        catch (Exception ex) { dialogs.ShowError(ex.Message); }
    }

    /// <summary>인라인 편집(EditableTextBlock) 확정 시 호출. item.EditName 으로 이름을 바꾼다.</summary>
    [RelayCommand]
    private void RenameProfile(ProfileItemViewModel? item)
    {
        if (item is null) return;
        var p = item.Profile;
        string name = item.EditName.Trim();

        // 비었거나 변경 없음 → 표시값만 원래대로 되돌리고 종료
        if (string.IsNullOrWhiteSpace(name) || name == p.Name)
        {
            item.EditName = p.Name;
            return;
        }

        store.Rename(p, name);
        ReloadFromStore();
        Changed?.Invoke();
    }

    private bool CanDelete(ProfileItemViewModel? item) => Target(item) is not null;

    /// <summary>각 행의 X 버튼/메뉴에서 호출. 인자가 없으면 선택된 행을 삭제한다.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteProfile(ProfileItemViewModel? item)
    {
        if (Target(item)?.Profile is not { } p) return;
        if (!dialogs.Confirm(L["MsgDeleteTitle"], L.Tr("MsgDeleteConfirm", p.Name))) return;

        store.Delete(p);
        ReloadFromStore();
        Changed?.Invoke();
        ShowToast(L.Tr("ToastDeleted", p.Name));
    }

    [RelayCommand]
    private void Refresh()
    {
        ReloadFromStore(forceUsage: true); // 사용자가 명시적으로 새로고침 → 캐시 무시
        Changed?.Invoke();
    }

    [RelayCommand]
    private void Settings() => dialogs.ShowSettings();

    [RelayCommand]
    private void Sessions() => dialogs.ShowSessionBrowser();

    /// <summary>행 메뉴: 프로필 격리 폴더를 탐색기로 연다.</summary>
    [RelayCommand]
    private void OpenProfileFolder(ProfileItemViewModel? item)
    {
        if (Target(item)?.Profile is not { } p) return;
        try
        {
            Directory.CreateDirectory(p.ConfigDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{p.ConfigDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { dialogs.ShowError(ex.Message); }
    }

    /// <summary>행 메뉴: 계정 이메일을 클립보드로.</summary>
    [RelayCommand]
    private void CopyEmail(ProfileItemViewModel? item)
    {
        if (Target(item)?.Profile.Email is not { Length: > 0 } email) return;
        if (ClipboardHelper.TrySetText(email)) ShowToast(L.Tr("ToastCopied", email));
    }

    /// <summary>
    /// 행 메뉴의 "최근 세션 이어하기" 목록을 채운다(메뉴가 열릴 때마다). 이미 항목이 있으면 그대로 두고 조용히 갱신하고,
    /// 처음이면 "불러오는 중…" 자리표시자를 먼저 보여준다. 마지막엔 항상 "세션 브라우저에서 모두 보기…".
    /// </summary>
    public async Task LoadRecentSessionsAsync(ProfileItemViewModel item)
    {
        var p = item.Profile;
        if (!item.HasCredentials) return;

        if (item.RecentSessions.Count == 0)
            item.RecentSessions.Add(SessionMenuEntry.Placeholder(SessionMenuEntryKind.Loading));

        bool isActive = p.Id == store.Data.ActiveProfileId;
        IReadOnlyList<SessionEntry> list = [];
        try { list = await Task.Run(() => sessions.ListRecentForProfile(p, isActive, RecentSessionsInMenu)); }
        catch { /* 목록을 못 읽어도 메뉴 자체는 뜬다 */ }

        item.RecentSessions.Clear();
        if (list.Count == 0) item.RecentSessions.Add(SessionMenuEntry.Placeholder(SessionMenuEntryKind.Empty));
        foreach (var e in list)
            item.RecentSessions.Add(SessionMenuEntry.For(e, new RelayCommand(() => _ = ResumeAsync(e, p))));
        item.RecentSessions.Add(SessionMenuEntry.Browse(SessionsCommand));
    }

    /// <summary>세션을 지정 계정으로 이어한다(행 메뉴·트레이 메뉴 공용). 실행됐으면 토스트.</summary>
    public async Task ResumeAsync(SessionEntry entry, Profile dest)
    {
        try
        {
            if (await launch.ResumeAsync(entry, dest)) ShowToast(L.Tr("ToastResumed", dest.Name));
        }
        catch (Exception ex) { dialogs.ShowError(ex.Message); }
    }

    /// <summary>창 아래쪽에 잠깐 떠 있다 사라지는 안내를 띄운다. 연속 호출하면 마지막 것만 남는다.</summary>
    public async void ShowToast(string text)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        Toast = text;
        IsToastVisible = true;
        try
        {
            await Task.Delay(ToastDuration, cts.Token);
            IsToastVisible = false;
        }
        catch (OperationCanceledException) { /* 새 토스트로 교체됨 */ }
    }

    internal static bool IsClaudeRunning()
    {
        try { return Process.GetProcessesByName("claude").Length > 0; }
        catch { return false; }
    }
}
