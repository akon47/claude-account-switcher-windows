using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeAccountSwitcher.Controls;
using ClaudeAccountSwitcher.ViewModels;

namespace ClaudeAccountSwitcher.Views;

public partial class MainWindow : ThemedWindow
{
    // 행 드래그로 순서 변경: 시작 지점과 드래그 중인 행을 기억해 둔다.
    private Point _dragStart;
    private ProfileItemViewModel? _dragItem;

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

    // ---- 행 드래그로 순서 변경 ----------------------------------------------

    private void ProfileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = null;

        var src = e.OriginalSource as DependencyObject;
        // 삭제 버튼 위에서 누른 경우엔 드래그를 시작하지 않는다(클릭 동작 보존).
        if (FindAncestor<ButtonBase>(src) is not null) return;
        // 실제 행을 누른 경우에만 그 행을 드래그 후보로 기억한다.
        if (ItemsControl.ContainerFromElement(ProfileList, src) is ListViewItem item)
            _dragItem = ProfileList.ItemContainerGenerator.ItemFromContainer(item) as ProfileItemViewModel;
    }

    private void ProfileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        // 이름 편집(TextBox) 중에는 텍스트 선택이 우선 — 드래그하지 않는다.
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null) return;

        var pos = e.GetPosition(null);
        if (System.Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var dragged = _dragItem;
        _dragItem = null; // DoDragDrop 은 드롭까지 블로킹되므로 먼저 비운다.
        DragDrop.DoDragDrop(ProfileList, dragged, DragDropEffects.Move);
    }

    private void ProfileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ProfileItemViewModel))
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ProfileList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ProfileItemViewModel)) is not ProfileItemViewModel dragged) return;

        ProfileItemViewModel? target = null;
        if (ItemsControl.ContainerFromElement(ProfileList, e.OriginalSource as DependencyObject) is ListViewItem item)
            target = ProfileList.ItemContainerGenerator.ItemFromContainer(item) as ProfileItemViewModel;

        (DataContext as MainViewModel)?.MoveProfile(dragged, target);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
    {
        for (var node = from; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is T match) return match;
        return null;
    }
}
