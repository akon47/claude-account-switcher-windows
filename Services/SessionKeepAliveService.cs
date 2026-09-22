using System.IO;
using System.Windows.Threading;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 세션(5시간) 자동 유지 백그라운드 감시자.
/// KeepSessionAlive 가 켜진 프로필을 주기적으로 점검해, 활성 5시간 창이 없으면(= 리셋됐으면)
/// 즉시 claude 에 한마디(headless)를 보내 새 5시간 창을 곧바로 시작시킨다.
///
/// 두 가지 모드(프로필별):
/// - 항상(KeepAliveSchedule == null): 24시간 내내, 창이 없으면 바로 시작. 창 시각은 매일 밀린다.
/// - 시간표(KeepAliveSchedule): 첫 리셋 시각에 맞춰 정렬된 슬롯(첫 리셋−5h 부터 5시간 간격 N개) 안에서만
///   동작한다. 슬롯 안에서 창이 없으면 바로 시작하고, 공백 구간(마지막 창 종료 ~ 다음 날 첫 창 시작)에는
///   조회조차 하지 않아 매일 같은 시간표로 되돌아온다.
///
/// 동작 전제: 트레이 앱이 떠 있어야 한다(앱은 "시작 시 자동 실행"으로 상주). PC 가 꺼져 있거나 절전이면
/// 어떤 방식으로도 발동 불가 — 이는 별도 서비스로 빼도 동일한 한계라 앱 내부 감시자로 둔다.
///
/// 판정 규칙(스팸 방지):
/// - 유효한 usage 응답(five_hour 정보 수신)인데 5시간 창이 없거나(resets_at null = 100%, 활성 창 없음)
///   그 시각이 이미 '지났다' → 활성 창 없음 → 한마디 발동.
/// - resets_at 이 미래면 창이 살아있으므로 발동하지 않는다.
/// - usage 가 null(무료 플랜/조회 실패 등)이면 확신 없음 → 발동하지 않는다.
/// - 주간(7일) 한도가 소진됐으면 발동해도 못 쓰므로 건너뛴다.
/// - 발동 후엔 쿨다운 동안 재발동을 막는다(엔드포인트가 새 창을 반영하기까지의 지연 흡수).
/// </summary>
public sealed class SessionKeepAliveService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    // 발동 직후 usage 엔드포인트가 새 창을 반영하기까지의 지연을 흡수하는 중복발동 방지 쿨다운.
    private static readonly TimeSpan FireCooldown = TimeSpan.FromMinutes(5);

    private readonly ProfileStore _store;
    private readonly UsageService _usage;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DateTime> _lastFired = new();
    private bool _busy;

    public SessionKeepAliveService(ProfileStore store, UsageService usage)
    {
        _store = store;
        _usage = usage;
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    /// <summary>감시 시작(앱 시작 시 1회). 토글이 하나도 안 켜져 있어도 무해하게 돈다. 첫 점검은 곧바로 한 번.</summary>
    public void Start()
    {
        _timer.Start();
        _ = TickAsync();
    }

    private async Task TickAsync()
    {
        if (_busy) return; // 직전 틱이 아직 진행 중이면 건너뛴다(겹침 방지)
        _busy = true;
        try
        {
            var activeId = _store.Data.ActiveProfileId;
            var targets = _store.Data.Profiles
                .Where(p => p.KeepSessionAlive && _store.HasCredentials(p))
                .ToList(); // 컬렉션 변경 대비 스냅샷

            foreach (var p in targets)
            {
                if (_lastFired.TryGetValue(p.Id, out var t) && DateTime.UtcNow - t < FireCooldown)
                    continue;

                // 시간표 모드: 공백 구간(마지막 창 종료 ~ 다음 날 첫 창 시작)에는 조회조차 하지 않는다.
                // (그래야 다음 날 첫 창이 정확히 시간표대로 시작한다 — 여기서 발동하면 시각이 밀린다.)
                if (p.KeepAliveSchedule is { } schedule && KeepAliveScheduler.CurrentSlot(schedule, DateTime.Now) is null)
                    continue;

                // 활성 프로필이라도 ~/.claude 가 정말 이 계정일 때만 라이브 토큰으로 발동한다.
                // (전환 없이 오래 두면 ActiveProfileId 와 ~/.claude 의 실제 계정이 어긋날 수 있다 → 남의 계정에 발동 금지.)
                bool useHome = p.Id == activeId && File.Exists(AppPaths.ClaudeCredentials) && ProfileStore.HomeBelongsTo(p);
                var sources = _store.CredentialSources(p); // 활성이면 ~/.claude 우선, 아니면 프로필 보관본

                // 캐시된 창이 곧 끝나거나(2분 내) 이미 지났거나 창 정보가 애매하면(resets_at 없음)
                // 강제 새로고침으로 리셋 순간을 놓치지 않는다.
                var cached = (await _usage.GetSessionUsageAsync(sources, p.Id)).Usage;
                bool soonOrPast = cached is not null && (cached.ResetsAt is null || cached.ResetsAt <= DateTimeOffset.UtcNow.AddMinutes(2));
                var usage = soonOrPast ? (await _usage.GetSessionUsageAsync(sources, p.Id, force: true)).Usage : cached;

                // 유효한 usage 응답(=활성 5시간 창 정보를 받음)인데 5시간 창이 없거나(resets_at 없음, 100%)
                // 이미 지났다 = 활성 창 없음 → 한마디로 새 5시간 창을 시작한다.
                // (usage 가 null 이면 무료 플랜/조회 실패 등 불확실 → 발동 안 함.)
                // 단, 주간 한도가 소진됐으면 발동해도 어차피 못 쓰므로 건너뛴다(무의미한 재시도 방지).
                bool noActiveWindow = usage is not null && !usage.WeeklyExhausted
                    && (usage.ResetsAt is null || usage.ResetsAt <= DateTimeOffset.UtcNow);
                if (noActiveWindow)
                {
                    Launcher.FireKeepAlive(p, useHome);
                    _lastFired[p.Id] = DateTime.UtcNow;
                }
            }
        }
        catch { /* best effort — 다음 틱에 다시 시도 */ }
        finally { _busy = false; }
    }
}
