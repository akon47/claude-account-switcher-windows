using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ClaudeAccountSwitcher.ShellExtension;

/// <summary>
/// 살아 있는 COM 객체 수. DllCanUnloadNow 가 이 값으로 "이제 내려도 된다"를 답해야 서로게이트(dllhost)가 종료된다
/// (늘 "안 된다"고 하면 프로세스가 영영 남아 DLL 파일을 잡고 있는다 → 업데이트가 파일을 못 바꾼다).
/// </summary>
internal abstract class ComObject
{
    private static int _live;

    protected ComObject() => Interlocked.Increment(ref _live);

    ~ComObject() => Interlocked.Decrement(ref _live);

    public static int Live => Volatile.Read(ref _live);
}

/// <summary>최상위 항목 "Claude로 실행" — 계정별 하위 명령을 펼친다.</summary>
[GeneratedComClass]
internal sealed unsafe partial class RunWithCommand : ComObject, IExplorerCommand, IObjectWithSite
{
    private readonly MenuData _menu = MenuData.Load();
    private nint _site;

    public int GetTitle(IShellItemArray? items, out nint title) => Com.String(_menu.Title, out title);

    public int GetIcon(IShellItemArray? items, out nint icon) => Com.String(_menu.Icon, out icon);

    public int GetToolTip(IShellItemArray? items, out nint tooltip)
    {
        tooltip = 0;
        return HResult.NotImplemented;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Guid.Empty;
        return HResult.Ok;
    }

    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        // 앱이 메뉴를 등록하지 않았거나 로그인된 계정이 없으면 항목 자체를 숨긴다.
        state = _menu.Title.Length == 0 || _menu.Entries.Count == 0 ? CommandState.Hidden : CommandState.Enabled;
        return HResult.Ok;
    }

    public int Invoke(IShellItemArray? items, nint bindContext) => HResult.Ok; // 하위 명령만 실행된다

    public int GetFlags(out uint flags)
    {
        flags = CommandFlags.HasSubCommands;
        return HResult.Ok;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        commands = new CommandEnumerator([.. _menu.Entries.Select(e => new AccountCommand(e, this))], 0);
        return HResult.Ok;
    }

    public int SetSite(nint site)
    {
        if (_site != 0) Marshal.Release(_site);
        _site = site;
        if (_site != 0) Marshal.AddRef(_site);
        return HResult.Ok;
    }

    public int GetSite(in Guid iid, out nint site)
    {
        site = 0;
        return _site == 0 ? HResult.Fail : Marshal.QueryInterface(_site, in iid, out site);
    }

    /// <summary>
    /// 폴더 빈 공간 우클릭처럼 선택 항목이 넘어오지 않을 때, 사이트(탐색기 창)에 현재 폴더를 물어본다.
    /// </summary>
    public string? FolderFromSite()
    {
        if (_site == 0) return null;
        if (Marshal.QueryInterface(_site, in Com.ServiceProviderId, out nint providerPtr) != HResult.Ok) return null;
        try
        {
            var provider = ComInterfaceMarshaller<IComServiceProvider>.ConvertToManaged((void*)providerPtr);
            if (provider is null || provider.QueryService(in Com.FolderViewId, in Com.FolderViewId, out nint viewPtr) != HResult.Ok || viewPtr == 0) return null;
            try
            {
                var view = ComInterfaceMarshaller<IFolderView>.ConvertToManaged((void*)viewPtr);
                if (view is null || view.GetFolder(in Com.ShellItemId, out nint itemPtr) != HResult.Ok || itemPtr == 0) return null;
                try { return Com.PathOf(ComInterfaceMarshaller<IShellItem>.ConvertToManaged((void*)itemPtr)); }
                finally { Marshal.Release(itemPtr); }
            }
            finally { Marshal.Release(viewPtr); }
        }
        catch { return null; }
        finally { Marshal.Release(providerPtr); }
    }
}

/// <summary>하위 항목: 계정 하나. 고르면 그 폴더에서 해당 계정으로 실행한다.</summary>
[GeneratedComClass]
internal sealed partial class AccountCommand(MenuEntry entry, RunWithCommand owner) : ComObject, IExplorerCommand
{
    public int GetTitle(IShellItemArray? items, out nint title) => Com.String(entry.Label, out title);

    public int GetIcon(IShellItemArray? items, out nint icon) => Com.String(entry.Icon, out icon);

    public int GetToolTip(IShellItemArray? items, out nint tooltip)
    {
        tooltip = 0;
        return HResult.NotImplemented;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Guid.Empty;
        return HResult.Ok;
    }

    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        state = CommandState.Enabled;
        return HResult.Ok;
    }

    public int Invoke(IShellItemArray? items, nint bindContext)
    {
        try
        {
            if ((Com.FirstPath(items) ?? owner.FolderFromSite()) is { Length: > 0 } folder)
                ProcessLauncher.Run(entry.Command, folder);
        }
        catch { /* 메뉴 명령은 실패해도 탐색기로 오류를 올리지 않는다 */ }
        return HResult.Ok;
    }

    public int GetFlags(out uint flags)
    {
        flags = CommandFlags.Default;
        return HResult.Ok;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        commands = null;
        return HResult.NotImplemented;
    }
}

[GeneratedComClass]
internal sealed unsafe partial class CommandEnumerator(AccountCommand[] commands, int start) : ComObject, IEnumExplorerCommand
{
    private int _index = start;

    public int Next(uint count, nint* result, uint* fetched)
    {
        if (result is null) return HResult.Pointer;
        uint done = 0;
        while (done < count && _index < commands.Length)
            result[done++] = (nint)ComInterfaceMarshaller<IExplorerCommand>.ConvertToUnmanaged(commands[_index++]);
        if (fetched is not null) *fetched = done;
        return done == count ? HResult.Ok : HResult.False;
    }

    public int Skip(uint count)
    {
        _index = (int)Math.Min((long)_index + count, commands.Length);
        return _index < commands.Length ? HResult.Ok : HResult.False;
    }

    public int Reset()
    {
        _index = 0;
        return HResult.Ok;
    }

    public int Clone(out IEnumExplorerCommand? copy)
    {
        copy = new CommandEnumerator(commands, _index);
        return HResult.Ok;
    }
}

[GeneratedComClass]
internal sealed partial class ClassFactory : ComObject, IClassFactory
{
    public int CreateInstance(nint outer, in Guid iid, out nint instance)
    {
        instance = 0;
        if (outer != 0) return HResult.NoAggregation;
        try
        {
            nint unknown = Com.Wrappers.GetOrCreateComInterfaceForObject(new RunWithCommand(), CreateComInterfaceFlags.None);
            try { return Marshal.QueryInterface(unknown, in iid, out instance); }
            finally { Marshal.Release(unknown); }
        }
        catch { return HResult.Fail; }
    }

    public int LockServer(bool doLock)
    {
        Com.LockServer(doLock);
        return HResult.Ok;
    }
}

/// <summary>COM 공용 도우미(래퍼·문자열·셸 항목 경로).</summary>
internal static class Com
{
    private const uint FileSystemPath = 0x80058000; // SIGDN_FILESYSPATH

    private static int _locks;

    public static readonly StrategyBasedComWrappers Wrappers = new();

    public static readonly Guid ServiceProviderId = new("6d5140c1-7436-11ce-8034-00aa006009fa");
    public static readonly Guid FolderViewId = new("cde725b0-ccc9-4519-917e-325d72fab4ce"); // IID_IFolderView = SID_SFolderView
    public static readonly Guid ShellItemId = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    public static int Locks => Volatile.Read(ref _locks);

    public static void LockServer(bool doLock)
    {
        if (doLock) Interlocked.Increment(ref _locks);
        else Interlocked.Decrement(ref _locks);
    }

    /// <summary>호출자가 CoTaskMemFree 하는 출력 문자열.</summary>
    public static int String(string value, out nint result)
    {
        result = Marshal.StringToCoTaskMemUni(value);
        return HResult.Ok;
    }

    public static string? FirstPath(IShellItemArray? items)
    {
        if (items is null || items.GetCount(out uint count) != HResult.Ok || count == 0) return null;
        return items.GetItemAt(0, out var item) == HResult.Ok ? PathOf(item) : null;
    }

    /// <summary>셸 항목의 파일 시스템 경로(가상 폴더면 null).</summary>
    public static string? PathOf(IShellItem? item)
    {
        if (item is null || item.GetDisplayName(FileSystemPath, out nint name) != HResult.Ok || name == 0) return null;
        try { return Marshal.PtrToStringUni(name); }
        finally { Marshal.FreeCoTaskMem(name); }
    }
}
