using System.Windows.Threading;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 단일 인스턴스 보장. 두 번째 인스턴스는 첫 인스턴스에 "창을 보여라" 신호만 보내고 끝난다 —
/// 트레이 아이콘이 둘 생기거나 설정 파일을 두 프로세스가 번갈아 덮어쓰는 일을 막는다.
/// (탐색기 메뉴의 <c>--launch</c> 실행은 트레이 없이 바로 끝나므로 이 검사를 거치지 않는다.)
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ClaudeAccountSwitcher.SingleInstance";
    private const string ActivateEventName = @"Local\ClaudeAccountSwitcher.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activate;

    /// <summary>첫 인스턴스면 true. 아니면 기존 인스턴스에 활성화 신호를 보내고 false.</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew) return true;

        try
        {
            using var existing = EventWaitHandle.OpenExisting(ActivateEventName);
            existing.Set();
        }
        catch { /* 상대가 아직 이벤트를 만들기 전이거나 종료 중 — 그냥 끝낸다 */ }

        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    /// <summary>다른 인스턴스가 보내는 활성화 신호를 기다리다 UI 스레드에서 <paramref name="onActivate"/> 를 호출한다.</summary>
    public void ListenForActivation(Dispatcher dispatcher, Action onActivate)
    {
        _activate = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ActivateEventName);
        var handle = _activate;
        Thread listener = new(() =>
        {
            try
            {
                while (handle.WaitOne()) dispatcher.InvokeAsync(onActivate);
            }
            catch (ObjectDisposedException) { /* 종료 */ }
        })
        {
            IsBackground = true,
            Name = "SingleInstance.Activate",
        };
        listener.Start();
    }

    public void Dispose()
    {
        _activate?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { /* 소유하지 않았을 수 있음 */ }
        _mutex?.Dispose();
    }
}
