namespace ClaudeAccountSwitcher.Models;

/// <summary>
/// 세션 자동 유지의 "시간표" 설정. 프로필에 이 값이 없으면(null) "항상" 모드
/// (5시간 창이 리셋되는 즉시 다음 창 시작, 24시간 내내)로 동작한다.
/// <para>
/// 시간표 모드는 사용자가 원하는 <b>첫 리셋 시각</b>을 기준으로 5시간 창을 정렬한다.
/// 첫 창은 <see cref="FirstResetAt"/> 5시간 전에 시작하고, 그 뒤로 <see cref="WindowsPerDay"/>개의 창을
/// 5시간 간격으로 연속 유지한다. 마지막 창이 끝나면 다음 날 첫 창 시작 시각까지는 발동하지 않는
/// 공백 구간을 둬서 매일 같은 시간표로 되돌아온다(24시간이 5의 배수가 아니라 이 공백이 없으면
/// 창 시각이 매일 밀린다).
/// </para>
/// </summary>
public sealed class KeepAliveSchedule
{
    public const int MinWindowsPerDay = 1;

    /// <summary>하루 최대 창 수. 5개면 25시간이 되어 다음 날 첫 창과 겹치므로 4개(20시간)가 상한.</summary>
    public const int MaxWindowsPerDay = 4;

    /// <summary>첫 리셋 시각(로컬 시각, 하루 기준). 첫 창은 이 시각 5시간 전에 시작한다.</summary>
    public TimeSpan FirstResetAt { get; set; } = new(11, 0, 0);

    /// <summary>하루에 연속으로 유지할 5시간 창 개수(1~4).</summary>
    public int WindowsPerDay { get; set; } = MaxWindowsPerDay;

    /// <summary>값을 유효 범위(하루 안의 시각, 1~4개)로 정리한 사본. 손으로 고친 JSON 도 안전하게 다룬다.</summary>
    public KeepAliveSchedule Normalized() => new()
    {
        FirstResetAt = TimeSpan.FromMinutes((((int)FirstResetAt.TotalMinutes % 1440) + 1440) % 1440),
        WindowsPerDay = Math.Clamp(WindowsPerDay, MinWindowsPerDay, MaxWindowsPerDay),
    };
}
