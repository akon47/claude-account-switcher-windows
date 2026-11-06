using System.Text;
using ClaudeAccountSwitcher.Localization;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 사용량(<see cref="SessionUsage"/>)을 화면 문자열로 바꾼다. 목록 행·트레이 메뉴·툴팁이 공유한다.
/// 세션 창이 없는 플랜(인증된 Ultra)은 "주간 85%" 처럼 주간 값을 주 표시로 쓴다.
/// </summary>
public static class UsageFormatter
{
    private static LocalizationManager L => LocalizationManager.Instance;

    /// <summary>목록/트레이의 짧은 표시: "73%" 또는 (세션 창 없음) "주간 85%". 미조회면 "—".</summary>
    public static string Inline(SessionUsage? u) => u switch
    {
        null => "—",
        { HasSessionWindow: false } => L.Tr("SessionWeeklyInline", u.DisplayPercent),
        _ => $"{u.DisplayPercent:0}%",
    };

    /// <summary>
    /// 주 표시 창의 리셋 카운트다운. 반환: (Inline = "↻ 2시간 13분", Tooltip = 전체 문장).
    /// 리셋 시각이 없거나 이미 지났으면 ("", null). 절대 시각을 쓰므로 조회가 몇 분 전이어도 표시 시점 기준으로 정확하다.
    /// </summary>
    public static (string Inline, string? Tip) Reset(SessionUsage? u)
    {
        if (u?.DisplayResetsAt is not { } at) return ("", null);
        var span = at - DateTimeOffset.Now;
        if (span <= TimeSpan.Zero) return ("", null); // 이미 리셋됨 → 카운트다운 숨김

        string compact = Remaining(span);
        bool weekly = u.DisplayIsWeekly;
        string tip = weekly
            ? L.Tr(u.WeeklyExhausted ? "WeeklyResetsTip" : "WeeklyResetsTipNormal", compact, at.ToLocalTime().ToString("g"))
            : L.Tr("SessionResetsTip", compact, at.ToLocalTime().ToString("t"));
        return ("↻ " + compact, tip); // "↻"(U+21BB) 리셋 글리프
    }

    /// <summary>남은 시간을 "5일 3시간" / "2시간 13분" / "7분" 꼴로.</summary>
    public static string Remaining(TimeSpan span)
    {
        if (span.TotalDays >= 1) return L.Tr("SessionResetsDh", (int)span.TotalDays, span.Hours);
        int hours = (int)span.TotalHours;
        int mins = span.TotalMinutes < 1 ? 1 : span.Minutes; // 1분 미만도 "1분"으로 보이게
        return hours > 0 ? L.Tr("SessionResetsHm", hours, mins) : L.Tr("SessionResetsM", mins);
    }

    /// <summary>
    /// 상세 툴팁(여러 줄): 세션 창 · 주간 창 · 모델별 범위 한도. 세션 창이 없는 플랜엔 그 설명을 첫 줄에 둔다.
    /// </summary>
    public static string Detail(SessionUsage u)
    {
        StringBuilder sb = new();

        if (u.Session is { } s)
            sb.AppendLine(Line("UsageTipSession", "UsageTipSessionNoReset", s, "t"));
        else
            sb.AppendLine(L["UsageNoSessionWindow"]);

        if (u.Weekly is { } w)
            sb.AppendLine(Line("UsageTipWeekly", "UsageTipWeeklyNoReset", w, "g"));

        foreach (var sc in u.Scoped)
            sb.AppendLine(L.Tr("UsageTipScoped", sc.Label, sc.RemainingPercent) + (sc.IsWarning ? " ⚠" : ""));

        return sb.ToString().TrimEnd();

        static string Line(string keyWithReset, string keyNoReset, UsageWindow w, string timeFormat)
        {
            if (w.ResetsAt is { } at && at > DateTimeOffset.Now)
                return L.Tr(keyWithReset, w.RemainingPercent, Remaining(at - DateTimeOffset.Now), at.ToLocalTime().ToString(timeFormat));
            return L.Tr(keyNoReset, w.RemainingPercent);
        }
    }
}
