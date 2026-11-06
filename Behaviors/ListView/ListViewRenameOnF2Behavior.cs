using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeAccountSwitcher.Controls;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>F2 를 누르면 선택된 행의 <see cref="EditableTextBlock"/>(이름 칸)을 편집 모드로 바꾸는 첨부 동작.</summary>
public static class ListViewRenameOnF2Behavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ListViewRenameOnF2Behavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView list) return;
        list.PreviewKeyDown -= OnPreviewKeyDown;
        if (e.NewValue is true) list.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || sender is not ListView { SelectedItem: { } selected } list) return;
        if (list.ItemContainerGenerator.ContainerFromItem(selected) is not DependencyObject container) return;
        if (FindDescendant<EditableTextBlock>(container) is not { } editable) return;

        editable.BeginEdit();
        e.Handled = true;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
