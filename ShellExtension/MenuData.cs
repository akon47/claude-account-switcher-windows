using ClaudeAccountSwitcher.Services;
using Microsoft.Win32;

namespace ClaudeAccountSwitcher.ShellExtension;

/// <summary>메뉴 한 줄(계정). Command 는 폴더 자리표시자가 든 실행 명령줄.</summary>
internal sealed record MenuEntry(string Label, string Icon, string Command);

/// <summary>
/// 앱이 레지스트리에 동기화해 둔 메뉴 내용(제목·아이콘·계정 목록)을 읽는다. 앱이 꺼져 있어도 메뉴가 뜨도록
/// 프로필 파일이 아니라 레지스트리를 원본으로 삼는다(클래식 메뉴와 같은 데이터 → 두 메뉴가 항상 일치).
/// </summary>
internal sealed record MenuData(string Title, string Icon, IReadOnlyList<MenuEntry> Entries)
{
    public static MenuData Load()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey($@"{ExplorerMenuContract.ClassesRoot}\{ExplorerMenuContract.SubKeyName}");
            if (root is null) return Empty;

            string title = root.GetValue(ExplorerMenuContract.LabelValue) as string ?? "";
            string icon = IconPath(root.GetValue(ExplorerMenuContract.IconValue) as string);

            List<MenuEntry> entries = [];
            using var shell = root.OpenSubKey(ExplorerMenuContract.ShellKey);
            if (shell is not null)
            {
                // 키 이름이 "NNN_<id>" 라 이름순 = 앱의 계정 순서.
                foreach (string name in shell.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
                {
                    using var item = shell.OpenSubKey(name);
                    using var command = item?.OpenSubKey(ExplorerMenuContract.CommandKey);
                    if (item?.GetValue(ExplorerMenuContract.LabelValue) is not string label || command?.GetValue("") is not string line) continue;
                    if (label.Length == 0 || line.Length == 0) continue;
                    entries.Add(new MenuEntry(label, IconPath(item.GetValue(ExplorerMenuContract.IconValue) as string), line));
                }
            }
            return new MenuData(title, icon, entries);
        }
        catch { return Empty; }
    }

    private static MenuData Empty { get; } = new("", "", []);

    /// <summary>레지스트리의 <c>"C:\…\app.exe",0</c> 형식에서 따옴표를 뗀다(IExplorerCommand 아이콘은 따옴표 없는 "경로,인덱스").</summary>
    private static string IconPath(string? value) => value?.Replace("\"", "") ?? "";
}
