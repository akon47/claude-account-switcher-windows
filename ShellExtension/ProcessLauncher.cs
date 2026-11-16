using System.Runtime.InteropServices;
using ClaudeAccountSwitcher.Services;

namespace ClaudeAccountSwitcher.ShellExtension;

/// <summary>레지스트리에 적힌 명령줄에 폴더를 끼워 넣어 실행한다(클래식 메뉴에서 탐색기가 하던 일).</summary>
internal static unsafe partial class ProcessLauncher
{
    public static void Run(string commandTemplate, string folder)
    {
        string commandLine = commandTemplate.Replace(ExplorerMenuContract.FolderPlaceholder, InsideQuotes(folder), StringComparison.OrdinalIgnoreCase);

        // CreateProcessW 는 명령줄 버퍼를 고쳐 쓸 수 있어야 한다(끝에 널 포함).
        char[] buffer = [.. commandLine, '\0'];
        StartupInfo startup = new() { Size = (uint)sizeof(StartupInfo) };
        fixed (char* line = buffer)
        {
            if (!CreateProcessW(null, line, 0, 0, false, 0, 0, Directory.Exists(folder) ? folder : null, ref startup, out var process)) return;
            CloseHandle(process.Thread);
            CloseHandle(process.Process);
        }
    }

    /// <summary>
    /// 따옴표로 감싼 인자 안에 넣을 경로. 끝의 역슬래시는 두 배로 적는다 — 드라이브 루트 <c>C:\</c> 가 <c>"C:\"</c> 로 들어가면
    /// <c>\"</c> 가 따옴표 이스케이프로 읽혀 인자가 깨진다.
    /// </summary>
    private static string InsideQuotes(string path)
    {
        int trailing = path.Length - path.AsSpan().TrimEnd('\\').Length;
        return trailing == 0 ? path : path + new string('\\', trailing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2Size;
        public nint Reserved2, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [LibraryImport("kernel32", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(string? application, char* commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string? currentDirectory,
        ref StartupInfo startup, out ProcessInformation process);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
