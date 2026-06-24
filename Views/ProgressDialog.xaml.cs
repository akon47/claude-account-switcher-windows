using System.ComponentModel;
using System.Windows;
using ClaudeAccountSwitcher.Controls;

namespace ClaudeAccountSwitcher.Views;

/// <summary>다운로드 진행률 표시 다이얼로그. 취소(버튼/X)는 CancelRequested 로 알린다.</summary>
public partial class ProgressDialog : ThemedWindow
{
    private bool _forceClose;

    /// <summary>사용자가 취소를 눌렀을 때(취소 버튼 또는 창 닫기) 발생.</summary>
    public event Action? CancelRequested;

    public ProgressDialog()
    {
        InitializeComponent();
    }

    /// <summary>다운로드 완료 등으로 코드에서 강제로 닫을 때 호출.</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

    protected override void OnClosing(CancelEventArgs e)
    {
        // 사용자가 직접 닫으면 취소 요청만 보내고 닫지 않는다(다운로드 취소 후 ForceClose 로 닫힘).
        if (!_forceClose)
        {
            e.Cancel = true;
            CancelRequested?.Invoke();
        }
        base.OnClosing(e);
    }
}
