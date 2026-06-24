using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;

namespace ClaudeAccountSwitcher.Controls;

/// <summary>
/// 커스텀 다크 타이틀바(WindowChrome)를 사용하는 테마 윈도우 베이스.
/// 캡션 버튼(최소화/최대화/복원/닫기)이 동작하도록 SystemCommands 커맨드 바인딩만 등록한다.
/// (외부 라이브러리의 무거운 윈도우 베이스와 달리 DI/ServiceLocator/P-Invoke 결합이 전혀 없는 경량 베이스)
/// </summary>
public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        CommandBindings.Add(new CommandBinding(
            SystemCommands.MinimizeWindowCommand,
            (_, _) => SystemCommands.MinimizeWindow(this)));

        CommandBindings.Add(new CommandBinding(
            SystemCommands.MaximizeWindowCommand,
            (_, _) => SystemCommands.MaximizeWindow(this),
            (_, e) => e.CanExecute = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip));

        CommandBindings.Add(new CommandBinding(
            SystemCommands.RestoreWindowCommand,
            (_, _) => SystemCommands.RestoreWindow(this),
            (_, e) => e.CanExecute = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip));

        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand,
            (_, _) => SystemCommands.CloseWindow(this)));
    }
}
