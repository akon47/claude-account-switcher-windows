using System.IO;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>세션 목록의 한 행(표시용). 원본 <see cref="SessionEntry"/> 를 감싼다.</summary>
public sealed class SessionItemViewModel
{
    public required SessionEntry Entry { get; init; }

    /// <summary>프로젝트 이름(작업 폴더의 마지막 구성요소). cwd 가 비면 인코딩된 폴더명으로 대체.</summary>
    public string Project => ProjectNameOf(Entry);

    /// <summary>전체 작업 폴더 경로(툴팁).</summary>
    public string Cwd => Entry.Cwd;

    /// <summary>Claude Code 가 붙인 세션 이름(ai-title). 없으면 빈 문자열.</summary>
    public string Name => Entry.Name ?? "";

    /// <summary>번들에 이 세션의 작업 폴더가 함께 담겨 있는지(가져오기 목록에서 📁 표시).</summary>
    public bool HasWorkdir => Entry.BundleWorkdirPath is not null;

    public string Preview => Entry.Preview ?? "";

    public string ProfileName => Entry.ProfileName;

    /// <summary>상대 시각(방금/N분 전/N시간 전/N일 전) — 최근 대화 파악용.</summary>
    public string When => RelativeTime(Entry.LastModified);

    public static string ProjectNameOf(SessionEntry e) =>
        string.IsNullOrEmpty(e.Cwd)
            ? e.ProjectFolder
            : Path.GetFileName(e.Cwd.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : e.Cwd;

    public static string RelativeTime(DateTime at)
    {
        var span = DateTime.Now - at;
        var L = LocalizationManager.Instance;
        return span switch
        {
            { TotalMinutes: < 1 } => L["TimeJustNow"],
            { TotalHours: < 1 } => L.Tr("TimeMinutesAgo", (int)span.TotalMinutes),
            { TotalDays: < 1 } => L.Tr("TimeHoursAgo", (int)span.TotalHours),
            { TotalDays: < 30 } => L.Tr("TimeDaysAgo", (int)span.TotalDays),
            _ => at.ToString("yyyy-MM-dd"),
        };
    }
}
