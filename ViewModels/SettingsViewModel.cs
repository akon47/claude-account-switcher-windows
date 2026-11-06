using System.Diagnostics;
using System.IO;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>설정 창 VM. 언어·실행 셸·권한·일반·업데이트·정보. 변경 즉시 저장한다.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private const string RepoUrl = "https://github.com/akon47/claude-account-switcher-windows";

    private readonly ProfileStore _store;
    private readonly UpdateCoordinator _updates;

    private static LocalizationManager L => LocalizationManager.Instance;

    public ShellKind[] Shells { get; } = [ShellKind.PowerShell, ShellKind.Cmd];

    [ObservableProperty] private ShellKind _selectedShell;

    /// <summary>설정 창에 표시할 언어 선택지.</summary>
    public sealed record LanguageOption(string Culture, string Display);

    public LanguageOption[] Languages { get; }

    [ObservableProperty] private LanguageOption _selectedLanguage;

    public SettingsViewModel(ProfileStore store, UpdateCoordinator updates)
    {
        _store = store;
        _updates = updates;
        _selectedShell = _store.Data.Shell;

        Languages = LocalizationManager.Available.Select(a => new LanguageOption(a.Culture, a.Display)).ToArray();
        _selectedLanguage = Languages.FirstOrDefault(l => l.Culture == L.Culture) ?? Languages[0];
    }

    /// <summary>
    /// 새 창을 관리자 권한으로 띄울지. 앱 자신은 일반 권한이라 켜면 실행할 때마다 UAC 창이 뜬다.
    /// 로그인 실행(새 계정 추가/다시 로그인)은 브라우저 인증 때문에 승격하지 않는다.
    /// </summary>
    public bool RunAsAdmin
    {
        get => _store.Data.RunAsAdmin;
        set { _store.Data.RunAsAdmin = value; _store.Save(); OnPropertyChanged(); }
    }

    /// <summary>Windows 로그인 시 자동 실행(HKCU Run). 트레이 메뉴 토글과 같은 레지스트리를 가리킨다.</summary>
    public bool RunAtStartup
    {
        get => _store.Data.RunAtStartup ?? AutoStart.IsEnabled();
        set
        {
            _store.Data.RunAtStartup = value; // AppData 가 진짜 값(업데이트로 레지스트리가 지워져도 복원됨)
            AutoStart.Set(value);
            _store.Save();
            OnPropertyChanged(); // 실패 시 실제 상태로 되돌아가도록 다시 읽게 한다
        }
    }

    /// <summary>탐색기 폴더 우클릭 "Claude로 실행" 메뉴 등록 여부. 트레이 메뉴 토글과 동일.</summary>
    public bool ExplorerMenuEnabled
    {
        get => _store.Data.ExplorerMenu ?? ExplorerMenu.IsInstalled();
        set
        {
            _store.Data.ExplorerMenu = value;
            if (value) ExplorerMenu.Install(_store.Data.Profiles);
            else ExplorerMenu.Uninstall();
            _store.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>동시 실행 claude 하단에 계정 상태줄 표시 여부. 끄면 모든 프로필에서 우리 statusLine 을 즉시 제거한다.</summary>
    public bool StatusLineEnabled
    {
        get => _store.Data.StatusLine;
        set
        {
            _store.Data.StatusLine = value;
            _store.Save();
            foreach (var p in _store.Data.Profiles) StatusLineProvisioner.Ensure(p, value);
            OnPropertyChanged();
        }
    }

    /// <summary>시작 시·주기적 자동 업데이트 확인.</summary>
    public bool AutoUpdateCheck
    {
        get => _store.Data.AutoUpdateCheck;
        set { _store.Data.AutoUpdateCheck = value; _store.Save(); OnPropertyChanged(); }
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
        OnPropertyChanged(nameof(VersionText));
    }

    public string SkipPermissionsState => _store.Data.SkipPermissions switch
    {
        null => L["SkipStateAsk"],
        true => L["SkipStateAlways"],
        false => L["SkipStateNever"],
    };

    public bool CanResetSkipPermissions => _store.Data.SkipPermissions is not null;

    /// <summary>앱 버전 문구(예: "버전 0.12.0").</summary>
    public string VersionText => L.Tr("SetVersionFmt", UpdateService.CurrentVersion.ToString(3));

    [RelayCommand]
    private void ResetSkipPermissions()
    {
        _store.Data.SkipPermissions = null;
        _store.Save();
        OnPropertyChanged(nameof(SkipPermissionsState));
        OnPropertyChanged(nameof(CanResetSkipPermissions));
    }

    /// <summary>지금 업데이트 확인(결과를 항상 알려준다). 진행 중엔 버튼이 잠긴다.</summary>
    [RelayCommand]
    private Task CheckUpdates() => _updates.CheckAsync(manual: true);

    [RelayCommand]
    private static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.AppDataDir}\"") { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    [RelayCommand]
    private static void OpenRepository()
    {
        try { Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true }); }
        catch { /* best effort */ }
    }
}
