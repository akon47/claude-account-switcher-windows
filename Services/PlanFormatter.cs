using System.Text.RegularExpressions;

namespace ClaudeAccountSwitcher.Services;

/// <summary>플랜 뱃지 색을 결정하는 구독 종류.</summary>
public enum PlanKind { None, Free, Pro, Max, Ultra, Team, Enterprise, Other }

/// <summary>
/// 구독 종류(+등급)를 사람이 읽는 플랜 라벨로 변환한다. 목록 뱃지(ProfileItemViewModel)와 상태줄이 공유한다.
/// <para>
/// 2026-10-30 Anthropic 이 플랜 이름을 바꿨다: Max 5x → <b>Max</b>, Max 20x → <b>Ultra</b>.
/// 자격증명의 subscriptionType/rateLimitTier 는 그대로(<c>max</c> / <c>default_claude_max_20x</c>)이고,
/// 계정 정보(oauthAccount)에 <c>planDisplayName: "Ultra"</c> 가 새로 들어온다. 서버가 준 표시 이름이 있으면
/// 그것을 그대로 쓰고, 없으면(구버전 로그인) 등급으로 추정한다.
/// </para>
/// </summary>
public static partial class PlanFormatter
{
    /// <summary>표시용 플랜 라벨. 알 수 없으면 "—".</summary>
    public static string Format(string? subscriptionType, string? rateLimitTier, string? planDisplayName = null)
    {
        if (!string.IsNullOrWhiteSpace(planDisplayName)) return planDisplayName.Trim();
        if (string.IsNullOrWhiteSpace(subscriptionType)) return "—";

        string sub = subscriptionType.Trim();
        string? mult = ExtractMultiplier(rateLimitTier);
        return sub.ToLowerInvariant() switch
        {
            "free" => "Free",
            "pro" => "Pro",
            "ultra" => "Ultra",
            "max" when mult is "20x" => "Ultra",
            "max" when mult is null or "5x" => "Max",
            "max" => $"Max {mult}",
            "team" => "Team",
            "enterprise" => "Enterprise",
            _ => char.ToUpperInvariant(sub[0]) + sub[1..] + (mult is null ? "" : $" {mult}"),
        };
    }

    /// <summary>라벨에서 뱃지 색 구간을 정한다("Ultra" → Ultra, "Max 7x" → Max …).</summary>
    public static PlanKind KindOf(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || label == "—") return PlanKind.None;
        string first = label.Split(' ', 2)[0];
        return first.ToLowerInvariant() switch
        {
            "free" => PlanKind.Free,
            "pro" => PlanKind.Pro,
            "max" => PlanKind.Max,
            "ultra" => PlanKind.Ultra,
            "team" => PlanKind.Team,
            "enterprise" => PlanKind.Enterprise,
            _ => PlanKind.Other,
        };
    }

    /// <summary>등급 문자열의 배수(예: default_claude_max_20x → "20x").</summary>
    private static string? ExtractMultiplier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return null;
        var m = MultiplierRegex().Match(tier);
        return m.Success ? m.Groups[1].Value + "x" : null;
    }

    [GeneratedRegex(@"(\d+)\s*x", RegexOptions.IgnoreCase)]
    private static partial Regex MultiplierRegex();
}
