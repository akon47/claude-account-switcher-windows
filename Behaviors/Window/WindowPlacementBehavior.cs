using System.Windows;
using System.Windows.Input;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// 창 위치/크기를 기억하고 복원하는 첨부 동작. <see cref="PlacementProperty"/> 에 바인딩한 모델을
/// 창이 처음 뜰 때(SourceInitialized) 적용하고, 닫힐 때(Closing — 트레이로 숨기며 취소돼도 발생) 다시 써 넣은 뒤
/// <see cref="SaveCommandProperty"/> 를 실행한다. 저장된 위치가 현재 화면 밖이면(모니터 구성이 바뀜) 무시한다.
/// </summary>
public static class WindowPlacementBehavior
{
    public static readonly DependencyProperty PlacementProperty = DependencyProperty.RegisterAttached(
        "Placement", typeof(WindowPlacement), typeof(WindowPlacementBehavior), new PropertyMetadata(null, OnPlacementChanged));

    public static WindowPlacement? GetPlacement(DependencyObject o) => (WindowPlacement?)o.GetValue(PlacementProperty);

    public static void SetPlacement(DependencyObject o, WindowPlacement? value) => o.SetValue(PlacementProperty, value);

    public static readonly DependencyProperty SaveCommandProperty = DependencyProperty.RegisterAttached(
        "SaveCommand", typeof(ICommand), typeof(WindowPlacementBehavior), new PropertyMetadata(null));

    public static ICommand? GetSaveCommand(DependencyObject o) => (ICommand?)o.GetValue(SaveCommandProperty);

    public static void SetSaveCommand(DependencyObject o, ICommand? value) => o.SetValue(SaveCommandProperty, value);

    private static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window w) return;
        w.SourceInitialized -= OnSourceInitialized;
        w.Closing -= OnClosing;
        if (e.NewValue is WindowPlacement)
        {
            w.SourceInitialized += OnSourceInitialized;
            w.Closing += OnClosing;
        }
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window w || GetPlacement(w) is not { Width: > 0, Height: > 0 } p) return;

        // 최소 크기보다 작거나 가상 화면(모든 모니터)과 겹치지 않으면 기억값을 버리고 기본 위치로.
        if (p.Width < w.MinWidth || p.Height < w.MinHeight) return;
        Rect saved = new(p.Left, p.Top, p.Width, p.Height);
        Rect screen = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                          SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        Rect visible = Rect.Intersect(saved, screen);
        if (visible.IsEmpty || visible.Width < 100 || visible.Height < 60) return; // 타이틀바를 잡을 만큼은 보여야 한다

        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = p.Left;
        w.Top = p.Top;
        w.Width = p.Width;
        w.Height = p.Height;
        if (p.IsMaximized) w.WindowState = WindowState.Maximized;
    }

    private static void OnClosing(object? sender, EventArgs e)
    {
        if (sender is not Window w || GetPlacement(w) is not { } p) return;
        if (w.WindowState == WindowState.Minimized) return; // 최소화 상태는 기억하지 않는다

        Rect bounds = w.WindowState == WindowState.Maximized ? w.RestoreBounds : new Rect(w.Left, w.Top, w.Width, w.Height);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

        p.Left = bounds.Left;
        p.Top = bounds.Top;
        p.Width = bounds.Width;
        p.Height = bounds.Height;
        p.IsMaximized = w.WindowState == WindowState.Maximized;

        if (GetSaveCommand(w) is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
    }
}
