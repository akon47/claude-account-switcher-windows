using System.Windows;
using ClaudeAccountSwitcher.Controls;

namespace ClaudeAccountSwitcher.Views;

public partial class MessageDialog : ThemedWindow
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
