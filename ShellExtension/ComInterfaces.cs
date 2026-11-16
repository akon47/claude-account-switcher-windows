using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ClaudeAccountSwitcher.ShellExtension;

// 셸 COM 인터페이스(소스 생성 방식 — NativeAOT 는 런타임 COM 마샬링이 없다).
// 메서드 순서가 곧 vtable 순서이므로 쓰지 않는 메서드도 자리를 지켜 선언한다. 전부 PreserveSig(HRESULT 직접 반환).

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, in Guid iid, out nint instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool doLock);
}

[GeneratedComInterface]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
internal partial interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IShellItemArray? items, out nint title);
    [PreserveSig] int GetIcon(IShellItemArray? items, out nint icon);
    [PreserveSig] int GetToolTip(IShellItemArray? items, out nint tooltip);
    [PreserveSig] int GetCanonicalName(out Guid name);
    [PreserveSig] int GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);
    [PreserveSig] int Invoke(IShellItemArray? items, nint bindContext);
    [PreserveSig] int GetFlags(out uint flags);
    [PreserveSig] int EnumSubCommands(out IEnumExplorerCommand? commands);
}

[GeneratedComInterface]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
internal unsafe partial interface IEnumExplorerCommand
{
    /// <param name="commands">IExplorerCommand* 배열(호출자가 Release).</param>
    /// <param name="fetched">null 일 수 있다(count 가 1 일 때).</param>
    [PreserveSig] int Next(uint count, nint* commands, uint* fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumExplorerCommand? copy);
}

[GeneratedComInterface]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
internal partial interface IShellItemArray
{
    [PreserveSig] int BindToHandler(nint bindContext, in Guid handlerId, in Guid iid, out nint result);
    [PreserveSig] int GetPropertyStore(int flags, in Guid iid, out nint result);
    [PreserveSig] int GetPropertyDescriptionList(nint keyType, in Guid iid, out nint result);
    [PreserveSig] int GetAttributes(int flags, uint mask, out uint attributes);
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemAt(uint index, out IShellItem? item);
    [PreserveSig] int EnumItems(out nint enumerator);
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    [PreserveSig] int BindToHandler(nint bindContext, in Guid handlerId, in Guid iid, out nint result);
    [PreserveSig] int GetParent(out nint parent);
    /// <param name="name">CoTaskMem 문자열(호출자가 해제).</param>
    [PreserveSig] int GetDisplayName(uint form, out nint name);
    [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    [PreserveSig] int Compare(nint other, uint hint, out int order);
}

[GeneratedComInterface]
[Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352")]
internal partial interface IObjectWithSite
{
    [PreserveSig] int SetSite(nint site);
    [PreserveSig] int GetSite(in Guid iid, out nint site);
}

/// <summary>COM 의 IServiceProvider(System.IServiceProvider 와 이름이 겹쳐 따로 부른다).</summary>
[GeneratedComInterface]
[Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
internal partial interface IComServiceProvider
{
    [PreserveSig] int QueryService(in Guid service, in Guid iid, out nint result);
}

/// <summary>폴더 뷰. 빈 공간 우클릭에서 선택 항목이 안 넘어올 때 현재 폴더를 얻는 데만 쓴다(GetFolder).</summary>
[GeneratedComInterface]
[Guid("cde725b0-ccc9-4519-917e-325d72fab4ce")]
internal partial interface IFolderView
{
    [PreserveSig] int GetCurrentViewMode(out uint mode);
    [PreserveSig] int SetCurrentViewMode(uint mode);
    [PreserveSig] int GetFolder(in Guid iid, out nint folder);
}

internal static class HResult
{
    public const int Ok = 0;
    public const int False = 1;
    public const int NotImplemented = unchecked((int)0x80004001);
    public const int NoInterface = unchecked((int)0x80004002);
    public const int Pointer = unchecked((int)0x80004003);
    public const int Fail = unchecked((int)0x80004005);
    public const int ClassNotAvailable = unchecked((int)0x80040111);
    public const int NoAggregation = unchecked((int)0x80040110);
}

/// <summary>EXPCMDSTATE / EXPCMDFLAGS 중 쓰는 값.</summary>
internal static class CommandState
{
    public const uint Enabled = 0;
    public const uint Hidden = 2;
}

internal static class CommandFlags
{
    public const uint Default = 0;
    public const uint HasSubCommands = 1;
}
