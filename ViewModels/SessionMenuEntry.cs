using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>"최근 세션 이어하기" 메뉴의 항목 종류.</summary>
public enum SessionMenuEntryKind
{
    /// <summary>실제 세션(누르면 이어하기).</summary>
    Session,

    /// <summary>목록을 읽는 중(비활성 자리표시자).</summary>
    Loading,

    /// <summary>세션이 하나도 없음(비활성 자리표시자).</summary>
    Empty,

    /// <summary>세션 브라우저에서 전체 보기.</summary>
    Browse,
}

/// <summary>
/// 행 메뉴/트레이 메뉴에 뜨는 "최근 세션" 한 줄. 세션 항목이면 <see cref="Entry"/> 를 들고, 누르면 <see cref="Command"/> 실행.
/// 자리표시자(불러오는 중 / 없음)는 비활성으로 표시한다.
/// </summary>
public sealed class SessionMenuEntry
{
    private const int MaxHeaderLength = 64;

    public required SessionMenuEntryKind Kind { get; init; }
    public required string Header { get; init; }

    /// <summary>오른쪽 보조 텍스트(상대 시각 "2시간 전").</summary>
    public string? Gesture { get; init; }

    public string? Tip { get; init; }
    public SessionEntry? Entry { get; init; }
    public IRelayCommand? Command { get; init; }

    public bool IsEnabled => Kind is SessionMenuEntryKind.Session or SessionMenuEntryKind.Browse;

    /// <summary>세션 항목: "프로젝트 · 세션 이름(없으면 미리보기)" + 상대 시각. 툴팁에 미리보기와 작업 폴더.</summary>
    public static SessionMenuEntry For(SessionEntry e, IRelayCommand command, bool includeAccount = false)
    {
        string project = SessionItemViewModel.ProjectNameOf(e);
        string title = e.Name ?? e.Preview ?? e.SessionId;
        string header = includeAccount ? $"{e.ProfileName}  ›  {project}  ·  {title}" : $"{project}  ·  {title}";
        if (header.Length > MaxHeaderLength) header = header[..MaxHeaderLength] + "…";

        string tip = string.Join('\n', new[] { e.Name, e.Preview, e.Cwd }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        return new SessionMenuEntry
        {
            Kind = SessionMenuEntryKind.Session,
            Header = header,
            Gesture = SessionItemViewModel.RelativeTime(e.LastModified),
            Tip = tip.Length == 0 ? null : tip,
            Entry = e,
            Command = command,
        };
    }

    public static SessionMenuEntry Placeholder(SessionMenuEntryKind kind) => new()
    {
        Kind = kind,
        Header = LocalizationManager.Instance[kind == SessionMenuEntryKind.Loading ? "CtxLoading" : "CtxNoSessions"],
    };

    public static SessionMenuEntry Browse(IRelayCommand command) => new()
    {
        Kind = SessionMenuEntryKind.Browse,
        Header = LocalizationManager.Instance["CtxBrowseSessions"],
        Command = command,
    };
}
