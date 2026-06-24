using ClaudeAccountSwitcher.Controls;

namespace ClaudeAccountSwitcher.Views;

/// <summary>다운로드 진행률 표시 다이얼로그. 취소(버튼/Esc/창 닫기)는 VM 의 CancelCommand 로 흐른다.</summary>
public partial class ProgressDialog : ThemedWindow
{
    public ProgressDialog() => InitializeComponent();
}
