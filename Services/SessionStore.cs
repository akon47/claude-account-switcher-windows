using System.IO;
using System.Text.Json;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>대상 계정에 같은 세션의 '다르게 진행된' 사본이 있을 때 호출부에 넘기는 정보.</summary>
public sealed record SessionConflict(DateTime SourceModified, DateTime DestModified, long SourceBytes, long DestBytes);

/// <summary>
/// 세션 충돌(두 사본이 서로 다른 방향으로 진행됨)을 어떻게 풀지 결정한다.
/// true = 선택한(소스) 세션으로 교체(기존 사본은 백업), false = 대상의 기존 사본을 그대로 이어간다.
/// </summary>
public delegate bool SessionConflictResolver(SessionConflict conflict);

/// <summary>
/// 프로필별 대화 세션(claude 트랜스크립트)을 열거하고, 한 계정의 세션을 다른 계정으로
/// 복사해 <c>claude --resume</c> 로 이어하게 한다.
/// 세션 파일은 <c>&lt;configdir&gt;\projects\&lt;enc&gt;\&lt;id&gt;.jsonl</c> 형태이고, <c>&lt;enc&gt;</c> 는
/// cwd 로만 결정되므로(계정 무관) 복사 시 소스의 폴더명을 그대로 재사용한다.
/// </summary>
public sealed class SessionStore
{
    private const string ProjectsFolder = "projects";
    private const string SessionBackupsFolder = "sessions";
    private const int MaxScanLines = 120; // cwd/미리보기는 앞부분에 있음. 앞선 슬래시 명령·메타 메시지를 건너뛸 여유.
    private const int MaxSessionBackups = 30;

    /// <summary>두 트랜스크립트 사본의 관계.</summary>
    private enum TranscriptRelation
    {
        /// <summary>내용이 같다.</summary>
        Identical,

        /// <summary>대상 사본 이후로 소스에서만 대화가 이어졌다(대상이 소스의 앞부분).</summary>
        SourceExtendsDest,

        /// <summary>소스 이후로 대상에서만 대화가 이어졌다(소스가 대상의 앞부분).</summary>
        DestExtendsSource,

        /// <summary>갈라진 뒤 양쪽 모두 진행됐다(어느 쪽도 다른 쪽의 앞부분이 아님).</summary>
        Diverged,
    }

    /// <summary>
    /// 프로필의 세션 목록(최근 수정 순). 활성 프로필이면 실제 라이브 저장소(~/.claude)도 함께 훑는다
    /// (활성 계정은 전환 시 ~/.claude 를 쓰고, 동시 실행 때만 프로필 폴더에 쌓기 때문).
    /// 같은 세션이 두 곳에 있으면 더 최근 것을 택한다.
    /// 세션 자동 유지가 보낸 headless 한마디(sdk 진입점 + KeepAlivePrompt)는 대화가 아니므로 숨긴다.
    /// </summary>
    public IReadOnlyList<SessionEntry> ListForProfile(Profile p, bool isActive) => ListForProfile(p, isActive, int.MaxValue);

    /// <summary>
    /// 최근 세션 <paramref name="max"/>개만 빠르게. 파일을 수정 시각 순으로 정렬해 앞에서부터 필요한 만큼만
    /// 파싱하므로 세션이 수백 개여도 메뉴를 바로 띄울 수 있다(행 우클릭 "최근 세션 이어하기").
    /// </summary>
    public IReadOnlyList<SessionEntry> ListRecentForProfile(Profile p, bool isActive, int max) => ListForProfile(p, isActive, max);

    private IReadOnlyList<SessionEntry> ListForProfile(Profile p, bool isActive, int max)
    {
        List<string> dirs = [p.ConfigDir];
        if (isActive) dirs.Add(AppPaths.ClaudeHome);

        // 두 저장소의 후보 파일을 최근 수정 순으로 한 줄로 세운다. 같은 id 는 가장 최근 것만 남는다.
        List<(string File, DateTime Modified)> candidates = [];
        foreach (string configDir in dirs)
        {
            string projects = Path.Combine(configDir, ProjectsFolder);
            if (!Directory.Exists(projects)) continue;
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(projects, "*.jsonl", SearchOption.AllDirectories)
                    // 서브에이전트(sidechain) 트랜스크립트는 이어하기 대상이 아니다(대량으로 생기고 영어
                    // 에이전트 프롬프트가 첫 메시지) → 제외. Claude Code 는 이를 agent-*.jsonl 로 만든다(빠른 필터).
                    .Where(f => !Path.GetFileName(f).StartsWith("agent-", StringComparison.OrdinalIgnoreCase))
                    .Select(f => (f, File.GetLastWriteTime(f))));
            }
            catch { /* 폴더 접근 실패 — 다른 저장소는 계속 */ }
        }
        candidates.Sort((a, b) => b.Modified.CompareTo(a.Modified));

        List<SessionEntry> result = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, modified) in candidates)
        {
            if (result.Count >= max) break;
            string id = Path.GetFileNameWithoutExtension(file);
            if (!seen.Add(id)) continue; // 프로필 폴더와 ~/.claude 양쪽에 있으면 더 최근 쪽(앞선 것)만

            try
            {
                if (BuildEntry(p, file, id, modified) is { } entry) result.Add(entry);
            }
            catch { /* 한 파일이 깨져도 나머지는 계속 */ }
        }
        return result;
    }

    /// <summary>트랜스크립트 한 파일을 목록 항목으로. 대화가 아닌 파일(sidechain·세션유지 한마디)이면 null.</summary>
    private static SessionEntry? BuildEntry(Profile p, string file, string id, DateTime modified)
    {
        var head = ScanHead(file);
        if (head.IsSidechain) return null; // agent- 규칙을 벗어난 sidechain 까지 방어
        if (head.IsKeepAlive) return null; // 세션 자동 유지의 headless 한마디 → 대화 아님

        // 세션 이름(ai-title)은 대화가 진행되며 갱신되므로 파일 끝쪽의 마지막 값이 현재 이름.
        // 앞부분(FirstTitle)은 꼬리를 못 읽었을 때의 폴백.
        string? name = ReadLastAiTitle(file) ?? head.FirstTitle;

        return new SessionEntry
        {
            SessionId = id,
            ProjectFolder = Path.GetFileName(Path.GetDirectoryName(file)!),
            Cwd = head.Cwd ?? "",
            FilePath = file,
            ProfileId = p.Id,
            ProfileName = p.Name,
            SourceEmail = string.IsNullOrEmpty(p.Email) ? null : p.Email,
            LastModified = modified,
            Preview = head.Preview,
            Name = Clean(name),
        };
    }

    /// <summary>
    /// 세션 파일을 대상 프로필의 격리 설정 폴더로 복사한다(projects\&lt;enc&gt; 경로). 실행할 세션 id 를 반환.
    /// <para>
    /// 대상에 같은 세션이 이미 있으면 두 사본을 비교해 정한다(계정을 오가며 이어한 뒤 되돌아오는 경우):
    /// - 같으면 그대로.
    /// - 대상 사본 이후로 소스에서만 대화가 이어졌으면(대상이 소스의 앞부분) 조용히 소스로 교체
    ///   — "1번→2번으로 이어하고 2번에서 더 진행한 뒤 다시 1번으로" 가 최신 상태로 열리게.
    /// - 대상이 이미 더 진행됐으면(소스가 대상의 앞부분) 대상 사본을 그대로 이어간다.
    /// - 갈라졌으면 <paramref name="onConflict"/> 에 묻는다(없으면 더 최근에 수정된 쪽).
    /// 교체할 때 기존 사본은 backups\sessions\ 에 백업한다.
    /// </para>
    /// <paramref name="overrideProjectFolder"/> 를 주면 소스 폴더명 대신 그걸 대상 enc 폴더로 쓴다
    /// (다른 PC 로 옮겨 작업 폴더가 바뀌었을 때 cwd 로 다시 인코딩한 폴더명 전달).
    /// </summary>
    public string ImportInto(SessionEntry s, Profile dest, string? overrideProjectFolder = null, SessionConflictResolver? onConflict = null)
    {
        string projectFolder = string.IsNullOrEmpty(overrideProjectFolder) ? s.ProjectFolder : overrideProjectFolder;
        string destProjects = Path.Combine(dest.ConfigDir, ProjectsFolder, projectFolder);
        Directory.CreateDirectory(destProjects);
        string destFile = Path.Combine(destProjects, s.SessionId + ".jsonl");

        bool samePath = string.Equals(Path.GetFullPath(s.FilePath), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase);
        bool replaced = false;

        if (samePath)
        {
            // 자기 자신으로 이어하기 — 손댈 것 없음.
        }
        else if (!File.Exists(destFile))
        {
            File.Copy(s.FilePath, destFile);
        }
        else
        {
            bool replace = CompareTranscripts(s.FilePath, destFile) switch
            {
                TranscriptRelation.Identical => false,
                TranscriptRelation.SourceExtendsDest => true,
                TranscriptRelation.DestExtendsSource => false,
                _ => onConflict?.Invoke(new SessionConflict(
                        File.GetLastWriteTime(s.FilePath), File.GetLastWriteTime(destFile),
                        new FileInfo(s.FilePath).Length, new FileInfo(destFile).Length))
                     ?? File.GetLastWriteTimeUtc(s.FilePath) > File.GetLastWriteTimeUtc(destFile),
            };

            if (replace)
            {
                BackupTranscript(destFile, s.SessionId);
                File.Copy(s.FilePath, destFile, overwrite: true);
                replaced = true;
            }
        }

        CopyLinkedSubAgents(s, destProjects, refresh: replaced);
        return s.SessionId;
    }

    /// <summary>
    /// 두 트랜스크립트의 관계를 판정한다. 트랜스크립트는 추가 전용(append-only)에 가깝기 때문에
    /// 한쪽이 다른 쪽의 앞부분(prefix)이면 그 뒤로 한쪽에서만 대화가 이어진 것이다.
    /// </summary>
    private static TranscriptRelation CompareTranscripts(string source, string dest)
    {
        long ls = new FileInfo(source).Length;
        long ld = new FileInfo(dest).Length;
        if (!PrefixEquals(source, dest, Math.Min(ls, ld))) return TranscriptRelation.Diverged;
        if (ls == ld) return TranscriptRelation.Identical;
        return ls > ld ? TranscriptRelation.SourceExtendsDest : TranscriptRelation.DestExtendsSource;
    }

    /// <summary>두 파일의 앞 <paramref name="length"/> 바이트가 같은지(청크 비교).</summary>
    private static bool PrefixEquals(string a, string b, long length)
    {
        if (length <= 0) return true;
        const int chunk = 64 * 1024;
        byte[] ba = new byte[chunk];
        byte[] bb = new byte[chunk];
        using FileStream fa = new(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using FileStream fb = new(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long remaining = length;
        while (remaining > 0)
        {
            int want = (int)Math.Min(chunk, remaining);
            int ra = fa.ReadAtLeast(ba.AsSpan(0, want), want, throwOnEndOfStream: false);
            int rb = fb.ReadAtLeast(bb.AsSpan(0, want), want, throwOnEndOfStream: false);
            if (ra != rb || ra == 0) return false;
            if (!ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb))) return false;
            remaining -= ra;
        }
        return true;
    }

    /// <summary>교체 전 대상 사본을 backups\sessions\ 에 보관한다(오래된 것부터 정리). projects 밖이라 claude 는 보지 못한다.</summary>
    private static void BackupTranscript(string file, string sessionId)
    {
        try
        {
            string dir = Path.Combine(AppPaths.BackupsDir, SessionBackupsFolder);
            Directory.CreateDirectory(dir);
            File.Copy(file, Path.Combine(dir, $"{sessionId}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl"), overwrite: true);

            foreach (var f in new DirectoryInfo(dir).GetFiles("*.jsonl")
                         .OrderByDescending(f => f.CreationTimeUtc)
                         .Skip(MaxSessionBackups))
            {
                f.Delete();
            }
        }
        catch { /* best effort — 백업 실패가 이어하기를 막지는 않는다 */ }
    }

    /// <summary>
    /// 이 세션에 연결된 서브에이전트(sidechain) 트랜스크립트도 대상 폴더로 함께 복사한다.
    /// 두 구조를 모두 다룬다:
    /// (신) 세션별 사이드카 폴더 <c>&lt;enc&gt;\&lt;id&gt;\</c>(subagents\agent-*.jsonl 등)를 통째로 복사.
    /// (구) <c>&lt;enc&gt;</c> 에 평평하게 놓인 agent-*.jsonl 중 내부 sessionId 가 이 세션인 것.
    /// (이어하기 자체엔 없어도 되지만, 대상 계정에서 서브에이전트 상세까지 온전히 재현되도록 가져온다.)
    /// <paramref name="refresh"/> 면 본문을 교체한 경우라, 대상에 이미 있어도 소스가 더 새로우면 덮어쓴다.
    /// </summary>
    private static void CopyLinkedSubAgents(SessionEntry s, string destProjects, bool refresh)
    {
        try
        {
            if (Path.GetDirectoryName(s.FilePath) is not { } srcDir) return;

            string sidecar = Path.Combine(srcDir, s.SessionId);
            if (Directory.Exists(sidecar))
                CopyDirectory(sidecar, Path.Combine(destProjects, s.SessionId), overwriteOlder: refresh);

            foreach (string agentFile in LinkedSubAgentFiles(srcDir, s.SessionId))
            {
                try { CopyFile(agentFile, Path.Combine(destProjects, Path.GetFileName(agentFile)), overwriteOlder: refresh); }
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

        foreach (string f in files)
        {
            string? sid = null;
            try { sid = ReadSessionId(f); }
            catch { /* 다음 파일 */ }
            if (sid == sessionId) yield return f;
        }
    }

    /// <summary>
    /// 디렉터리 트리를 재귀 복사한다. 기본은 대상에 이미 있는 파일 보존;
    /// <paramref name="overwriteOlder"/> 면 소스가 더 새로운 파일은 덮어쓴다.
    /// </summary>
    internal static void CopyDirectory(string src, string dest, bool overwriteOlder = false)
    {
        foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(dest, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            CopyFile(file, target, overwriteOlder);
        }
    }

    private static void CopyFile(string src, string dest, bool overwriteOlder)
    {
        if (!File.Exists(dest)) { File.Copy(src, dest); return; }
        if (overwriteOlder && File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dest))
            File.Copy(src, dest, overwrite: true);
    }

    /// <summary>트랜스크립트 앞부분에서 sessionId 필드를 읽는다(없으면 null).</summary>
    private static string? ReadSessionId(string file)
    {
        using StreamReader reader = new(file);
        for (int i = 0; i < 5; i++)
        {
            string? line = reader.ReadLine();
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && Str(doc.RootElement, "sessionId") is { } id) return id;
            }
            catch { /* 다음 줄 시도 */ }
        }
        return null;
    }

    /// <summary>파일 앞부분을 훑어 얻은 메타.</summary>
    private sealed record HeadInfo(string? Cwd, string? Preview, bool IsSidechain, string? FirstTitle, bool IsKeepAlive);

    /// <summary>
    /// 파일 앞부분만 훑어 (cwd, 미리보기, sidechain 여부, 앞쪽 세션이름, 세션유지 한마디 여부)를 뽑는다.
    /// 미리보기는 summary 우선, 없으면 첫 사용자 메시지. 세션이름은 처음 만난 ai-title(폴백용).
    /// </summary>
    private static HeadInfo ScanHead(string file)
    {
        string? cwd = null;
        string? summary = null;
        string? firstUser = null;
        string? firstTitle = null;
        bool isSidechain = false;
        bool isKeepAlive = false;

        using StreamReader reader = new(file);
        for (int i = 0; i < MaxScanLines; i++)
        {
            string? line = reader.ReadLine();
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement root;
            try { using var doc = JsonDocument.Parse(line); root = doc.RootElement.Clone(); }
            catch { continue; }

            if (root.ValueKind != JsonValueKind.Object) continue;

            isSidechain |= root.TryGetProperty("isSidechain", out var scEl) && scEl.ValueKind == JsonValueKind.True;
            cwd ??= Str(root, "cwd") is { Length: > 0 } c ? c : null;

            string? type = Str(root, "type");
            if (firstTitle is null && type == "ai-title") firstTitle = Str(root, "aiTitle");
            if (summary is null && type == "summary") summary = Str(root, "summary");

            if (firstUser is null && type == "user"
                // 메타(caveat 등 하네스 주입) 메시지는 건너뛴다.
                && !(root.TryGetProperty("isMeta", out var metaEl) && metaEl.ValueKind == JsonValueKind.True)
                && root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object
                && msg.TryGetProperty("content", out var content))
            {
                string? txt = ExtractText(content);
                // 슬래시 명령 래퍼(<command-name>…)·시스템 리마인더(<system-reminder>)·명령 출력
                // (<local-command-stdout>) 등 <태그>로 시작하는 합성 메시지는 미리보기로 부적합 → 건너뛴다.
                if (!IsSynthetic(txt))
                {
                    firstUser = txt;
                    // 세션 자동 유지가 headless(`claude -p`)로 보낸 한마디: sdk 진입점 + 고정 프롬프트.
                    isKeepAlive = IsSdkEntry(root)
                        && string.Equals(txt!.Trim(), Launcher.KeepAlivePrompt, StringComparison.OrdinalIgnoreCase);
                }
            }

            // cwd·미리보기(요약)·세션이름을 확보했으면 조기 종료.
            if (cwd is not null && summary is not null && firstTitle is not null) break;
        }

        return new HeadInfo(cwd, Clean(summary ?? firstUser), isSidechain, firstTitle, isKeepAlive);
    }

    /// <summary>사용자 메시지가 대화형 REPL 이 아니라 sdk/headless(`claude -p`)로 들어왔는지.</summary>
    private static bool IsSdkEntry(JsonElement root) =>
        Str(root, "entrypoint")?.StartsWith("sdk", StringComparison.OrdinalIgnoreCase) == true
        || Str(root, "promptSource") == "sdk";

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// 파일 끝쪽 일부만 읽어 마지막 <c>ai-title</c>(현재 세션 이름)를 찾는다. 파일 크기와 무관하게
    /// 상수 시간에 가깝다. 꼬리 청크에 ai-title 이 없으면 null(호출부에서 앞쪽 값으로 폴백).
    /// </summary>
    internal static string? ReadLastAiTitle(string file)
    {
        const int chunk = 64 * 1024;
        try
        {
            using FileStream fs = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, fs.Length - chunk);
            fs.Seek(start, SeekOrigin.Begin);
            using StreamReader reader = new(fs);
            string[] lines = reader.ReadToEnd().Split('\n');

            // 청크 중간에서 시작했으면 첫 줄은 잘렸을 수 있으니 뒤에서부터 훑되 첫(부분) 줄은 무시.
            int lower = start > 0 ? 1 : 0;
            for (int i = lines.Length - 1; i >= lower; i--)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] != '{') continue;
                if (!line.Contains("\"ai-title\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && Str(root, "type") == "ai-title" && Str(root, "aiTitle") is { } title)
                        return title;
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
        if (content.ValueKind != JsonValueKind.Array) return null;

        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object && Str(part, "type") == "text" && Str(part, "text") is { } text)
                return text;
        }
        return null;
    }

    /// <summary>합성/주입 메시지(슬래시 명령 래퍼·시스템 리마인더·명령 출력 등 &lt;태그&gt;로 시작)인지.</summary>
    private static bool IsSynthetic(string? s) => string.IsNullOrWhiteSpace(s) || s.TrimStart().StartsWith('<');

    /// <summary>미리보기 텍스트를 한 줄로 정리하고 길이를 제한한다.</summary>
    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = string.Join(' ', s.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        const int max = 140;
        return s.Length <= max ? s : s[..max] + "…";
    }
}
