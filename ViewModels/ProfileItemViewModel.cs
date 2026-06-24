using System.Text.RegularExpressions;
using ClaudeAccountSwitcher.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>행 상태 표시등(왼쪽 동그라미)이 나타내는 계정 상태.</summary>
public enum AccountStatus { NeedLogin, SignedIn, Active }

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
    public string PlanLabel => ResolvePlanLabel(Profile.SubscriptionType, Profile.RateLimitTier);

    // ---------------- 상태 표시등 ----------------
    public AccountStatus StatusKind { get; init; }
    public string Status { get; init; } = "";

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

    private static string ResolvePlanLabel(string? sub, string? tier)
    {
        if (string.IsNullOrWhiteSpace(sub)) return "—";
        string s = sub.Trim();
        string label = s.ToLowerInvariant() switch
        {
            "free" => "Free",
            "pro" => "Pro",
            "max" => "Max",
            "team" => "Team",
            "enterprise" => "Enterprise",
            _ => char.ToUpperInvariant(s[0]) + s[1..],
        };
        // Max 등급의 배수(예: 5x / 20x)가 있으면 함께 표시한다.
        string? mult = ExtractMultiplier(tier);
        return mult is null ? label : $"{label} {mult}";
    }

    private static string? ExtractMultiplier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return null;
        var m = Regex.Match(tier, @"(\d+)\s*x", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value + "x" : null;
    }
}
