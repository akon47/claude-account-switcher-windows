using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeAccountSwitcher.Controls;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// 컨트롤 더블클릭 시 바인딩된 ICommand 를 실행하는 첨부 동작.
/// 코드비하인드 없이 View 에서 더블클릭을 커맨드로 연결한다.
/// </summary>
public static class DoubleClickCommandBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(DoubleClickCommandBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(DoubleClickCommandBehavior), new PropertyMetadata(null));

    public static ICommand? GetCommand(DependencyObject o) => (ICommand?)o.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject o, ICommand? value) => o.SetValue(CommandProperty, value);

    public static object? GetCommandParameter(DependencyObject o) => o.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(DependencyObject o, object? value) =>
        o.SetValue(CommandParameterProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;

        control.MouseDoubleClick -= OnMouseDoubleClick;
        if (e.NewValue is ICommand) control.MouseDoubleClick += OnMouseDoubleClick;
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 이름 셀(EditableTextBlock) 안에서 시작된 더블클릭은 인라인 편집용이다.
        // Control.MouseDoubleClick 은 MouseDown 의 Handled 여부와 무관하게 발생하므로,
        // 여기서 출처를 보고 전환 커맨드 실행을 막아야 한다(편집 진입/편집 중 모두).
        if (e.OriginalSource is DependencyObject src && FindAncestor<EditableTextBlock>(src) is not null)
            return;

        var element = (DependencyObject)sender;
        var command = GetCommand(element);
        var parameter = GetCommandParameter(element);
        if (command?.CanExecute(parameter) == true) command.Execute(parameter);
    }

    /// <summary>시각/논리 트리를 거슬러 올라가며 T 형식 조상을 찾는다(없으면 null).</summary>
    private static T? FindAncestor<T>(DependencyObject? node)
        where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}
