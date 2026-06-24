using System.IO;
using Microsoft.Win32;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 탐색기 폴더 우클릭 메뉴 "Claude로 실행" + 계정별 서브메뉴 (레지스트리 클래식 방식).
/// HKCU\Software\Classes 아래에 등록하므로 관리자 권한이 필요 없다.
/// (Windows 11에서는 '더 많은 옵션 표시' 메뉴에 나타난다.)
/// </summary>
public static class ExplorerMenu
{
    private const string Verb = "ClaudeAccountSwitcher";
    private const string DisplayName = "Claude로 실행";
    private const string SubKeyName = "ClaudeAccountSwitcher.Sub"; // ExtendedSubCommandsKey (Software\Classes 기준)

    private static string ExePath => Environment.ProcessPath ?? "";
    private static string Classes(string sub) => $@"Software\Classes\{sub}";

    private static readonly string[] TopKeys =
    [
        $@"Directory\shell\{Verb}",            // 폴더 우클릭
        $@"Directory\Background\shell\{Verb}", // 폴더 빈 공간 우클릭
    ];

    public static bool IsInstalled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Classes(TopKeys[0]));
            return k is not null;
        }
        catch { return false; }
    }

    public static void Install(IEnumerable<Profile> profiles)
    {
        string icon = $"\"{ExePath}\",0";
        foreach (var top in TopKeys)
        {
            using var k = Registry.CurrentUser.CreateSubKey(Classes(top));
            if (k is null) continue;
            k.SetValue("MUIVerb", DisplayName);
            k.SetValue("Icon", icon);
            k.SetValue("ExtendedSubCommandsKey", SubKeyName);
        }
        Sync(profiles);
    }

    /// <summary>서브메뉴(계정 목록)를 현재 프로필에 맞춰 다시 구성. 로그인된 계정만.</summary>
    public static void Sync(IEnumerable<Profile> profiles)
    {
        if (!IsInstalled()) return;

        // 기존 서브항목 제거 후 재생성
        try { Registry.CurrentUser.DeleteSubKeyTree(Classes(SubKeyName), throwOnMissingSubKey: false); }
        catch { /* best effort */ }

        string icon = $"\"{ExePath}\",0";
        int order = 0;
        foreach (var p in profiles)
        {
            if (!File.Exists(p.CredentialsPath)) continue; // 로그인된 계정만

            string itemKey = Classes($@"{SubKeyName}\shell\{order:D3}_{p.Id}");
            using var ik = Registry.CurrentUser.CreateSubKey(itemKey);
            if (ik is null) continue;

            string label = string.IsNullOrEmpty(p.Email) ? p.Name : $"{p.Name}  ({p.Email})";
            ik.SetValue("MUIVerb", label);
            ik.SetValue("Icon", icon);

            using var cmd = ik.CreateSubKey("command");
            cmd?.SetValue("", $"\"{ExePath}\" --launch {p.Id} --dir \"%V\"");
            order++;
        }
    }

    public static void Uninstall()
    {
        foreach (var top in TopKeys)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Classes(top), throwOnMissingSubKey: false); }
            catch { /* best effort */ }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree(Classes(SubKeyName), throwOnMissingSubKey: false); }
        catch { /* best effort */ }
    }
}
