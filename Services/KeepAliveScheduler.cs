using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 시간표 모드(<see cref="KeepAliveSchedule"/>)의 5시간 창 슬롯 계산. 모든 시각은 로컬 시각.
/// 하루의 슬롯 = (첫 리셋 시각 − 5h) 부터 5시간 간격으로 WindowsPerDay 개.
/// 어제/오늘/내일의 슬롯을 함께 보므로 자정을 넘는 슬롯(21:00~02:00)과 첫 리셋 시각이 05:00 이전이라
/// 첫 창이 전날에 시작하는 경우도 그대로 다뤄진다.
/// </summary>
public static class KeepAliveScheduler
{
    /// <summary>Claude 의 세션 창 길이(5시간).</summary>
    public static readonly TimeSpan WindowLength = TimeSpan.FromHours(5);

    private const int MinutesPerDay = 1440;

    /// <summary>하루 기준 첫 창 시작 시각(첫 리셋 시각 5시간 전, 0~24h 로 감음).</summary>
    public static TimeSpan FirstStartOf(KeepAliveSchedule s) => Wrap(s.Normalized().FirstResetAt - WindowLength);

    /// <summary>날짜 <paramref name="day"/> 를 기준으로 한 슬롯 시작 시각들(오름차순). 전날/다음 날로 넘어갈 수 있다.</summary>
    public static IEnumerable<DateTime> SlotStartsFor(KeepAliveSchedule s, DateTime day)
    {
        var n = s.Normalized();
        DateTime first = day.Date + n.FirstResetAt - WindowLength;
        for (int k = 0; k < n.WindowsPerDay; k++) yield return first + (k * WindowLength);
    }

    /// <summary>지금 시각을 덮는 슬롯(시작, 끝). 공백 구간이면 null.</summary>
    public static (DateTime Start, DateTime End)? CurrentSlot(KeepAliveSchedule s, DateTime now)
    {
        for (int d = -1; d <= 1; d++)
        {
            foreach (var start in SlotStartsFor(s, now.Date.AddDays(d)))
            {
                if (start <= now && now < start + WindowLength) return (start, start + WindowLength);
            }
        }
        return null;
    }

    /// <summary>지금 이후 가장 가까운 슬롯 시작 시각(다음 창 시작 예정).</summary>
    public static DateTime NextSlotStart(KeepAliveSchedule s, DateTime now)
    {
        DateTime? best = null;
        for (int d = -1; d <= 2; d++)
        {
            foreach (var start in SlotStartsFor(s, now.Date.AddDays(d)))
            {
                if (start > now && (best is null || start < best)) best = start;
            }
        }
        return best ?? now;
    }

    /// <summary>하루의 리셋 시각 목록(표시용: 11:00 · 16:00 · 21:00 · 02:00).</summary>
    public static IEnumerable<TimeSpan> ResetTimesOf(KeepAliveSchedule s)
    {
        var n = s.Normalized();
        for (int k = 0; k < n.WindowsPerDay; k++) yield return Wrap(n.FirstResetAt + (k * WindowLength));
    }

    /// <summary>"HH:mm" 표시.</summary>
    public static string FormatTime(TimeSpan t)
    {
        var w = Wrap(t);
        return $"{(int)w.TotalHours:00}:{w.Minutes:00}";
    }

    /// <summary>리셋 시각 목록을 " · " 로 이은 문자열.</summary>
    public static string FormatResetTimes(KeepAliveSchedule s) =>
        string.Join(" · ", ResetTimesOf(s).Select(FormatTime));

    private static TimeSpan Wrap(TimeSpan t) =>
        TimeSpan.FromMinutes((((int)t.TotalMinutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay);
}
