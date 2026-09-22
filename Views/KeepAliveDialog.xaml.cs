using ClaudeAccountSwitcher.Controls;

namespace ClaudeAccountSwitcher.Views;

/// <summary>세션 자동 유지 방식(항상 / 시간표) 설정 다이얼로그. 로직은 KeepAliveDialogViewModel 에 있다.</summary>
public partial class KeepAliveDialog : ThemedWindow
{
    public KeepAliveDialog() => InitializeComponent();
}
