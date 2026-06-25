using System.Text.RegularExpressions;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 구독 종류(+등급)를 사람이 읽는 플랜 라벨로 변환한다(예: "Pro", "Max 20x").
/// 목록 뱃지(ProfileItemViewModel)와 상태줄(--statusline)이 공유한다.
/// </summary>
public static class PlanFormatter
{
    public static string Format(string? sub, string? tier)
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
