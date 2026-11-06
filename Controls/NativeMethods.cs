using System.Runtime.InteropServices;

namespace ClaudeAccountSwitcher.Controls;

/// <summary>ThemedWindow 가 쓰는 Win32/DWM 호출 모음(P/Invoke 는 여기 한곳에).</summary>
internal static class NativeMethods
{
    public const int WmNcHitTest = 0x0084;
    public const int WmNcLButtonDown = 0x00A1;
    public const int WmNcLButtonUp = 0x00A2;
    public const int WmNcMouseLeave = 0x02A2;
    public const int HtMaxButton = 9;

    public const int SmCxFrame = 32;
    public const int SmCyFrame = 33;
    public const int SmCxPaddedBorder = 92;

    public const int DwmwaUseImmersiveDarkMode = 20;
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwaBorderColor = 34;
    public const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
