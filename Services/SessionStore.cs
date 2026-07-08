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
    private const int MaxScanLines = 120; // cwd/미리보기는 앞부분에 있음. 앞선 슬래시 명령·메타 메시지를 건너뛸 여유.

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
                    // 서브에이전트(sidechain) 트랜스크립트는 이어하기 대상이 아니다(대량으로 생기고 영어
                    // 에이전트 프롬프트가 첫 메시지) → 제외. Claude Code 는 이를 agent-*.jsonl 로 만든다(빠른 필터).
                    if (Path.GetFileName(file).StartsWith("agent-", StringComparison.OrdinalIgnoreCase)) continue;

                    string id = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(id)) continue;

                    var (cwd, preview, isSidechain, firstTitle) = ScanHead(file);
                    if (isSidechain) continue; // agent- 규칙을 벗어난 sidechain 까지 방어

                    // 세션 이름(ai-title)은 대화가 진행되며 갱신되므로 파일 끝쪽의 마지막 값이 현재 이름.
                    // 앞부분(firstTitle)은 꼬리를 못 읽었을 때의 폴백.
                    string? name = ReadLastAiTitle(file) ?? firstTitle;

                    result.Add(new SessionEntry
                    {
                        SessionId = id,
                        ProjectFolder = Path.GetFileName(Path.GetDirectoryName(file)!),
                        Cwd = cwd ?? "",
                        FilePath = file,
                        ProfileId = p.Id,
                        ProfileName = p.Name,
                        SourceEmail = string.IsNullOrEmpty(p.Email) ? null : p.Email,
                        LastModified = File.GetLastWriteTime(file),
                        Preview = preview,
                        Name = Clean(name),
                    });
                }
                catch { /* 한 파일이 깨져도 나머지는 계속 */ }
            }
        }

        result.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
        return result;
    }

    /// <summary>
    /// 세션 파일을 대상 프로필의 격리 설정 폴더로 복사한다(projects\&lt;enc&gt; 경로).
    /// 이미 있으면 덮어쓰지 않는다(대상에서 이미 이어가던 대화를 보존). 실행할 세션 id 를 반환.
    /// <paramref name="overrideProjectFolder"/> 를 주면 소스 폴더명 대신 그걸 대상 enc 폴더로 쓴다
    /// (다른 PC 로 옮겨 작업 폴더가 바뀌었을 때 cwd 로 다시 인코딩한 폴더명 전달).
    /// </summary>
    public string ImportInto(SessionEntry s, Profile dest, string? overrideProjectFolder = null)
    {
        string projectFolder = string.IsNullOrEmpty(overrideProjectFolder) ? s.ProjectFolder : overrideProjectFolder;
        string destProjects = Path.Combine(dest.ConfigDir, ProjectsFolder, projectFolder);
        Directory.CreateDirectory(destProjects);
        string destFile = Path.Combine(destProjects, s.SessionId + ".jsonl");

        // 이미 대상에 있는 세션(자기 자신으로 이어하기 포함)이면 그대로 이어간다.
        if (!File.Exists(destFile) &&
            !string.Equals(Path.GetFullPath(s.FilePath), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(s.FilePath, destFile);
        }

        CopyLinkedSubAgents(s, destProjects);
        return s.SessionId;
    }

    /// <summary>
    /// 이 세션에 연결된 서브에이전트(sidechain) 트랜스크립트도 대상 폴더로 함께 복사한다.
    /// 두 구조를 모두 다룬다:
    /// (신) 세션별 사이드카 폴더 <c>&lt;enc&gt;\&lt;id&gt;\</c>(subagents\agent-*.jsonl 등)를 통째로 복사.
    /// (구) <c>&lt;enc&gt;</c> 에 평평하게 놓인 agent-*.jsonl 중 내부 sessionId 가 이 세션인 것.
    /// (이어하기 자체엔 없어도 되지만, 대상 계정에서 서브에이전트 상세까지 온전히 재현되도록 가져온다.)
    /// </summary>
    private static void CopyLinkedSubAgents(SessionEntry s, string destProjects)
    {
        try
        {
            string? srcDir = Path.GetDirectoryName(s.FilePath);
            if (srcDir is null) return;

            string sidecar = Path.Combine(srcDir, s.SessionId);
            if (Directory.Exists(sidecar))
                CopyDirectory(sidecar, Path.Combine(destProjects, s.SessionId));

            foreach (var agentFile in LinkedSubAgentFiles(srcDir, s.SessionId))
            {
                try
                {
                    string dest = Path.Combine(destProjects, Path.GetFileName(agentFile));
                    if (!File.Exists(dest)) File.Copy(agentFile, dest);
                }
                catch { /* 개별 서브에이전트 복사 실패는 무시(이어하기엔 필수 아님) */ }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// 세션 폴더(enc)에 평평하게 놓인 레거시 서브에이전트(agent-*.jsonl) 중 내부 sessionId 가
    /// <paramref name="sessionId"/> 인 파일들의 경로. 내보내기·복사에서 공용으로 쓴다.
    /// </summary>
    internal static IEnumerable<string> LinkedSubAgentFiles(string sessionDir, string sessionId)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(sessionDir, "agent-*.jsonl"); }
        catch { yield break; }

        foreach (var f in files)
        {
            string? sid = null;
            try { sid = ReadSessionId(f); }
            catch { /* 다음 파일 */ }
            if (sid == sessionId) yield return f;
        }
    }

    /// <summary>디렉터리 트리를 재귀 복사한다(대상에 이미 있는 파일은 보존).</summary>
    internal static void CopyDirectory(string src, string dest)
    {
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, file);
            string target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(file, target);
        }
    }

    /// <summary>트랜스크립트 앞부분에서 sessionId 필드를 읽는다(없으면 null).</summary>
    private static string? ReadSessionId(string file)
    {
        using var reader = new StreamReader(file);
        for (int i = 0; i < 5; i++)
        {
            string? line = reader.ReadLine();
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("sessionId", out var idEl) &&
                    idEl.ValueKind == JsonValueKind.String)
                {
                    return idEl.GetString();
                }
            }
            catch { /* 다음 줄 시도 */ }
        }
        return null;
    }

    /// <summary>
    /// 파일 앞부분만 훑어 (cwd, 미리보기, sidechain 여부, 앞쪽 세션이름)을 뽑는다.
    /// 미리보기는 summary 우선, 없으면 첫 사용자 메시지. 세션이름은 처음 만난 ai-title(폴백용).
    /// </summary>
    private static (string? Cwd, string? Preview, bool IsSidechain, string? FirstTitle) ScanHead(string file)
    {
        string? cwd = null;
        string? summary = null;
        string? firstUser = null;
        string? firstTitle = null;
        bool isSidechain = false;

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

            if (!isSidechain && root.TryGetProperty("isSidechain", out var scEl) && scEl.ValueKind == JsonValueKind.True)
                isSidechain = true;

            if (cwd is null && root.TryGetProperty("cwd", out var cwdEl) && cwdEl.ValueKind == JsonValueKind.String)
            {
                var v = cwdEl.GetString();
                if (!string.IsNullOrWhiteSpace(v)) cwd = v;
            }

            if (firstTitle is null && root.TryGetProperty("type", out var tt) &&
                tt.ValueKind == JsonValueKind.String && tt.GetString() == "ai-title" &&
                root.TryGetProperty("aiTitle", out var titEl) && titEl.ValueKind == JsonValueKind.String)
            {
                firstTitle = titEl.GetString();
            }

            if (summary is null && root.TryGetProperty("type", out var typeEl) &&
                typeEl.ValueKind == JsonValueKind.String && typeEl.GetString() == "summary" &&
                root.TryGetProperty("summary", out var sumEl) && sumEl.ValueKind == JsonValueKind.String)
            {
                summary = sumEl.GetString();
            }

            if (firstUser is null && root.TryGetProperty("type", out var t2) &&
                t2.ValueKind == JsonValueKind.String && t2.GetString() == "user" &&
                // 메타(caveat 등 하네스 주입) 메시지는 건너뛴다.
                !(root.TryGetProperty("isMeta", out var metaEl) && metaEl.ValueKind == JsonValueKind.True) &&
                root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
                msg.TryGetProperty("content", out var content))
            {
                var txt = ExtractText(content);
                // 슬래시 명령 래퍼(<command-name>…)·시스템 리마인더(<system-reminder>)·명령 출력
                // (<local-command-stdout>) 등 <태그>로 시작하는 합성 메시지는 미리보기로 부적합 → 건너뛴다.
                if (!IsSynthetic(txt)) firstUser = txt;
            }

            // cwd·미리보기(요약)·세션이름을 확보했으면 조기 종료.
            if (cwd is not null && summary is not null && firstTitle is not null) break;
        }

        string? preview = Clean(summary ?? firstUser);
        return (cwd, preview, isSidechain, firstTitle);
    }

    /// <summary>
    /// 파일 끝쪽 일부만 읽어 마지막 <c>ai-title</c>(현재 세션 이름)를 찾는다. 파일 크기와 무관하게
    /// 상수 시간에 가깝다. 꼬리 청크에 ai-title 이 없으면 null(호출부에서 앞쪽 값으로 폴백).
    /// </summary>
    internal static string? ReadLastAiTitle(string file)
    {
        const int chunk = 64 * 1024;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, fs.Length - chunk);
            fs.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            string text = reader.ReadToEnd();

            var lines = text.Split('\n');
            // 청크 중간에서 시작했으면 첫 줄은 잘렸을 수 있으니 뒤에서부터 훑되 첫(부분) 줄은 무시.
            int lower = start > 0 ? 1 : 0;
            for (int i = lines.Length - 1; i >= lower; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] != '{') continue;
                if (!line.Contains("\"ai-title\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("type", out var t) && t.GetString() == "ai-title" &&
                        root.TryGetProperty("aiTitle", out var tit) && tit.ValueKind == JsonValueKind.String)
                    {
                        return tit.GetString();
                    }
                }
                catch { /* 잘린/깨진 줄 — 계속 위로 */ }
            }
        }
        catch { /* best effort */ }
        return null;
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

    /// <summary>합성/주입 메시지(슬래시 명령 래퍼·시스템 리마인더·명령 출력 등 &lt;태그&gt;로 시작)인지.</summary>
    private static bool IsSynthetic(string? s) =>
        string.IsNullOrWhiteSpace(s) || s.TrimStart().StartsWith('<');

    /// <summary>미리보기 텍스트를 한 줄로 정리하고 길이를 제한한다.</summary>
    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = string.Join(' ', s.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        const int max = 140;
        return s.Length <= max ? s : s[..max] + "…";
    }
}
