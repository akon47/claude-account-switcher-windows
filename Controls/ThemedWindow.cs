using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClaudeAccountSwitcher.Controls;

/// <summary>
/// 커스텀 다크 타이틀바(WindowChrome)를 사용하는 테마 윈도우 베이스.
/// <list type="bullet">
/// <item>캡션 버튼(최소화/최대화/복원/닫기)이 동작하도록 SystemCommands 커맨드 바인딩을 등록한다.</item>
/// <item>DWM 에 다크 모드·둥근 모서리·테두리 색을 알려 Windows 11 의 시스템 그림자/테두리가 테마와 맞게 그려지게 한다.</item>
/// <item>Windows 11 스냅 레이아웃: 최대화 버튼 위에서 <c>WM_NCHITTEST</c> 에 <c>HTMAXBUTTON</c> 을 돌려주면
///       시스템이 스냅 플라이아웃을 띄운다. 그 대신 그 영역의 마우스 메시지는 비클라이언트로 바뀌어 WPF 버튼이 받지 못하므로
///       호버 배경과 클릭(최대화/복원)을 여기서 직접 처리한다.</item>
/// <item>최대화 시 창이 화면 밖으로 밀리는 프레임 두께를 DPI 에 맞게 계산해 <see cref="ContentMargin"/> 으로 노출한다.</item>
/// </list>
/// </summary>
public class ThemedWindow : Window
{
    private const int BorderColorRef = 0x00444444; // CharcoalAsh (#444444) — COLORREF 는 0x00BBGGRR

    /// <summary>최대화 상태에서 콘텐츠를 안쪽으로 밀 여백(프레임이 화면 밖으로 나가는 만큼). 보통 때는 0.</summary>
    public static readonly DependencyProperty ContentMarginProperty = DependencyProperty.Register(
        nameof(ContentMargin), typeof(Thickness), typeof(ThemedWindow), new PropertyMetadata(default(Thickness)));

    public Thickness ContentMargin
    {
        get => (Thickness)GetValue(ContentMarginProperty);
        private set => SetValue(ContentMarginProperty, value);
    }

    private Button? _maximizeButton;
    private Button? _restoreButton;
    private Button? _snapHovered;

    public ThemedWindow()
    {
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this), (_, e) => e.CanExecute = CanResize));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this), (_, e) => e.CanExecute = CanResize));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
        StateChanged += (_, _) => UpdateContentMargin();
    }

    private bool CanResize => ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _maximizeButton = GetTemplateChild("Win_MaximizeButton") as Button;
        _restoreButton = GetTemplateChild("Win_RestoreButton") as Button;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        ApplyDwmAttributes(hwnd);
        UpdateContentMargin();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateContentMargin();
    }

    /// <summary>DWM 힌트: 다크 프레임, 둥근 모서리(Win11), 테마색 1px 테두리. 지원하지 않는 OS(Win10 구버전)에선 조용히 실패한다.</summary>
    private static void ApplyDwmAttributes(nint hwnd)
    {
        try
        {
            int on = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
            int round = NativeMethods.DwmwcpRound;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref round, sizeof(int));
            int color = BorderColorRef;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaBorderColor, ref color, sizeof(int));
        }
        catch { /* dwmapi 없음/미지원 — 기본 프레임으로 */ }
    }

    /// <summary>최대화 시 OS 가 창을 프레임 두께만큼 화면 밖으로 밀어내므로 그만큼 콘텐츠 여백을 준다(DPI 반영).</summary>
    private void UpdateContentMargin()
    {
        if (WindowState != WindowState.Maximized)
        {
            ContentMargin = default;
            return;
        }

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            uint dpiValue = (uint)Math.Round(96 * dpi.DpiScaleX);
            double x = (NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxFrame, dpiValue) + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpiValue)) / dpi.DpiScaleX;
            double y = (NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyFrame, dpiValue) + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpiValue)) / dpi.DpiScaleY;
            ContentMargin = new Thickness(x, y, x, y);
        }
        catch
        {
            ContentMargin = new Thickness(8);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WmNcHitTest:
                if (CanResize && HitTestMaximizeButton(lParam) is { } button)
                {
                    SetSnapHover(button);
                    handled = true;
                    return NativeMethods.HtMaxButton;
                }
                SetSnapHover(null);
                break;

            case NativeMethods.WmNcMouseLeave:
                SetSnapHover(null);
                break;

            case NativeMethods.WmNcLButtonDown when wParam == NativeMethods.HtMaxButton:
                handled = true; // 기본 처리(시스템 메뉴 등)를 막고 클릭은 WM_NCLBUTTONUP 에서 완성한다
                break;

            case NativeMethods.WmNcLButtonUp when wParam == NativeMethods.HtMaxButton:
                handled = true;
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
        }
        return 0;
    }

    /// <summary>화면 좌표(lParam)가 현재 보이는 최대화/복원 버튼 위인지.</summary>
    private Button? HitTestMaximizeButton(nint lParam)
    {
        var button = _restoreButton is { IsVisible: true } ? _restoreButton : _maximizeButton;
        if (button is not { IsVisible: true, IsEnabled: true }) return null;

        long packed = lParam.ToInt64();
        Point screen = new((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
        Point local = button.PointFromScreen(screen);
        bool inside = local.X >= 0 && local.Y >= 0 && local.X < button.ActualWidth && local.Y < button.ActualHeight;
        return inside ? button : null;
    }

    /// <summary>비클라이언트로 처리되는 동안 WPF 호버 트리거가 안 돌므로 배경을 직접 칠한다.</summary>
    private void SetSnapHover(Button? button)
    {
        if (ReferenceEquals(_snapHovered, button)) return;
        _snapHovered?.ClearValue(BackgroundProperty);
        _snapHovered = button;
        if (button is not null && TryFindResource("CharcoalAshBrush") is Brush hover) button.Background = hover;
    }
}
