using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeAccountSwitcher.Controls;
using ClaudeAccountSwitcher.ViewModels;

namespace ClaudeAccountSwitcher.Views;

public partial class MainWindow : ThemedWindow
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Activated += (_, _) => vm.ReloadFromStore();
    }

    private void ProfileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 헤더/빈 영역 더블클릭은 무시하고, 실제 행을 더블클릭했을 때만 전환한다.
        if (ItemsControl.ContainerFromElement(ProfileList, e.OriginalSource as DependencyObject) is not ListViewItem)
            return;
        // 이름 칸(EditableTextBlock) 더블클릭은 이름 편집이므로 전환하지 않는다.
        if (FindAncestor<EditableTextBlock>(e.OriginalSource as DependencyObject) is not null)
            return;
        (DataContext as MainViewModel)?.SwitchCommand.Execute(null);
    }

    private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
    {
        for (var node = from; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is T match) return match;
        return null;
    }
}
