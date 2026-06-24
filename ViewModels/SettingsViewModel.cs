using System.Reflection;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>설정 창 VM. 실행 셸 + 언어 선택 + 스킵 권한 기억값 재설정. 변경 즉시 저장한다.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ProfileStore _store;

    private static LocalizationManager L => LocalizationManager.Instance;

    public ShellKind[] Shells { get; } = { ShellKind.PowerShell, ShellKind.Cmd };

    [ObservableProperty] private ShellKind _selectedShell;

    /// <summary>설정 창에 표시할 언어 선택지.</summary>
    public sealed record LanguageOption(string Culture, string Display);

    public LanguageOption[] Languages { get; }

    [ObservableProperty] private LanguageOption _selectedLanguage;

    public SettingsViewModel(ProfileStore store)
    {
        _store = store;
        _selectedShell = _store.Data.Shell;

        Languages = LocalizationManager.Available.Select(a => new LanguageOption(a.Culture, a.Display)).ToArray();
        _selectedLanguage = Languages.FirstOrDefault(l => l.Culture == L.Culture) ?? Languages[0];
    }

    partial void OnSelectedShellChanged(ShellKind value)
    {
        _store.Data.Shell = value;
        _store.Save();
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null) return;
        L.Culture = value.Culture;
        _store.Data.Language = value.Culture;
        _store.Save();
        // 코드로 만든 표시 문자열은 수동 갱신
        OnPropertyChanged(nameof(SkipPermissionsState));
    }

    public string SkipPermissionsState => _store.Data.SkipPermissions switch
    {
        null => L["SkipStateAsk"],
        true => L["SkipStateAlways"],
        false => L["SkipStateNever"],
    };

    public bool CanResetSkipPermissions => _store.Data.SkipPermissions is not null;

    /// <summary>앱 버전 (예: v0.1.0).</summary>
    public string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    [RelayCommand]
    private void ResetSkipPermissions()
    {
        _store.Data.SkipPermissions = null;
        _store.Save();
        OnPropertyChanged(nameof(SkipPermissionsState));
        OnPropertyChanged(nameof(CanResetSkipPermissions));
    }
}
