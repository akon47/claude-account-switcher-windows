using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ClaudeAccountSwitcher.Controls;

/// <summary>
/// 더블클릭하면 인라인 편집(TextBox)으로 전환되는 텍스트 컨트롤.
/// Enter 또는 포커스 잃음 = 확정(AcceptCommand 실행), Esc = 취소(원래 값 복원).
/// </summary>
[TemplatePart(Name = PartTextBlock, Type = typeof(TextBlock))]
[TemplatePart(Name = PartTextBox, Type = typeof(TextBox))]
public class EditableTextBlock : Control
{
    private const string PartTextBlock = "PART_TextBlock";
    private const string PartTextBox = "PART_TextBox";

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EditableTextBlock),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly DependencyPropertyKey IsEditingKey = DependencyProperty.RegisterReadOnly(
        nameof(IsEditing), typeof(bool), typeof(EditableTextBlock), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsEditingProperty = IsEditingKey.DependencyProperty;

    public static readonly DependencyProperty AcceptCommandProperty = DependencyProperty.Register(
        nameof(AcceptCommand), typeof(ICommand), typeof(EditableTextBlock), new PropertyMetadata(null));

    public static readonly DependencyProperty AcceptCommandParameterProperty = DependencyProperty.Register(
        nameof(AcceptCommandParameter), typeof(object), typeof(EditableTextBlock), new PropertyMetadata(null));

    private TextBox? _textBox;
    private string? _originalText;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        private set => SetValue(IsEditingKey, value);
    }

    public ICommand? AcceptCommand
    {
        get => (ICommand?)GetValue(AcceptCommandProperty);
        set => SetValue(AcceptCommandProperty, value);
    }

    public object? AcceptCommandParameter
    {
        get => GetValue(AcceptCommandParameterProperty);
        set => SetValue(AcceptCommandParameterProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox is not null)
        {
            _textBox.LostKeyboardFocus -= OnTextBoxLostFocus;
            _textBox.KeyDown -= OnTextBoxKeyDown;
        }

        _textBox = GetTemplateChild(PartTextBox) as TextBox;

        if (_textBox is not null)
        {
            _textBox.LostKeyboardFocus += OnTextBoxLostFocus;
            _textBox.KeyDown += OnTextBoxKeyDown;
        }
    }

    // 셀 전체(투명 배경 포함)에서 더블클릭을 받아 편집을 시작한다. 글자 위만 반응하던 기존 동작의 사각지대를 없앤다.
    // (행 더블클릭=전환과의 충돌은 DoubleClickCommandBehavior 가 이름 셀 출처를 보고 막는다 — Handled 로는 못 막음.)
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2 && !IsEditing)
        {
            e.Handled = true;
            BeginEdit();
        }
    }

    private void BeginEdit()
    {
        if (IsEditing) return;
        _originalText = Text;
        IsEditing = true;

        // 템플릿 트리거로 TextBox가 보이게 된 뒤 포커스/전체선택
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _textBox?.Focus();
            _textBox?.SelectAll();
        });
    }

    private void OnTextBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelEdit();
                e.Handled = true;
                break;
        }
    }

    private void Commit()
    {
        if (!IsEditing) return;
        IsEditing = false;

        // 값이 바뀐 경우에만 확정 커맨드 실행 (Text는 양방향 바인딩으로 이미 최신)
        if (Text != _originalText && AcceptCommand?.CanExecute(AcceptCommandParameter) == true)
            AcceptCommand.Execute(AcceptCommandParameter);
    }

    private void CancelEdit()
    {
        if (!IsEditing) return;
        IsEditing = false;
        SetCurrentValue(TextProperty, _originalText); // 원래 값 복원
    }
}
