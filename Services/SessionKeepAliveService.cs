using System.Windows.Threading;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 세션(5시간) 자동 유지 백그라운드 감시자.
/// KeepSessionAlive 가 켜진 프로필을 주기적으로 점검해, 5시간 창이 리셋되면(= resets_at 가 지났으면)
/// 즉시 claude 에 한마디(headless)를 보내 새 5시간 창을 곧바로 시작시킨다.
///
/// 동작 전제: 트레이 앱이 떠 있어야 한다(앱은 "시작 시 자동 실행"으로 상주). PC 가 꺼져 있거나 절전이면
/// 어떤 방식으로도 발동 불가 — 이는 별도 서비스로 빼도 동일한 한계라 앱 내부 감시자로 둔다.
///
/// 판정 규칙(스팸 방지):
/// - usage 엔드포인트에서 resets_at 을 받았고 그 시각이 '지났다' = 활성 창 없음(다음 첫 메시지 전까지 시계 멈춤)
///   → 한마디 발동.
/// - resets_at 이 미래면 창이 살아있으므로 발동하지 않는다.
/// - usage 가 null(무료 플랜/조회 실패 등)이면 확신 없음 → 발동하지 않는다.
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

    /// <summary>감시 시작(앱 시작 시 1회). 토글이 하나도 안 켜져 있어도 무해하게 돈다.</summary>
    public void Start() => _timer.Start();

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

                bool isActive = p.Id == activeId;
                // 활성 프로필은 ~/.claude 의 라이브 토큰을, 그 외엔 프로필 보관본을 쓴다(UsageService 와 동일).
                string path = isActive ? AppPaths.ClaudeCredentials : p.CredentialsPath;

                // 캐시된 창이 곧 끝나거나 이미 지났으면 강제 새로고침으로 리셋 순간을 놓치지 않는다.
                var cached = await _usage.GetSessionUsageAsync(path, p.Id);
                bool soonOrPast = cached?.ResetsAt is { } r && r <= DateTimeOffset.UtcNow.AddMinutes(2);
                var usage = soonOrPast ? await _usage.GetSessionUsageAsync(path, p.Id, force: true) : cached;

                // resets_at 을 받았고 그 시각이 지났다 = 활성 창 없음 → 한마디로 새 5시간 창 시작.
                if (usage?.ResetsAt is { } reset && reset <= DateTimeOffset.UtcNow)
                {
                    Launcher.FireKeepAlive(p, isActive);
                    _lastFired[p.Id] = DateTime.UtcNow;
                }
            }
        }
        catch { /* best effort — 다음 틱에 다시 시도 */ }
        finally { _busy = false; }
    }
}
