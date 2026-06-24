using System.Windows;
using ClaudeAccountSwitcher.Controls;

namespace ClaudeAccountSwitcher.Views;

public partial class InputDialog : ThemedWindow
{
    public InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
