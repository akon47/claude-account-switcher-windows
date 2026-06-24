using System.Windows.Data;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>단일 값과 다중 값 변환을 모두 지원하는 컨버터 인터페이스.</summary>
public interface IValueAndMultiValueConverter : IMultiValueConverter, IValueConverter;
