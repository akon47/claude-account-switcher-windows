using System.Windows;
using ClaudeAccountSwitcher.Controls;

namespace ClaudeAccountSwitcher.Views;

public partial class SkipPermissionsDialog : ThemedWindow
{
    /// <summary>"권한 건너뛰고 실행"을 눌렀으면 true, "그냥 실행"이면 false.</summary>
    public bool SkipChosen { get; private set; }

    public SkipPermissionsDialog() => InitializeComponent();

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SkipChosen = true;
        DialogResult = true;
    }

    private void NoSkip_Click(object sender, RoutedEventArgs e)
    {
        SkipChosen = false;
        DialogResult = true;
    }
}
