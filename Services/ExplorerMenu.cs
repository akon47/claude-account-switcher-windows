using System.IO;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using Microsoft.Win32;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 탐색기 폴더 우클릭 메뉴 "Claude로 실행" + 계정별 서브메뉴(레지스트리 클래식 방식).
/// HKCU\Software\Classes 아래에 등록하므로 관리자 권한이 필요 없다. (Windows 11 에서는 '더 많은 옵션 표시' 메뉴에 나타난다.)
/// <para>
/// <b>서브메뉴 갱신은 비파괴적으로</b> 한다. 예전엔 Sync 가 서브키 트리를 통째로 지우고 다시 만들었는데,
/// 그 사이(수 ms~수십 ms)에 탐색기가 메뉴를 열면 계정 목록이 빈 채로 나온다. 격리 실행 중인 claude 가
/// <c>.claude.json</c> 을 쓸 때마다 파일 감시자가 Sync 를 호출하므로 이 창은 생각보다 자주 열렸다
/// ("우클릭을 빨리 두 번 하면 목록이 안 나온다"는 증상). 이제는 바뀐 값만 쓰고 남는 항목만 지운다 —
/// 변경이 없으면 레지스트리에 아무것도 쓰지 않는다.
/// </para>
/// </summary>
public static class ExplorerMenu
{
    // 주의: 아래 Verb/SubKeyName 으로 만드는 레지스트리 경로는 Installer\Setup.nsi 의 Section Uninstall
    //       (DeleteRegKey)에도 문자열로 복제돼 있다. 키 이름을 바꾸면 제거 시 정리가 깨지니 NSI 와 함께 수정할 것.
    private const string Verb = "ClaudeAccountSwitcher";
    private const string SubKeyName = "ClaudeAccountSwitcher.Sub"; // ExtendedSubCommandsKey (Software\Classes 기준)

    // 최상위 메뉴 라벨은 현재 언어로 현지화한다(키 없으면 영어 폴백). 언어 변경 시 App 이 Install 을 다시 호출해 갱신.
    private static string DisplayName => LocalizationManager.Instance["ExplorerRunWith"];

    private static string ExePath => Environment.ProcessPath ?? "";
    private static string IconValue => $"\"{ExePath}\",0";
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
        try
        {
            foreach (string top in TopKeys)
            {
                using var k = Registry.CurrentUser.CreateSubKey(Classes(top));
                if (k is null) continue;
                SetIfChanged(k, "MUIVerb", DisplayName);
                SetIfChanged(k, "Icon", IconValue);
                SetIfChanged(k, "ExtendedSubCommandsKey", SubKeyName);
            }
            Sync(profiles);
        }
        catch { /* best effort — 레지스트리 쓰기 실패해도(설정 토글 setter 포함) 앱은 계속 동작 */ }
    }

    /// <summary>
    /// 서브메뉴(계정 목록)를 현재 프로필에 맞춘다. 로그인된 계정만.
    /// 원하는 상태를 먼저 전부 써 넣고(같으면 건너뜀) 그 다음에 남는 항목을 지우므로, 어느 순간에도 목록이 비지 않는다.
    /// </summary>
    public static void Sync(IEnumerable<Profile> profiles)
    {
        if (!IsInstalled()) return;

        try
        {
            using var shell = Registry.CurrentUser.CreateSubKey(Classes($@"{SubKeyName}\shell"));
            if (shell is null) return;

            Dictionary<string, (string Label, string Command)> desired = new(StringComparer.OrdinalIgnoreCase);
            int order = 0;
            foreach (var p in profiles)
            {
                if (!File.Exists(p.CredentialsPath)) continue; // 로그인된 계정만
                string label = string.IsNullOrEmpty(p.Email) ? p.Name : $"{p.Name}  ({p.Email})";
                desired[$"{order++:D3}_{p.Id}"] = (label, $"\"{ExePath}\" --launch {p.Id} --dir \"%V\"");
            }

            foreach (var (name, (label, command)) in desired)
            {
                using var item = shell.CreateSubKey(name);
                if (item is null) continue;
                SetIfChanged(item, "MUIVerb", label);
                SetIfChanged(item, "Icon", IconValue);
                using var cmd = item.CreateSubKey("command");
                if (cmd is not null) SetIfChanged(cmd, "", command);
            }

            foreach (string stale in shell.GetSubKeyNames().Where(n => !desired.ContainsKey(n)))
            {
                try { shell.DeleteSubKeyTree(stale, throwOnMissingSubKey: false); }
                catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    public static void Uninstall()
    {
        foreach (string key in TopKeys.Append(SubKeyName))
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Classes(key), throwOnMissingSubKey: false); }
            catch { /* best effort */ }
        }
    }

    /// <summary>값이 이미 같으면 쓰지 않는다(탐색기가 보고 있는 키를 불필요하게 건드리지 않기 위해).</summary>
    private static void SetIfChanged(RegistryKey key, string name, string value)
    {
        if (key.GetValue(name) is string existing && existing == value) return;
        key.SetValue(name, value);
    }
}
