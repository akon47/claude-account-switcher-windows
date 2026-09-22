using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>
/// 세션 자동 유지 방식 설정 다이얼로그 VM: "항상"(리셋 즉시 재시작) 또는 "시간표"(원하는 첫 리셋 시각 + 하루 창 개수).
/// 시간표를 고르면 창 시작 시각과 하루의 리셋 시각들을 미리보기로 보여준다.
/// </summary>
public partial class KeepAliveDialogViewModel : ObservableObject
{
    private static LocalizationManager L => LocalizationManager.Instance;

    /// <summary>콤보 항목(값 + 표시 라벨). 시/분은 두 자리("06"), 개수는 그대로.</summary>
    public sealed record Option(int Value, string Label);

    public string ProfileName { get; init; } = "";

    public string Title => L.Tr("KaDlgTitle", ProfileName);

    public Option[] Hours { get; } = Enumerable.Range(0, 24).Select(h => new Option(h, h.ToString("00"))).ToArray();

    public Option[] Minutes { get; } = Enumerable.Range(0, 12).Select(i => new Option(i * 5, (i * 5).ToString("00"))).ToArray();

    public Option[] Counts { get; } = Enumerable
        .Range(KeepAliveSchedule.MinWindowsPerDay, KeepAliveSchedule.MaxWindowsPerDay - KeepAliveSchedule.MinWindowsPerDay + 1)
        .Select(n => new Option(n, n.ToString()))
        .ToArray();

    /// <summary>"항상" 모드 선택 여부. 라디오 두 개가 이 값과 <see cref="IsSchedule"/> 에 각각 바인딩된다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSchedule))]
    private bool _isAlways = true;

    /// <summary>"시간표" 모드 선택 여부(= !IsAlways). 라디오 그룹이 한쪽을 끄면 다른 쪽이 켜지도록 setter 를 둔다.</summary>
    public bool IsSchedule
    {
        get => !IsAlways;
        set => IsAlways = !value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private Option _selectedHour;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private Option _selectedMinute;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private Option _selectedCount;

    /// <summary>창의 DialogResult로 흘려보낼 값(DialogResultBehavior). 확인 시 true.</summary>
    [ObservableProperty]
    private bool? _dialogResult;

    public KeepAliveDialogViewModel()
    {
        _selectedHour = Hours[11];
        _selectedMinute = Minutes[0];
        _selectedCount = Counts[^1];
    }

    /// <summary>현재 입력값으로 만든 시간표.</summary>
    public KeepAliveSchedule Schedule => new()
    {
        FirstResetAt = new TimeSpan(SelectedHour.Value, SelectedMinute.Value, 0),
        WindowsPerDay = SelectedCount.Value,
    };

    /// <summary>미리보기: "창 시작 06:00 → 리셋 11:00 · 16:00 · 21:00 · 02:00".</summary>
    public string Preview => L.Tr("KaPreviewFmt",
        KeepAliveScheduler.FormatTime(KeepAliveScheduler.FirstStartOf(Schedule)),
        KeepAliveScheduler.FormatResetTimes(Schedule));

    /// <summary>확정 결과: 항상이면 null, 시간표면 그 값.</summary>
    public KeepAliveSchedule? Result => IsAlways ? null : Schedule;

    /// <summary>프로필의 현재 설정으로 입력값을 채운다(null = 항상).</summary>
    public void LoadFrom(KeepAliveSchedule? schedule)
    {
        if (schedule is null)
        {
            IsAlways = true;
            return;
        }

        var n = schedule.Normalized();
        IsAlways = false;
        int hour = (int)n.FirstResetAt.TotalHours;
        int minute = (int)Math.Round(n.FirstResetAt.Minutes / 5.0) * 5; // 5분 단위로 반올림(손으로 고친 값 대비)
        if (minute >= 60) { minute = 0; hour = (hour + 1) % 24; }
        SelectedHour = Hours[hour];
        SelectedMinute = Minutes[minute / 5];
        SelectedCount = Counts[n.WindowsPerDay - KeepAliveSchedule.MinWindowsPerDay];
    }

    [RelayCommand]
    private void Accept() => DialogResult = true;
}
