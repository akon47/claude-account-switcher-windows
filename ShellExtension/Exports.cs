using System.Runtime.InteropServices;
using ClaudeAccountSwitcher.Services;

namespace ClaudeAccountSwitcher.ShellExtension;

/// <summary>COM 인프로세스 서버 진입점(네이티브 내보내기).</summary>
internal static unsafe class Exports
{
    private static readonly Guid CommandClsid = new(ExplorerMenuContract.CommandClsid);

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    private static int DllGetClassObject(Guid* clsid, Guid* iid, nint* result)
    {
        if (clsid is null || iid is null || result is null) return HResult.Pointer;
        *result = 0;
        if (*clsid != CommandClsid) return HResult.ClassNotAvailable;
        try
        {
            nint unknown = Com.Wrappers.GetOrCreateComInterfaceForObject(new ClassFactory(), CreateComInterfaceFlags.None);
            try { return Marshal.QueryInterface(unknown, in *iid, out *result); }
            finally { Marshal.Release(unknown); }
        }
        catch { return HResult.Fail; }
    }

    /// <summary>
    /// 남은 객체가 없으면 S_OK → 서로게이트가 스스로 종료한다. 관리 객체는 참조가 풀려도 GC 가 돌아야 수가 줄기 때문에
    /// 여기서 한 번 수거한다(메뉴를 닫은 뒤 드물게 불리고, 힙이 아주 작아 비용은 무시할 수준).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    private static int DllCanUnloadNow()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return ComObject.Live == 0 && Com.Locks == 0 ? HResult.Ok : HResult.False;
        }
        catch { return HResult.False; }
    }
}
