using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeAccountSwitcher.Controls;
using ClaudeAccountSwitcher.ViewModels;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// ListView 의 행을 드래그해 순서를 바꾸는 첨부 동작(삽입 위치선 포함).
/// 드롭 시 바인딩된 Command 를 <see cref="ReorderRequest"/> 인자로 실행한다. 코드비하인드 불필요.
/// </summary>
public static class ListViewDragReorderBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(ListViewDragReorderBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject o) => (ICommand?)o.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject o, ICommand? value) => o.SetValue(CommandProperty, value);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(DragState), typeof(ListViewDragReorderBehavior), new PropertyMetadata(null));

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView list) return;

        var state = (DragState?)list.GetValue(StateProperty);
        if (e.NewValue is ICommand && state is null)
        {
            list.SetValue(StateProperty, new DragState(list));
        }
        else if (e.NewValue is null && state is not null)
        {
            state.Detach();
            list.ClearValue(StateProperty);
        }
    }

    /// <summary>ListView 한 개의 드래그 상태/핸들러를 담는다.</summary>
    private sealed class DragState
    {
        private readonly ListView _list;
        private Point _start;
        private object? _item;
        private InsertionAdorner? _adorner;

        public DragState(ListView list)
        {
            _list = list;
            list.AllowDrop = true;
            list.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            list.PreviewMouseMove += OnPreviewMouseMove;
            list.DragOver += OnDragOver;
            list.DragLeave += OnDragLeave;
            list.Drop += OnDrop;
        }

        public void Detach()
        {
            _list.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            _list.PreviewMouseMove -= OnPreviewMouseMove;
            _list.DragOver -= OnDragOver;
            _list.DragLeave -= OnDragLeave;
            _list.Drop -= OnDrop;
            RemoveAdorner();
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _start = e.GetPosition(null);
            _item = null;

            var src = e.OriginalSource as DependencyObject;
            // 삭제 버튼 등 위에서 누른 경우엔 드래그를 시작하지 않는다(클릭 동작 보존).
            if (FindAncestor<ButtonBase>(src) is not null) return;
            if (ItemsControl.ContainerFromElement(_list, src) is ListViewItem item)
                _item = _list.ItemContainerGenerator.ItemFromContainer(item);
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_item is null || e.LeftButton != MouseButtonState.Pressed) return;
            // 이름 편집(TextBox) 중에는 텍스트 선택이 우선 — 드래그하지 않는다.
            if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null) return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var dragged = _item;
            _item = null;
            DragDrop.DoDragDrop(_list, dragged, DragDropEffects.Move);
            RemoveAdorner();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (!HasReorderData(e))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            if (RowUnder(e.OriginalSource) is ListViewItem item)
                ShowAdorner(item, above: e.GetPosition(item).Y < item.ActualHeight / 2);
            else if (LastRow() is ListViewItem last)
                ShowAdorner(last, above: false);
            else
                RemoveAdorner();
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            var p = e.GetPosition(_list);
            if (p.X < 0 || p.Y < 0 || p.X > _list.ActualWidth || p.Y > _list.ActualHeight)
                RemoveAdorner();
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            RemoveAdorner();
            var dragged = GetReorderData(e);
            if (dragged is null) return;

            int insertIndex = _list.Items.Count; // 기본: 맨 뒤
            if (RowUnder(e.OriginalSource) is ListViewItem item)
            {
                int t = _list.ItemContainerGenerator.IndexFromContainer(item);
                bool below = e.GetPosition(item).Y >= item.ActualHeight / 2;
                insertIndex = below ? t + 1 : t;
            }

            var command = GetCommand(_list);
            var request = new ReorderRequest(dragged, insertIndex);
            if (command?.CanExecute(request) == true) command.Execute(request);
            e.Handled = true;
        }

        private static bool HasReorderData(DragEventArgs e) => GetReorderData(e) is not null;

        // 드래그 데이터는 행의 데이터 항목(VM) 자체. 포맷은 그 런타임 타입.
        private static object? GetReorderData(DragEventArgs e)
        {
            foreach (var format in e.Data.GetFormats())
            {
                var data = e.Data.GetData(format);
                if (data is not null && data is not string && !format.StartsWith("System.Windows."))
                    return data;
            }
            return null;
        }

        private ListViewItem? RowUnder(object? originalSource) =>
            ItemsControl.ContainerFromElement(_list, originalSource as DependencyObject) as ListViewItem;

        private ListViewItem? LastRow() =>
            _list.Items.Count == 0
                ? null
                : _list.ItemContainerGenerator.ContainerFromIndex(_list.Items.Count - 1) as ListViewItem;

        private void ShowAdorner(ListViewItem item, bool above)
        {
            if (_adorner is not null && ReferenceEquals(_adorner.AdornedElement, item) && _adorner.IsAbove == above)
                return;

            RemoveAdorner();
            var layer = AdornerLayer.GetAdornerLayer(item);
            if (layer is null) return;
            _adorner = new InsertionAdorner(item, above);
            layer.Add(_adorner);
        }

        private void RemoveAdorner()
        {
            if (_adorner is null) return;
            AdornerLayer.GetAdornerLayer(_adorner.AdornedElement)?.Remove(_adorner);
            _adorner = null;
        }

        private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
        {
            for (var node = from; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is T match) return match;
            }
            return null;
        }
    }
}
