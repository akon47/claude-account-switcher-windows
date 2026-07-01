using System.IO;
using ClaudeAccountSwitcher.Localization;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.ViewModels;

/// <summary>세션 목록의 한 행(표시용). 원본 <see cref="SessionEntry"/> 를 감싼다.</summary>
public sealed class SessionItemViewModel
{
    public required SessionEntry Entry { get; init; }

    /// <summary>프로젝트 이름(작업 폴더의 마지막 구성요소). cwd 가 비면 인코딩된 폴더명으로 대체.</summary>
    public string Project =>
        string.IsNullOrEmpty(Entry.Cwd)
            ? Entry.ProjectFolder
            : Path.GetFileName(Entry.Cwd.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Entry.Cwd;

    /// <summary>전체 작업 폴더 경로(툴팁).</summary>
    public string Cwd => Entry.Cwd;

    public string Preview => Entry.Preview ?? "";

    public string ProfileName => Entry.ProfileName;

    /// <summary>상대 시각(방금/N분 전/N시간 전/N일 전) — 최근 대화 파악용.</summary>
    public string When
    {
        get
        {
            var span = DateTime.Now - Entry.LastModified;
            var L = LocalizationManager.Instance;
            if (span.TotalMinutes < 1) return L["TimeJustNow"];
            if (span.TotalHours < 1) return L.Tr("TimeMinutesAgo", (int)span.TotalMinutes);
            if (span.TotalDays < 1) return L.Tr("TimeHoursAgo", (int)span.TotalHours);
            if (span.TotalDays < 30) return L.Tr("TimeDaysAgo", (int)span.TotalDays);
            return Entry.LastModified.ToString("yyyy-MM-dd");
        }
    }
}
