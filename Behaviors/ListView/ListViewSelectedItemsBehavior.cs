using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// ListView 의 다중 선택(SelectedItems)을 VM 의 컬렉션으로 밀어넣는 첨부 동작.
/// SelectedItems 는 바인딩 불가한 읽기전용이라, SelectionChanged 마다 대상 IList 를 다시 채운다.
/// 코드비하인드 없이 다중 선택을 VM 으로 노출한다.
/// </summary>
public static class ListViewSelectedItemsBehavior
{
    public static readonly DependencyProperty BoundSelectionProperty = DependencyProperty.RegisterAttached(
        "BoundSelection", typeof(IList), typeof(ListViewSelectedItemsBehavior),
        new PropertyMetadata(null, OnBoundSelectionChanged));

    public static IList? GetBoundSelection(DependencyObject o) => (IList?)o.GetValue(BoundSelectionProperty);

    public static void SetBoundSelection(DependencyObject o, IList? value) => o.SetValue(BoundSelectionProperty, value);

    private static void OnBoundSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView list) return;

        list.SelectionChanged -= OnSelectionChanged;
        if (e.NewValue is IList) list.SelectionChanged += OnSelectionChanged;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var list = (ListView)sender;
        var target = GetBoundSelection(list);
        if (target is null) return;

        target.Clear();
        foreach (var item in list.SelectedItems) target.Add(item);
    }
}
