using System.Collections.ObjectModel;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>행 상태 표시등(왼쪽 동그라미)이 나타내는 계정 상태.</summary>
public enum AccountStatus
{
    /// <summary>저장된 자격증명이 아예 없다.</summary>
    NeedLogin,

    /// <summary>자격증명 보관 중(비활성 계정).</summary>
    SignedIn,

    /// <summary>현재 ~/.claude 에 적용된 계정.</summary>
    Active,

    /// <summary>자격증명은 있지만 토큰이 만료·폐기됐다 → 다시 로그인해야 한다.</summary>
    Expired,
}

/// <summary>남은 한도 색 구간. Unknown = 미조회/해당없음.</summary>
public enum SessionLevel { Unknown, High, Medium, Low }

/// <summary>ListView 표시용 프로필 행. 스토어 상태로 목록을 다시 그릴 때마다 생성된다. 행 메뉴의 명령은 <see cref="Owner"/> 에 위임한다.</summary>
public sealed partial class ProfileItemViewModel : ObservableObject
{
    private static LocalizationManager L => LocalizationManager.Instance;

    public required Profile Profile { get; init; }

    /// <summary>행 컨텍스트 메뉴/버튼이 호출하는 명령의 소유자(메인 VM).</summary>
    public required MainViewModel Owner { get; init; }

    public bool IsActive { get; init; }

    /// <summary>자격증명이 저장돼 있는지(전환/실행/세션 메뉴 활성 조건).</summary>
    public bool HasCredentials { get; init; }

    public string Name => Profile.Name;

    /// <summary>인라인 이름 편집 버퍼. 확정 시 RenameProfile 이 이 값으로 프로필 이름을 바꾼다.</summary>
    [ObservableProperty]
    private string _editName = "";

    public string Email => string.IsNullOrEmpty(Profile.Email) ? "—" : Profile.Email;

    // ---------------- 플랜 뱃지 ----------------
    public string PlanLabel => Profile.PlanLabel;
    public PlanKind PlanKind => PlanFormatter.KindOf(PlanLabel);

    // ---------------- 상태 표시등 ----------------

    /// <summary>상태 표시등 종류. 사용량 조회에서 토큰 만료가 확인되면 Expired 로 바뀐다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKeepAlive), nameof(KeepAliveTipEffective))]
    private AccountStatus _statusKind;

    /// <summary>상태 텍스트(활성 / 로그인됨 / 로그인 필요 / 다시 로그인 필요).</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>행 전체 툴팁: 이메일·조직·플랜·마지막 사용.</summary>
    public string RowTip
    {
        get
        {
            List<string> lines = [];
            if (!string.IsNullOrEmpty(Profile.Email)) lines.Add(Profile.Email);
            if (!string.IsNullOrEmpty(Profile.OrganizationName)) lines.Add(L.Tr("TipOrg", Profile.OrganizationName));
            if (PlanLabel != "—") lines.Add(L.Tr("TipPlan", PlanLabel));
            lines.Add(L.Tr("TipLastUsed", Profile.LastUsed is { } t ? t.ToString("g") : L["TipNever"]));
            return string.Join('\n', lines);
        }
    }

    // ---------------- 세션 자동 유지 ----------------

    /// <summary>세션 자동 유지 토글(체크박스). 변경 시 MainViewModel.ToggleKeepAlive 가 프로필에 반영·저장한다.</summary>
    [ObservableProperty]
    private bool _keepAlive;

    /// <summary>이 플랜에 5시간 세션 창이 있는지. null = 아직 모름. false(인증된 Ultra)면 세션 유지가 의미 없어 비활성.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKeepAlive), nameof(KeepAliveTipEffective))]
    private bool? _hasSessionWindow;

    /// <summary>로그인된 계정이고 5시간 창이 있는 플랜일 때만 자동 유지 가능.</summary>
    public bool CanKeepAlive => StatusKind != AccountStatus.NeedLogin && HasSessionWindow != false;

    /// <summary>세션 유지 방식 요약("항상" 또는 "11:00 ×4" = 첫 리셋 시각 × 하루 창 수). 라벨 버튼에 표시, 누르면 설정.</summary>
    public string KeepAliveSummary { get; init; } = "";

    /// <summary>세션 유지 방식 상세 툴팁(시간표면 창 시작·리셋 시각과 다음 시작 예정).</summary>
    public string? KeepAliveTip { get; init; }

    /// <summary>실제 툴팁: 5시간 창이 없는 플랜이면 그 설명으로 대체한다.</summary>
    public string? KeepAliveTipEffective => HasSessionWindow == false ? L["TipKeepAliveNoSession"] : KeepAliveTip;

    // ---------------- 한도 ----------------

    /// <summary>남은 한도 표시 텍스트(예: "73%" / "주간 85%"). 비동기 조회가 끝나면 갱신된다.</summary>
    [ObservableProperty]
    private string _sessionRemaining = "…";

    /// <summary>남은 비율(0~100). null = 미조회/해당없음. 색 구간(SessionLevel)을 결정한다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionLevel))]
    private double? _sessionPercent;

    public SessionLevel SessionLevel => SessionPercent switch
    {
        null => SessionLevel.Unknown,
        >= 50 => SessionLevel.High,
        >= 20 => SessionLevel.Medium,
        _ => SessionLevel.Low,
    };

    /// <summary>리셋까지 남은 시간 표시(예: "↻ 2시간 13분"). 빈 문자열이면 표시하지 않는다.</summary>
    [ObservableProperty]
    private string _sessionResetsIn = "";

    /// <summary>리셋 카운트다운 툴팁(예: "2시간 13분 후 세션 초기화 …"). null 이면 툴팁 없음.</summary>
    [ObservableProperty]
    private string? _sessionResetsTip;

    /// <summary>한도 상세 툴팁(세션·주간·모델별). null 이면 툴팁 없음.</summary>
    [ObservableProperty]
    private string? _usageTip;

    /// <summary>모델별 범위 한도 중 경고 단계가 있어 ⚠ 를 붙일지.</summary>
    [ObservableProperty]
    private bool _hasUsageWarning;

    // ---------------- 최근 세션 메뉴 ----------------

    /// <summary>행 메뉴의 "최근 세션 이어하기" 항목. 메뉴가 열릴 때 <see cref="LoadRecentSessionsCommand"/> 가 채운다.</summary>
    public ObservableCollection<SessionMenuEntry> RecentSessions { get; } = [];

    [RelayCommand]
    private Task LoadRecentSessions() => Owner.LoadRecentSessionsAsync(this);
}
