using System.IO;
using System.Text.Json;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 프로필별 대화 세션(claude 트랜스크립트)을 열거하고, 한 계정의 세션을 다른 계정으로
/// 복사해 <c>claude --resume</c> 로 이어하게 한다.
/// 세션 파일은 <c>&lt;configdir&gt;\projects\&lt;enc&gt;\&lt;id&gt;.jsonl</c> 형태이고, <c>&lt;enc&gt;</c> 는
/// cwd 로만 결정되므로(계정 무관) 복사 시 소스의 폴더명을 그대로 재사용한다.
/// </summary>
public sealed class SessionStore
{
    private const string ProjectsFolder = "projects";
    private const int MaxScanLines = 60; // cwd/미리보기는 파일 앞부분에 있으므로 앞줄만 훑는다.

    /// <summary>
    /// 프로필의 세션 목록. 활성 프로필이면 실제 라이브 저장소(~/.claude)도 함께 훑는다
    /// (활성 계정은 전환 시 ~/.claude 를 쓰고, 동시 실행 때만 프로필 폴더에 쌓기 때문).
    /// 최근 수정 순으로 정렬.
    /// </summary>
    public IReadOnlyList<SessionEntry> ListForProfile(Profile p, bool isActive)
    {
        var dirs = new List<string> { p.ConfigDir };
        if (isActive) dirs.Add(AppPaths.ClaudeHome);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // 중복 세션 id 제거
        var result = new List<SessionEntry>();

        foreach (var configDir in dirs)
        {
            string projects = Path.Combine(configDir, ProjectsFolder);
            if (!Directory.Exists(projects)) continue;

            string[] files;
            try { files = Directory.GetFiles(projects, "*.jsonl", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var file in files)
            {
                try
                {
                    string id = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(id)) continue;

                    var (cwd, preview) = ScanHead(file);
                    result.Add(new SessionEntry
                    {
                        SessionId = id,
                        ProjectFolder = Path.GetFileName(Path.GetDirectoryName(file)!),
                        Cwd = cwd ?? "",
                        FilePath = file,
                        ProfileId = p.Id,
                        ProfileName = p.Name,
                        LastModified = File.GetLastWriteTime(file),
                        Preview = preview,
                    });
                }
                catch { /* 한 파일이 깨져도 나머지는 계속 */ }
            }
        }

        result.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
        return result;
    }

    /// <summary>
    /// 세션 파일을 대상 프로필의 격리 설정 폴더로 복사한다(같은 projects\&lt;enc&gt; 경로).
    /// 이미 있으면 덮어쓰지 않는다(대상에서 이미 이어가던 대화를 보존). 실행할 세션 id 를 반환.
    /// </summary>
    public string ImportInto(SessionEntry s, Profile dest)
    {
        string destProjects = Path.Combine(dest.ConfigDir, ProjectsFolder, s.ProjectFolder);
        Directory.CreateDirectory(destProjects);
        string destFile = Path.Combine(destProjects, s.SessionId + ".jsonl");

        // 이미 대상에 있는 세션(자기 자신으로 이어하기 포함)이면 그대로 이어간다.
        if (!File.Exists(destFile) &&
            !string.Equals(Path.GetFullPath(s.FilePath), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(s.FilePath, destFile);
        }

        return s.SessionId;
    }

    /// <summary>파일 앞부분만 훑어 (cwd, 미리보기)를 뽑는다. summary 우선, 없으면 첫 사용자 메시지.</summary>
    private static (string? Cwd, string? Preview) ScanHead(string file)
    {
        string? cwd = null;
        string? summary = null;
        string? firstUser = null;

        using var reader = new StreamReader(file);
        for (int i = 0; i < MaxScanLines; i++)
        {
            string? line = reader.ReadLine();
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement root;
            try { using var doc = JsonDocument.Parse(line); root = doc.RootElement.Clone(); }
            catch { continue; }

            if (root.ValueKind != JsonValueKind.Object) continue;

            if (cwd is null && root.TryGetProperty("cwd", out var cwdEl) && cwdEl.ValueKind == JsonValueKind.String)
            {
                var v = cwdEl.GetString();
                if (!string.IsNullOrWhiteSpace(v)) cwd = v;
            }

            if (summary is null && root.TryGetProperty("type", out var typeEl) &&
                typeEl.ValueKind == JsonValueKind.String && typeEl.GetString() == "summary" &&
                root.TryGetProperty("summary", out var sumEl) && sumEl.ValueKind == JsonValueKind.String)
            {
                summary = sumEl.GetString();
            }

            if (firstUser is null && root.TryGetProperty("type", out var t2) &&
                t2.ValueKind == JsonValueKind.String && t2.GetString() == "user" &&
                root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
                msg.TryGetProperty("content", out var content))
            {
                firstUser = ExtractText(content);
            }

            // cwd 와 미리보기(요약이면 최상)를 확보했으면 조기 종료.
            if (cwd is not null && summary is not null) break;
        }

        string? preview = Clean(summary ?? firstUser);
        return (cwd, preview);
    }

    /// <summary>메시지 content(문자열 또는 파트 배열)에서 첫 텍스트를 뽑는다.</summary>
    private static string? ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object &&
                    part.TryGetProperty("type", out var pt) && pt.GetString() == "text" &&
                    part.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                {
                    return txt.GetString();
                }
            }
        }
        return null;
    }

    /// <summary>미리보기 텍스트를 한 줄로 정리하고 길이를 제한한다.</summary>
    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = string.Join(' ', s.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        // 슬래시 명령(<command-name> 등 하네스 주입)으로 시작하면 미리보기로 부적합 → 그대로 두되 길이만 제한.
        const int max = 140;
        return s.Length <= max ? s : s[..max] + "…";
    }
}
