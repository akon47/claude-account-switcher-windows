using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using ClaudeAccountSwitcher.Services;
using ClaudeAccountSwitcher.ViewModels;
using ClaudeAccountSwitcher.Views;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeAccountSwitcher;

/// <summary>
/// 합성 루트(Composition Root). DI 컨테이너 구성 + 트레이 상주 + 관리 창/다이얼로그 호스팅.
/// </summary>
public partial class App : Application
{
    private IServiceProvider _services = null!;
    private ProfileStore _store = null!;
    private MainViewModel _mainVm = null!;
    private TaskbarIcon? _tray;
    private ContextMenu _menu = null!;
    private MainWindow? _window;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadDebounce;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = BuildServiceProvider();
        _store = _services.GetRequiredService<ProfileStore>();

        // 탐색기 우클릭 메뉴에서 호출된 경우: 트레이 없이 해당 계정으로 실행만 하고 종료
        if (TryHandleCliLaunch(e.Args)) { Shutdown(); return; }

        _store.Load();
        ReconcileStartupSettings(); // 저장된 설정대로 자동실행/탐색기 레지스트리 복원(업데이트 후 초기화 방지)
        InitLanguage();

        _mainVm = _services.GetRequiredService<MainViewModel>();
        _mainVm.Changed += OnDataChanged;
        _mainVm.UsageUpdated += RebuildMenu; // 사용량 조회 완료 시 트레이 라벨만 갱신
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        SetupProfileWatcher();

        _tray = new TaskbarIcon
        {
            ToolTipText = "Claude Account Switcher",
            Visibility = Visibility.Visible,
        };

        try
        {
            _tray.IconSource = BitmapFrame.Create(
                new Uri("pack://application:,,,/Resources/app.ico"),
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);
        }
        catch { /* 아이콘 로드 실패해도 기능은 동작 */ }

        _menu = new ContextMenu();
        _menu.Opened += (_, _) => RebuildMenu();
        _tray.ContextMenu = _menu;
        _tray.TrayLeftMouseUp += (_, _) => ShowWindow();
        _tray.TrayBalloonTipClicked += (_, _) => ShowWindow(); // 토스트 클릭 → 관리 창 열기

        RebuildMenu();
        _tray.ForceCreate();

        // 수동 실행(자동 실행 인자 없음)이면 트레이에 들어갔음을 토스트로 알린다(첫 사용자 "안 켜졌나?" 혼란 방지).
        if (!e.Args.Contains("--autostart")) ShowRunningInTrayToast();

        // 세션 자동 유지 감시 시작(KeepSessionAlive 켜진 프로필의 5시간 창을 리셋 즉시 재시작).
        _services.GetRequiredService<SessionKeepAliveService>().Start();

        // 시작 시 1회 백그라운드 업데이트 확인(조용히; 새 버전이 있을 때만 안내).
        _ = CheckForUpdatesAsync(manual: false);
    }

    /// <summary>
    /// 저장된 사용자 설정(AppData)대로 자동실행/탐색기메뉴 레지스트리를 맞춘다.
    /// 업데이트가 구버전 언인스톨러를 돌려 HKCU 설정을 지워도 이 메서드가 복원한다.
    /// 미설정(null)이면 현재 레지스트리 상태로 시드한다(기존 사용자 설정 유지).
    /// </summary>
    private void ReconcileStartupSettings()
    {
        try
        {
            var data = _store.Data;

            // 자동 실행: 미설정이면 현재 상태로 시드한 뒤, 저장값을 레지스트리에 강제(+ --autostart 정규화).
            data.RunAtStartup ??= AutoStart.IsEnabled();
            AutoStart.Set(data.RunAtStartup.Value);

            // 탐색기 메뉴: 미설정이면 시드. ON인데 지워졌으면 복원, OFF인데 남아있으면 제거.
            data.ExplorerMenu ??= ExplorerMenu.IsInstalled();
            if (data.ExplorerMenu.Value && !ExplorerMenu.IsInstalled()) ExplorerMenu.Install(data.Profiles);
            else if (!data.ExplorerMenu.Value && ExplorerMenu.IsInstalled()) ExplorerMenu.Uninstall();

            _store.Save();
        }
        catch { /* best effort — 복원 실패해도 앱은 동작 */ }
    }

    /// <summary>트레이에서 실행 중임을 토스트로 알린다(클릭 시 관리 창 열기 — TrayBalloonTipClicked).</summary>
    private void ShowRunningInTrayToast()
    {
        try
        {
            _tray?.ShowNotification("Claude Account Switcher", LocalizationManager.Instance["TrayRunningBody"], NotificationIcon.Info);
        }
        catch { /* 알림 실패는 무시 */ }
    }

    /// <summary>첫 실행이면 윈도우 언어로 기본값을 정하고, 저장된 언어를 적용한다.</summary>
    private void InitLanguage()
    {
        var lang = _store.Data.Language;
        if (string.IsNullOrEmpty(lang))
        {
            lang = LocalizationManager.ResolveDefaultCulture();
            _store.Data.Language = lang;
            _store.Save();
        }
        LocalizationManager.Instance.Culture = lang;
    }

    /// <summary>언어 변경 시 코드로 만든 문자열(트레이 메뉴 + 목록 상태)을 다시 그린다.</summary>
    private void OnLanguageChanged()
    {
        RebuildMenu();
        _mainVm.ReloadFromStore();

        // 탐색기 우클릭 메뉴 최상위 라벨도 현재 언어로 다시 쓴다(설치돼 있을 때만).
        if (ExplorerMenu.IsInstalled()) ExplorerMenu.Install(_store.Data.Profiles);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ProfileStore>();
        sc.AddSingleton<UsageService>();
        sc.AddSingleton<SessionKeepAliveService>();
        sc.AddSingleton<IDialogService, DialogService>();
        sc.AddSingleton<MainViewModel>();
        sc.AddTransient<MainWindow>();
        return sc.BuildServiceProvider();
    }

    /// <summary>트레이 컨텍스트 메뉴를 현재 프로필 목록으로 다시 구성.</summary>
    private void RebuildMenu()
    {
        var L = LocalizationManager.Instance;
        _store.RefreshAll();
        _menu.Items.Clear();

        var active = _store.Active;
        _menu.Items.Add(new MenuItem
        {
            Header = active is null ? L["TrayNoActive"] : $"{L["TrayActivePrefix"]}{Label(active)}{UsageSuffix(active)}",
            IsEnabled = false,
        });
        _menu.Items.Add(new Separator());

        if (_store.Data.Profiles.Count == 0)
            _menu.Items.Add(new MenuItem { Header = L["TrayNoProfiles"], IsEnabled = false });

        foreach (var p in _store.Data.Profiles)
        {
            bool isActive = p.Id == _store.Data.ActiveProfileId;
            bool hasCreds = _store.HasCredentials(p);
            var item = new MenuItem
            {
                Header = (isActive ? "● " : "") + Label(p) + (hasCreds ? UsageSuffix(p) : L["TrayNeedLogin"]),
                IsEnabled = hasCreds && !isActive,
            };
            var target = p;
            item.Click += (_, _) => QuickSwitch(target);
            _menu.Items.Add(item);
        }

        _menu.Items.Add(new Separator());

        var open = new MenuItem { Header = L["TrayOpen"] };
        open.Click += (_, _) => ShowWindow();
        _menu.Items.Add(open);

        var autostart = new MenuItem
        {
            Header = L["TrayAutostart"],
            IsCheckable = true,
            IsChecked = _store.Data.RunAtStartup ?? AutoStart.IsEnabled(),
        };
        autostart.Click += (_, _) =>
        {
            _store.Data.RunAtStartup = autostart.IsChecked;
            AutoStart.Set(autostart.IsChecked);
            _store.Save();
        };
        _menu.Items.Add(autostart);

        var explorer = new MenuItem
        {
            Header = L["TrayExplorer"],
            IsCheckable = true,
            IsChecked = _store.Data.ExplorerMenu ?? ExplorerMenu.IsInstalled(),
        };
        explorer.Click += (_, _) =>
        {
            _store.Data.ExplorerMenu = explorer.IsChecked;
            if (explorer.IsChecked) ExplorerMenu.Install(_store.Data.Profiles);
            else ExplorerMenu.Uninstall();
            _store.Save();
        };
        _menu.Items.Add(explorer);

        var settings = new MenuItem { Header = L["TraySettings"] };
        settings.Click += (_, _) => _services.GetRequiredService<IDialogService>().ShowSettings();
        _menu.Items.Add(settings);

        var update = new MenuItem { Header = L["TrayCheckUpdate"] };
        update.Click += (_, _) => _ = CheckForUpdatesAsync(manual: true);
        _menu.Items.Add(update);

        _menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = L["TrayExit"] };
        exit.Click += (_, _) => Shutdown();
        _menu.Items.Add(exit);
    }

    private void QuickSwitch(Profile p)
    {
        var L = LocalizationManager.Instance;
        var dialogs = _services.GetRequiredService<IDialogService>();
        if (MainViewModel.IsClaudeRunning() &&
            !dialogs.Confirm(L["WarnTitle"], L["RunningSwitchWarn"]))
        {
            return;
        }

        try
        {
            _store.SwitchTo(p);
            _tray?.ShowNotification(L["TraySwitchedTitle"], L.Tr("TraySwitchedBody", p.Name));
            _mainVm.ReloadFromStore();
            OnDataChanged();
        }
        catch (Exception ex)
        {
            dialogs.ShowError(ex.Message);
        }
    }

    /// <summary>GitHub Releases 에서 새 버전을 확인하고, 있으면 사용자 동의 후 인스톨러를 받아 실행한다.</summary>
    /// <param name="manual">트레이 메뉴로 직접 호출했는지. true 면 "최신 버전" 안내/오류도 표시한다.</param>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        var L = LocalizationManager.Instance;
        var dialogs = _services.GetRequiredService<IDialogService>();
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info is null)
            {
                if (manual) dialogs.ShowInfo(L["UpdTitle"], L["UpdNone"]);
                return;
            }

            string ver = info.Tag.TrimStart('v', 'V');
            if (!dialogs.Confirm(L["UpdTitle"], L.Tr("UpdAvailable", ver)))
                return;

            // 다운로드 진행률 다이얼로그
            using var cts = new CancellationTokenSource();
            var pvm = new ProgressDialogViewModel { Status = L.Tr("UpdDownloading", ver) };
            var dlg = new ProgressDialog { DataContext = pvm };
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
            if (owner is not null) dlg.Owner = owner;
            else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // 취소(버튼·Esc·창 닫기 X)는 VM 의 CancelRequested 로 흘러와 다운로드 토큰을 취소한다.
            pvm.CancelRequested += cts.Cancel;
            dlg.Show();

            void CloseDialog()
            {
                if (dlg.IsVisible) dlg.Close(); // 사용자가 X로 이미 닫았으면 중복 닫기 방지
            }

            try
            {
                var progress = new Progress<double>(p => pvm.Progress = p);
                var path = await UpdateService.DownloadAsync(info, progress, cts.Token);
                // 성공: 창을 닫으면 WindowClosingCommandBehavior 가 CancelCommand 를 한 번 더 호출하지만
                // 다운로드는 이미 끝났으므로 토큰 취소는 무해하다(이후 작업 추가 시 이 점에 유의).
                CloseDialog();
                UpdateService.RunInstaller(path);
                Shutdown(); // 인스톨러가 교체할 수 있도록 종료
            }
            catch (OperationCanceledException)
            {
                CloseDialog(); // 사용자가 취소
            }
            catch (Exception ex)
            {
                CloseDialog();
                if (manual) dialogs.ShowError(ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (manual) dialogs.ShowError(ex.Message);
            // 자동 확인은 실패해도 조용히 넘어간다(오프라인 등).
        }
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            _window = _services.GetRequiredService<MainWindow>();
            _window.Closing += (_, args) =>
            {
                args.Cancel = true;   // 닫지 말고 트레이로 숨김
                _window!.Hide();
            };
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>프로필 목록이 바뀌면 트레이 메뉴 + 탐색기 서브메뉴를 갱신.</summary>
    private void OnDataChanged()
    {
        RebuildMenu();
        if (ExplorerMenu.IsInstalled())
            ExplorerMenu.Sync(_store.Data.Profiles);
    }

    /// <summary>--launch &lt;id&gt; --dir &lt;path&gt; 인자 처리. 처리했으면 true.</summary>
    private bool TryHandleCliLaunch(string[] args)
    {
        int li = Array.IndexOf(args, "--launch");
        if (li < 0 || li + 1 >= args.Length) return false;
        string id = args[li + 1];

        string? dir = null;
        int di = Array.IndexOf(args, "--dir");
        if (di >= 0 && di + 1 < args.Length) dir = args[di + 1];

        _store.Load();
        var p = _store.Data.Profiles.FirstOrDefault(x => x.Id == id);
        if (p is not null)
        {
            // 스킵권한: 기억값이 있으면 그대로 쓰고, "매번 묻기"(null)면 다이얼로그로 물어본다(취소 시 실행 안 함).
            bool? skip = _store.Data.SkipPermissions;
            if (skip is null)
            {
                InitLanguage(); // 독립 실행이라 다이얼로그를 현재 언어로 띄우려면 먼저 언어를 적용
                var res = _services.GetRequiredService<IDialogService>().AskSkipPermissions(p.Name);
                if (res is null) return true; // 사용자가 취소 → 실행하지 않음
                if (res.Remember)
                {
                    _store.Data.SkipPermissions = res.SkipPermissions;
                    _store.Save();
                }
                skip = res.SkipPermissions;
            }

            try { Launcher.LaunchInProfile(p, dir, _store.Data.Shell, skip.Value, _store.Data.StatusLine); } catch { /* best effort */ }
        }
        return true;
    }

    /// <summary>메뉴 표시용 라벨: 이름 + (이메일).</summary>
    private static string Label(Profile p) =>
        string.IsNullOrEmpty(p.Email) ? p.Name : $"{p.Name}  ({p.Email})";

    /// <summary>세션 남은 사용량을 메뉴 라벨 뒤에 붙인다(조회 전/실패면 빈 문자열).</summary>
    private static string UsageSuffix(Profile p) =>
        string.IsNullOrEmpty(p.SessionRemaining) || p.SessionRemaining is "—" or "…"
            ? "" : $"   · {LocalizationManager.Instance["TraySessionPrefix"]}{p.SessionRemaining}";

    /// <summary>
    /// 프로필 폴더의 .claude.json 변경(격리 로그인 완료/계정 갱신)을 감시해 자동으로 목록·메뉴를 새로고침한다.
    /// .credentials.json 은 우리 토큰 갱신이 건드리므로 감시 대상에서 제외(되먹임 방지).
    /// </summary>
    private void SetupProfileWatcher()
    {
        try
        {
            AppPaths.EnsureDirs();

            _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _reloadDebounce.Tick += (_, _) =>
            {
                _reloadDebounce!.Stop();
                _mainVm.ReloadFromStore();
                OnDataChanged();
            };

            _watcher = new FileSystemWatcher(AppPaths.ProfilesDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                Filter = "*.json",
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnProfileFileChanged;
            _watcher.Changed += OnProfileFileChanged;
            _watcher.Renamed += OnProfileFileChanged;
        }
        catch { /* 감시 실패해도 수동 새로고침으로 동작 */ }
    }

    private void OnProfileFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!string.Equals(Path.GetFileName(e.FullPath), ".claude.json", StringComparison.OrdinalIgnoreCase))
            return;
        // 워처 스레드 → UI 스레드로 옮겨 디바운스(연속 쓰기 합치기)
        Dispatcher.BeginInvoke(() =>
        {
            _reloadDebounce!.Stop();
            _reloadDebounce.Start();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _tray?.Dispose();
        (_services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
