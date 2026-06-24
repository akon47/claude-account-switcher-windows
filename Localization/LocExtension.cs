using System.Windows.Data;
using System.Windows.Markup;

namespace ClaudeAccountSwitcher.Localization;

/// <summary>
/// XAML에서 {loc:Loc Key} 형태로 번역 문자열을 OneWay 바인딩한다.
/// LocalizationManager 인덱서에 바인딩하므로 언어 변경 시 실시간으로 갱신된다.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
