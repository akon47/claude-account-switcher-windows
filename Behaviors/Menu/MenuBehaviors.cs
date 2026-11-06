using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// 메뉴 관련 첨부 동작(코드비하인드 없이 메뉴가 열리는 순간 VM 명령을 실행).
/// - <see cref="OpenedCommandProperty"/>: ContextMenu 가 열릴 때(지연 로딩 목록 채우기).
/// - <see cref="SubmenuOpenedCommandProperty"/>: MenuItem 의 서브메뉴가 열릴 때.
/// - <see cref="IsDropDownProperty"/>: Button 을 누르면 자기 ContextMenu 를 바로 아래에 드롭다운으로 연다.
/// </summary>
public static class MenuBehaviors
{
    public static readonly DependencyProperty OpenedCommandProperty = DependencyProperty.RegisterAttached(
        "OpenedCommand", typeof(ICommand), typeof(MenuBehaviors), new PropertyMetadata(null, OnOpenedCommandChanged));

    public static ICommand? GetOpenedCommand(DependencyObject o) => (ICommand?)o.GetValue(OpenedCommandProperty);

    public static void SetOpenedCommand(DependencyObject o, ICommand? value) => o.SetValue(OpenedCommandProperty, value);

    private static void OnOpenedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContextMenu menu) return;
        menu.Opened -= OnMenuOpened;
        if (e.NewValue is ICommand) menu.Opened += OnMenuOpened;
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && GetOpenedCommand(menu) is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
    }

    public static readonly DependencyProperty SubmenuOpenedCommandProperty = DependencyProperty.RegisterAttached(
        "SubmenuOpenedCommand", typeof(ICommand), typeof(MenuBehaviors), new PropertyMetadata(null, OnSubmenuOpenedCommandChanged));

    public static ICommand? GetSubmenuOpenedCommand(DependencyObject o) => (ICommand?)o.GetValue(SubmenuOpenedCommandProperty);

    public static void SetSubmenuOpenedCommand(DependencyObject o, ICommand? value) => o.SetValue(SubmenuOpenedCommandProperty, value);

    private static void OnSubmenuOpenedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MenuItem item) return;
        item.SubmenuOpened -= OnSubmenuOpened;
        if (e.NewValue is ICommand) item.SubmenuOpened += OnSubmenuOpened;
    }

    private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        // 서브메뉴 항목 자신의 SubmenuOpened 가 버블링돼 올 수 있으므로 원본이 이 항목일 때만.
        if (!ReferenceEquals(sender, e.OriginalSource)) return;
        if (sender is MenuItem item && GetSubmenuOpenedCommand(item) is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
    }

    public static readonly DependencyProperty IsDropDownProperty = DependencyProperty.RegisterAttached(
        "IsDropDown", typeof(bool), typeof(MenuBehaviors), new PropertyMetadata(false, OnIsDropDownChanged));

    public static bool GetIsDropDown(DependencyObject o) => (bool)o.GetValue(IsDropDownProperty);

    public static void SetIsDropDown(DependencyObject o, bool value) => o.SetValue(IsDropDownProperty, value);

    private static void OnIsDropDownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        button.Click -= OnDropDownClick;
        if (e.NewValue is true) button.Click += OnDropDownClick;
    }

    private static void OnDropDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
