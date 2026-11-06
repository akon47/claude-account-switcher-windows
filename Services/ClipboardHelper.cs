using System.Runtime.InteropServices;
using System.Windows;

namespace ClaudeAccountSwitcher.Services;

/// <summary>클립보드 쓰기. 다른 프로그램이 잠깐 잡고 있어 실패하는 경우(CLIPBRD_E_CANT_OPEN)를 몇 번 재시도한다.</summary>
public static class ClipboardHelper
{
    public static bool TrySetText(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException) { Thread.Sleep(30); }
            catch { return false; }
        }
        return false;
    }
}
