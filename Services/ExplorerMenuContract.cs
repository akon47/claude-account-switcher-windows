namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 탐색기 메뉴 등록 데이터의 계약. 앱(쓰는 쪽, <see cref="ExplorerMenu"/>)과 셸 확장 DLL(읽는 쪽, ShellExtension 프로젝트)이
/// 이 파일을 함께 컴파일한다 — 키 이름이 어긋나 새 메뉴가 조용히 비는 일을 막기 위해 한곳에 둔다.
/// <para>
/// 구조(HKCU\Software\Classes 기준): <c>ClaudeAccountSwitcher.Sub</c> 의 MUIVerb/Icon = 최상위 항목,
/// <c>ClaudeAccountSwitcher.Sub\shell\NNN_&lt;id&gt;</c> 의 MUIVerb/Icon + <c>command</c> 기본값 = 계정별 항목.
/// 클래식 메뉴는 이 키를 ExtendedSubCommandsKey 로 직접 쓰고, Windows 11 새 메뉴는 셸 확장이 같은 키를 읽어 그린다.
/// </para>
/// </summary>
internal static class ExplorerMenuContract
{
    /// <summary>계정 목록이 들어가는 키(Software\Classes 기준). Installer\Setup.nsi 의 제거 구문에도 복제돼 있다.</summary>
    public const string SubKeyName = "ClaudeAccountSwitcher.Sub";

    public const string ClassesRoot = @"Software\Classes";
    public const string LabelValue = "MUIVerb";
    public const string IconValue = "Icon";
    public const string ShellKey = "shell";
    public const string CommandKey = "command";

    /// <summary>명령줄에서 대상 폴더가 들어갈 자리(클래식 메뉴는 탐색기가, 새 메뉴는 셸 확장이 치환한다).</summary>
    public const string FolderPlaceholder = "%V";

    /// <summary>새 메뉴 명령(IExplorerCommand)의 CLSID. Installer\Package\AppxManifest.xml 과 같아야 한다(빌드 스크립트가 검사).</summary>
    public const string CommandClsid = "F8F23DA5-FCF6-4F48-9F05-C4F06BF7F9E8";

    /// <summary>스파스 패키지 이름(Identity Name). winget 식별자와 같은 값.</summary>
    public const string PackageName = "akon47.ClaudeAccountSwitcher";
}
