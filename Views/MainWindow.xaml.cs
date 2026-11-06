using ClaudeAccountSwitcher.Controls;
using ClaudeAccountSwitcher.ViewModels;

namespace ClaudeAccountSwitcher.Views;

public sealed partial class MainWindow : ThemedWindow
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
