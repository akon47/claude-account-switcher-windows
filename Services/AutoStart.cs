using Microsoft.Win32;

namespace ClaudeAccountSwitcher.Services;

/// <summary>Windows 로그인 시 자동 실행 (HKCU ...\Run 레지스트리).</summary>
public static class AutoStart
{
    // 주의: 아래 RunKey/ValueName 은 Installer\Setup.nsi 의 Section Uninstall(DeleteRegValue, line 130)에도
    //       문자열로 복제돼 있다. 이름을 바꾸면 제거 시 레지스트리가 정리되지 않으니 NSI 와 함께 수정할 것.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Claude-Account-Switcher";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "";
                // --autostart 인자로 "Windows 로그인 시 자동 실행"임을 표시 → 앱이 수동 실행과 구분(토스트 억제).
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\" --autostart");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* best effort */ }
    }
}
