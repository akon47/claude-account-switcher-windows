using System.Windows.Threading;
using ClaudeAccountSwitcher.Localization;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 업데이트 사용자 흐름: 확인 → 안내/동의 → 진행률 다운로드 → 인스톨러 실행 → 앱 종료 요청.
/// 시작 직후 한 번(네트워크가 올라오기까지 잠깐 기다림)과 이후 6시간마다 조용히 확인한다(설정으로 끌 수 있음).
/// 트레이 메뉴와 설정 창의 "지금 확인"은 수동 호출(결과를 항상 알려준다).
/// </summary>
public sealed class UpdateCoordinator(ProfileStore store, IDialogService dialogs)
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PeriodicInterval = TimeSpan.FromHours(6);

    private static LocalizationManager L => LocalizationManager.Instance;

    private DispatcherTimer? _timer;
    private bool _busy;

    /// <summary>인스톨러를 띄웠으니 앱을 종료해 달라는 요청(App 이 Shutdown).</summary>
    public event Action? RestartRequested;

    /// <summary>자동 확인 시작(앱 시작 시 1회 호출). 설정이 꺼져 있어도 타이머는 돌되 틱마다 설정을 다시 본다.</summary>
    public void StartPeriodic()
    {
        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = PeriodicInterval;
            if (store.Data.AutoUpdateCheck) await CheckAsync(manual: false);
        };
        _timer.Start();
    }

    /// <summary>GitHub Releases 에서 새 버전을 확인하고, 있으면 사용자 동의 후 인스톨러를 받아 실행한다.</summary>
    /// <param name="manual">사용자가 직접 호출했는지. true 면 "최신 버전" 안내/오류도 표시한다.</param>
    public async Task CheckAsync(bool manual)
    {
        if (_busy) return; // 자동 확인과 수동 확인이 겹치지 않게
        _busy = true;
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info is null)
            {
                if (manual) dialogs.ShowInfo(L["UpdTitle"], L["UpdNone"]);
                return;
            }

            string ver = info.Tag.TrimStart('v', 'V');
            if (!dialogs.Confirm(L["UpdTitle"], L.Tr("UpdAvailable", ver))) return;

            string? path = null;
            bool ok = await dialogs.RunWithProgress(L["UpdTitle"], L.Tr("UpdDownloading", ver),
                async (progress, ct) => path = await UpdateService.DownloadAsync(info, progress, ct));
            if (!ok || path is null) return; // 사용자가 취소

            UpdateService.RunInstaller(path);
            RestartRequested?.Invoke(); // 인스톨러가 교체할 수 있도록 종료
        }
        catch (Exception ex)
        {
            if (manual) dialogs.ShowError(ex.Message); // 자동 확인은 실패해도 조용히 넘어간다(오프라인 등)
        }
        finally
        {
            _busy = false;
        }
    }
}
