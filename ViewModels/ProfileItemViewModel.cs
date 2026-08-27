using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;

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

/// <summary>플랜 뱃지 색을 결정하는 구독 종류.</summary>
public enum PlanKind { None, Free, Pro, Max, Team, Enterprise, Other }

/// <summary>남은 세션 한도 색 구간. Unknown = 미조회/해당없음.</summary>
public enum SessionLevel { Unknown, High, Medium, Low }

/// <summary>ListView 표시용 프로필 행. 스토어 상태로 목록을 다시 그릴 때마다 생성된다.</summary>
public sealed partial class ProfileItemViewModel : ObservableObject
{
    public required Profile Profile { get; init; }
    public bool IsActive { get; init; }
    public string Name => Profile.Name;

    /// <summary>인라인 이름 편집 버퍼. 확정 시 RenameProfile 이 이 값으로 프로필 이름을 바꾼다.</summary>
    [ObservableProperty]
    private string _editName = "";

    public string Email => string.IsNullOrEmpty(Profile.Email) ? "—" : Profile.Email!;

    // ---------------- 플랜 뱃지 ----------------
    public bool HasPlan => !string.IsNullOrWhiteSpace(Profile.SubscriptionType);
    public PlanKind PlanKind => ResolvePlanKind(Profile.SubscriptionType);
    public string PlanLabel => PlanFormatter.Format(Profile.SubscriptionType, Profile.RateLimitTier);

    // ---------------- 상태 표시등 ----------------

    /// <summary>상태 표시등 종류. 사용량 조회에서 토큰 만료가 확인되면 Expired 로 바뀐다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKeepAlive))]
    private AccountStatus _statusKind;

    /// <summary>상태 텍스트(활성 / 로그인됨 / 로그인 필요 / 다시 로그인 필요).</summary>
    [ObservableProperty]
    private string _status = "";

    // ---------------- 세션 자동 유지 ----------------
    /// <summary>세션 자동 유지 토글(체크박스). 변경 시 MainViewModel.ToggleKeepAlive 가 프로필에 반영·저장한다.</summary>
    [ObservableProperty]
    private bool _keepAlive;

    /// <summary>로그인된 계정만 자동 유지 가능(로그인 필요 상태면 체크박스 비활성).</summary>
    public bool CanKeepAlive => StatusKind != AccountStatus.NeedLogin;

    // ---------------- 세션 한도 ----------------
    /// <summary>세션(5시간) 남은 사용량 표시 텍스트(예: "73%"). 비동기 조회가 끝나면 갱신된다.</summary>
    [ObservableProperty]
    private string _sessionRemaining = "…";

    /// <summary>세션 남은 비율(0~100). null = 미조회/해당없음. 색 구간(SessionLevel)을 결정한다.</summary>
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

    /// <summary>세션 리셋까지 남은 시간 표시(예: "↻ 2시간 13분"). 빈 문자열이면 표시하지 않는다.</summary>
    [ObservableProperty]
    private string _sessionResetsIn = "";

    /// <summary>리셋 카운트다운 툴팁(예: "2시간 13분 후 세션 초기화 …"). null이면 툴팁 없음.</summary>
    [ObservableProperty]
    private string? _sessionResetsTip;

    private static PlanKind ResolvePlanKind(string? sub) => sub?.Trim().ToLowerInvariant() switch
    {
        null or "" => PlanKind.None,
        "free" => PlanKind.Free,
        "pro" => PlanKind.Pro,
        "max" => PlanKind.Max,
        "team" => PlanKind.Team,
        "enterprise" => PlanKind.Enterprise,
        _ => PlanKind.Other,
    };

}
