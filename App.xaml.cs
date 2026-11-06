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
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeAccountSwitcher;

/// <summary>
/// 합성 루트(Composition Root). DI 컨테이너 구성 + 단일 인스턴스 + 트레이 상주 + 관리 창/다이얼로그 호스팅.
/// </summary>
public partial class App : Application
{
    private const int TrayRecentSessions = 8;

    private IServiceProvider _services = null!;
    private ProfileStore _store = null!;
    private MainViewModel _mainVm = null!;
    private SingleInstance? _instance;
    private TaskbarIcon? _tray;
    private ContextMenu _menu = null!;
    private MainWindow? _window;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadDebounce;

    private static LocalizationManager L => LocalizationManager.Instance;

    private IDialogService Dialogs => _services.GetRequiredService<IDialogService>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = BuildServiceProvider();
        _store = _services.GetRequiredService<ProfileStore>();

        // 탐색기 우클릭 메뉴에서 호출된 경우: 트레이 없이 해당 계정으로 실행만 하고 종료
        if (TryHandleCliLaunch(e.Args)) { Shutdown(); return; }

        // UI 자동 검증용: 창을 그려 PNG 로 저장하고 종료(트레이·단일 인스턴스 검사 없음).
        if (TryHandleScreenshot(e.Args)) return;

        // 이미 떠 있으면 그 인스턴스의 관리 창을 열어 주고 끝낸다(트레이 아이콘이 둘 생기지 않게).
        _instance = new SingleInstance();
        if (!_instance.TryAcquire()) { Shutdown(); return; }

        InstallExceptionHandlers();

        _store.Load();
        _store.PruneOrphanDirs();   // 목록에 없고 데이터도 없는 빈 프로필 폴더 정리
        ReconcileStartupSettings(); // 저장된 설정대로 자동실행/탐색기 레지스트리 복원(업데이트 후 초기화 방지)
        InitLanguage();

        _mainVm = _services.GetRequiredService<MainViewModel>();
        _mainVm.Changed += OnDataChanged;
        _mainVm.UsageUpdated += RebuildMenu; // 사용량 조회 완료 시 트레이 라벨만 갱신
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        SetupProfileWatcher();
        SetupTray();
        _instance.ListenForActivation(Dispatcher, ShowWindow);

        // 수동 실행(자동 실행 인자 없음)이면 트레이에 들어갔음을 토스트로 알린다(첫 사용자 "안 켜졌나?" 혼란 방지).
        // --show 로 띄우면 토스트 대신 관리 창을 곧바로 연다(바로가기/스크립트에서 창을 열고 싶을 때).
        if (e.Args.Contains("--show")) ShowWindow();
        else if (!e.Args.Contains("--autostart")) Notify("Claude Account Switcher", L["TrayRunningBody"]);

        // 세션 자동 유지 감시 시작(KeepSessionAlive 켜진 프로필의 5시간 창을 리셋 즉시 재시작).
        _services.GetRequiredService<SessionKeepAliveService>().Start();

        // 업데이트: 시작 직후 1회(네트워크 대기 후) + 6시간마다 조용히 확인. 새 버전이 있을 때만 안내.
        var updates = _services.GetRequiredService<UpdateCoordinator>();
        updates.RestartRequested += Shutdown;
        updates.StartPeriodic();
    }

    private static IServiceProvider BuildServiceProvider() => new ServiceCollection()
        .AddSingleton<ProfileStore>()
        .AddSingleton<SessionStore>()
        .AddSingleton<UsageService>()
        .AddSingleton<SessionKeepAliveService>()
        .AddSingleton<LaunchService>()
        .AddSingleton<UpdateCoordinator>()
        .AddSingleton<IDialogService, DialogService>()
        .AddSingleton<MainViewModel>()
        .AddTransient<MainWindow>()
        .AddTransient<SettingsViewModel>()
        .AddTransient<SessionBrowserViewModel>()
        .BuildServiceProvider();

    /// <summary>
    /// 처리되지 않은 예외로 트레이 앱이 소리 없이 사라지지 않게 한다: UI 스레드 예외는 테마 오류 창으로 보여주고
    /// 계속 실행하고, 관찰되지 않은 Task 예외는 프로세스를 죽이지 않도록 관찰 처리한다.
    /// </summary>
    private void InstallExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            try { Dialogs.ShowError(L.Tr("ErrUnexpected", args.Exception.Message)); }
            catch { /* 오류 창마저 실패하면 조용히 넘어간다 */ }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();
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

            // 탐색기 메뉴: 미설정이면 시드. ON 인데 지워졌으면 복원, OFF 인데 남아있으면 제거.
            data.ExplorerMenu ??= ExplorerMenu.IsInstalled();
            if (data.ExplorerMenu.Value && !ExplorerMenu.IsInstalled()) ExplorerMenu.Install(data.Profiles);
            else if (!data.ExplorerMenu.Value && ExplorerMenu.IsInstalled()) ExplorerMenu.Uninstall();

            _store.Save();
        }
        catch { /* best effort — 복원 실패해도 앱은 동작 */ }
    }

    /// <summary>첫 실행이면 윈도우 언어로 기본값을 정하고, 저장된 언어를 적용한다.</summary>
    private void InitLanguage()
    {
        string? lang = _store.Data.Language;
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

    // ---------------------------------------------------------------- 트레이

    private void SetupTray()
    {
        _tray = new TaskbarIcon { ToolTipText = "Claude Account Switcher", Visibility = Visibility.Visible };
        try
        {
            _tray.IconSource = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/app.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch { /* 아이콘 로드 실패해도 기능은 동작 */ }

        _menu = new ContextMenu();
        _menu.Opened += (_, _) => RebuildMenu();
        _tray.ContextMenu = _menu;
        _tray.TrayLeftMouseUp += (_, _) => ShowWindow();
        _tray.TrayBalloonTipClicked += (_, _) => ShowWindow(); // 토스트 클릭 → 관리 창 열기

        RebuildMenu();
        _tray.ForceCreate();
    }

    /// <summary>트레이 컨텍스트 메뉴를 현재 프로필 목록으로 다시 구성. 툴팁도 활성 계정/잔량으로 갱신.</summary>
    private void RebuildMenu()
    {
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

        var signedIn = _store.Data.Profiles.Where(_store.HasCredentials).ToList();
        foreach (var p in _store.Data.Profiles)
        {
            bool isActive = p.Id == _store.Data.ActiveProfileId;
            bool hasCreds = _store.HasCredentials(p);
            MenuItem item = new()
            {
                Header = (isActive ? "● " : "") + Label(p) + (hasCreds ? UsageSuffix(p) : L["TrayNeedLogin"]),
                IsEnabled = hasCreds && !isActive,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            };
            item.Click += (_, _) => QuickSwitch(p);
            _menu.Items.Add(item);
        }

        _menu.Items.Add(new Separator());

        // 새 창 실행 ▸ 계정 — 폴더를 골라 그 계정으로 바로 띄운다.
        MenuItem launch = new() { Header = L["TrayLaunch"], IsEnabled = signedIn.Count > 0 };
        foreach (var p in signedIn)
        {
            MenuItem li = new() { Header = Label(p) };
            li.Click += (_, _) => _mainVm.DoLaunch(p);
            launch.Items.Add(li);
        }
        _menu.Items.Add(launch);

        // 최근 세션 이어하기 ▸ (전 계정 통합, 열 때 비동기로 채움)
        MenuItem recent = new() { Header = L["TrayRecentSessions"], IsEnabled = signedIn.Count > 0 };
        recent.Items.Add(new MenuItem { Header = L["CtxLoading"], IsEnabled = false });
        recent.SubmenuOpened += async (_, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, recent)) await FillRecentSessionsAsync(recent, signedIn);
        };
        _menu.Items.Add(recent);

        _menu.Items.Add(new Separator());

        _menu.Items.Add(Item(L["TrayOpen"], ShowWindow, bold: true));

        MenuItem autostart = new() { Header = L["TrayAutostart"], IsCheckable = true, IsChecked = _store.Data.RunAtStartup ?? AutoStart.IsEnabled() };
        autostart.Click += (_, _) =>
        {
            _store.Data.RunAtStartup = autostart.IsChecked;
            AutoStart.Set(autostart.IsChecked);
            _store.Save();
        };
        _menu.Items.Add(autostart);

        MenuItem explorer = new() { Header = L["TrayExplorer"], IsCheckable = true, IsChecked = _store.Data.ExplorerMenu ?? ExplorerMenu.IsInstalled() };
        explorer.Click += (_, _) =>
        {
            _store.Data.ExplorerMenu = explorer.IsChecked;
            if (explorer.IsChecked) ExplorerMenu.Install(_store.Data.Profiles);
            else ExplorerMenu.Uninstall();
            _store.Save();
        };
        _menu.Items.Add(explorer);

        _menu.Items.Add(Item(L["TraySettings"], () => Dialogs.ShowSettings()));
        _menu.Items.Add(Item(L["TrayCheckUpdate"], () => _ = _services.GetRequiredService<UpdateCoordinator>().CheckAsync(manual: true)));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item(L["TrayExit"], Shutdown));

        if (_tray is not null) _tray.ToolTipText = BuildTrayTooltip(active);

        static MenuItem Item(string header, Action onClick, bool bold = false)
        {
            MenuItem mi = new() { Header = header, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            mi.Click += (_, _) => onClick();
            return mi;
        }
    }

    /// <summary>트레이 "최근 세션 이어하기" 서브메뉴를 전 계정의 최근 세션으로 채운다(열릴 때마다 새로).</summary>
    private async Task FillRecentSessionsAsync(MenuItem menu, IReadOnlyList<Profile> profiles)
    {
        var sessions = _services.GetRequiredService<SessionStore>();
        string? activeId = _store.Data.ActiveProfileId;
        List<(SessionEntry Entry, Profile Profile)> all = [];
        try
        {
            all = await Task.Run(() => profiles
                .SelectMany(p => sessions.ListRecentForProfile(p, p.Id == activeId, TrayRecentSessions).Select(e => (e, p)))
                .OrderByDescending(x => x.e.LastModified)
                .Take(TrayRecentSessions)
                .ToList());
        }
        catch { /* 목록을 못 읽어도 메뉴는 유지 */ }

        if (!menu.IsSubmenuOpen) return; // 기다리는 동안 닫혔으면 다음에 다시
        menu.Items.Clear();
        if (all.Count == 0) menu.Items.Add(new MenuItem { Header = L["CtxNoSessions"], IsEnabled = false });
        foreach (var (entry, profile) in all)
        {
            var row = SessionMenuEntry.For(entry, new RelayCommand(() => { }), includeAccount: true);
            MenuItem mi = new() { Header = row.Header, InputGestureText = row.Gesture, ToolTip = row.Tip };
            mi.Click += (_, _) => _ = _mainVm.ResumeAsync(entry, profile);
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        MenuItem browse = new() { Header = L["CtxBrowseSessions"] };
        browse.Click += (_, _) => Dialogs.ShowSessionBrowser();
        menu.Items.Add(browse);
    }

    private string BuildTrayTooltip(Profile? active)
    {
        if (active is null) return $"Claude Account Switcher\n{L["TrayNoActive"]}";
        string usage = active.LastUsage is { } u ? $"  ·  {UsageFormatter.Inline(u)}" : "";
        string tip = $"Claude Account Switcher\n{L["TrayActivePrefix"]}{active.Name}{usage}";
        return tip.Length > 127 ? tip[..127] : tip; // Shell_NotifyIcon 툴팁 상한
    }

    private void QuickSwitch(Profile p)
    {
        if (_mainVm.DoSwitch(p))
        {
            Notify(L["TraySwitchedTitle"], L.Tr("TraySwitchedBody", p.Name));
            OnDataChanged();
        }
    }

    private void Notify(string title, string body)
    {
        try { _tray?.ShowNotification(title, body, NotificationIcon.Info); }
        catch { /* 알림 실패는 무시 */ }
    }

    // ---------------------------------------------------------------- 관리 창

    private void ShowWindow()
    {
        if (_window is null)
        {
            _window = _services.GetRequiredService<MainWindow>();
            _window.Closing += (_, args) =>
            {
                args.Cancel = true; // 닫지 말고 트레이로 숨김
                _window!.Hide();
                ShowCloseToTrayHintOnce();
            };
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        // 다른 앱이 포그라운드일 때 Activate 만으로는 앞으로 안 올 수 있어 Topmost 를 잠깐 토글한다.
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    /// <summary>X 로 닫았을 때 앱이 사라진 게 아니라 트레이에 남아 있음을 처음 한 번만 알려준다.</summary>
    private void ShowCloseToTrayHintOnce()
    {
        if (_store.Data.CloseToTrayHintShown) return;
        _store.Data.CloseToTrayHintShown = true;
        _store.Save();
        Notify(L["TrayCloseHintTitle"], L["TrayCloseHintBody"]);
    }

    /// <summary>프로필 목록이 바뀌면 트레이 메뉴 + 탐색기 서브메뉴를 갱신.</summary>
    private void OnDataChanged()
    {
        RebuildMenu();
        if (ExplorerMenu.IsInstalled()) ExplorerMenu.Sync(_store.Data.Profiles);
    }

    /// <summary>--launch &lt;id&gt; --dir &lt;path&gt; 인자 처리. 처리했으면 true.</summary>
    private bool TryHandleCliLaunch(string[] args)
    {
        int li = Array.IndexOf(args, "--launch");
        if (li < 0 || li + 1 >= args.Length) return false;
        string id = args[li + 1];

        int di = Array.IndexOf(args, "--dir");
        string dir = di >= 0 && di + 1 < args.Length ? args[di + 1] : AppPaths.UserHome;

        _store.Load();
        if (_store.Data.Profiles.FirstOrDefault(x => x.Id == id) is { } p)
        {
            InitLanguage(); // 독립 실행이라 스킵권한 다이얼로그를 현재 언어로 띄우려면 먼저 언어를 적용
            try { _services.GetRequiredService<LaunchService>().LaunchNewWindow(p, dir); }
            catch { /* best effort */ }
        }
        return true;
    }

    /// <summary>
    /// <c>--screenshot &lt;png&gt; [--page main|settings|sessions]</c>: 해당 창을 띄워 렌더링한 뒤 PNG 로 저장하고 종료한다.
    /// 사람이 보지 않고도(에이전트·CI) UI 변경을 확인하기 위한 개발용 모드. 처리했으면 true.
    /// </summary>
    private bool TryHandleScreenshot(string[] args)
    {
        int si = Array.IndexOf(args, "--screenshot");
        if (si < 0 || si + 1 >= args.Length) return false;
        string output = args[si + 1];
        int pi = Array.IndexOf(args, "--page");
        string page = pi >= 0 && pi + 1 < args.Length ? args[pi + 1] : "main";

        _store.Load();
        InitLanguage();
        _mainVm = _services.GetRequiredService<MainViewModel>();

        // 바인딩 오류(화면엔 안 보이고 디버거 출력에만 남는다)를 <png>.binding.log 로 남겨 검증에 쓴다.
        System.Diagnostics.PresentationTraceSources.Refresh();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(output + ".binding.log"));
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;

        Window window = page switch
        {
            "settings" => new SettingsWindow { DataContext = _services.GetRequiredService<SettingsViewModel>() },
            "sessions" => new SessionBrowserWindow { DataContext = _services.GetRequiredService<SessionBrowserViewModel>() },
            _ => _services.GetRequiredService<MainWindow>(),
        };
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();

        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(3000); // 사용량 조회·애니메이션이 안정될 때까지
            try
            {
                if (page == "rowmenu") await RenderRowMenuAsync(window, output);
                else RenderToPng(window, output);
            }
            finally
            {
                System.Diagnostics.Trace.Flush();
                Shutdown();
            }
        });
        return true;
    }

    /// <summary>첫 행의 우클릭 메뉴를 열어 렌더하고, "최근 세션" 서브메뉴를 열어 로딩 결과를 함께 저장한다(검증용).</summary>
    private static async Task RenderRowMenuAsync(Window window, string output)
    {
        var list = FindDescendant<ListView>(window);
        if (list is null || list.Items.Count == 0) return;
        if (list.ItemContainerGenerator.ContainerFromIndex(0) is not ListViewItem { ContextMenu: { } menu } row) return;

        menu.PlacementTarget = row;
        menu.IsOpen = true;
        await Task.Delay(600);
        RenderToPng(menu, output);

        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.ItemsSource is not null) is { } recent)
        {
            recent.IsSubmenuOpen = true;
            await Task.Delay(2500); // 세션 목록 비동기 로딩
            var headers = recent.Items.Cast<object>().Select(i => i switch
            {
                ViewModels.SessionMenuEntry e => $"[{e.Kind}] {e.Header}  ({e.Gesture})",
                _ => i.ToString() ?? "",
            });
            await File.WriteAllLinesAsync(output + ".submenu.txt", headers);
            if (recent.Template.FindName("Popup", recent) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement sub })
                RenderToPng(sub, Path.ChangeExtension(output, ".submenu.png"));
        }
        menu.IsOpen = false;
    }

    private static void RenderToPng(FrameworkElement element, string output)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(element);
        RenderTargetBitmap bitmap = new((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(element);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(output);
        encoder.Save(fs);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>메뉴 표시용 라벨: 이름 + (이메일).</summary>
    private static string Label(Profile p) =>
        string.IsNullOrEmpty(p.Email) ? p.Name : $"{p.Name}  ({p.Email})";

    /// <summary>
    /// 잔량을 메뉴 라벨 뒤에 붙인다(조회 전/실패면 빈 문자열). 토큰이 만료된 계정은 "다시 로그인 필요".
    /// 5시간 창이 없는 플랜은 "세션" 접두 없이 주간 값("주간 85%")만.
    /// </summary>
    private static string UsageSuffix(Profile p) => p switch
    {
        { NeedsRelogin: true } => L["TrayNeedRelogin"],
        { LastUsage: { HasSessionWindow: false } u } => $"   · {UsageFormatter.Inline(u)}",
        { LastUsage: { } u } => $"   · {L["TraySessionPrefix"]}{UsageFormatter.Inline(u)}",
        _ => "",
    };

    // ---------------------------------------------------------------- 파일 감시

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
                _reloadDebounce.Stop();
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
        if (!string.Equals(Path.GetFileName(e.FullPath), ".claude.json", StringComparison.OrdinalIgnoreCase)) return;
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
        _instance?.Dispose();
        (_services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
