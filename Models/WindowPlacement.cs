namespace ClaudeAccountSwitcher.Models;

/// <summary>창의 위치/크기 기억값(DIP 단위). 최대화 상태면 복원 크기(RestoreBounds)를 담는다.</summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }
}
