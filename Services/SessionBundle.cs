using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 세션을 단일 파일(<c>.claudesession</c>, 실체는 zip)로 내보내고 다시 읽는다. 다른 PC 로 옮겨
/// 이 PC 의 계정으로 이어하기(resume) 위한 이식용 번들. 선택 시 각 세션의 작업 폴더(cwd 내용)도 함께 담는다.
/// <para>zip 구조는 실제 저장소(projects\&lt;enc&gt;\)를 그대로 미러링한다:</para>
/// <code>
/// manifest.json
/// projects\&lt;enc&gt;\&lt;id&gt;.jsonl            (본문 트랜스크립트)
/// projects\&lt;enc&gt;\&lt;id&gt;\...              (세션 사이드카: subagents 등)
/// projects\&lt;enc&gt;\agent-*.jsonl        (레거시 평면 서브에이전트)
/// workdirs\&lt;enc&gt;\...                    (작업 폴더 내용, 포함을 택했을 때. enc 로 cwd 와 1:1)
/// </code>
/// 이렇게 두면 읽을 때 임시폴더에 그대로 풀어 <see cref="SessionStore.ImportInto"/> 가
/// 온디스크 세션과 똑같이 다룰 수 있다.
/// </summary>
public static class SessionBundle
{
    public const string FileExtension = ".claudesession";
    private const string ManifestName = "manifest.json";
    private const string FormatId = "claude-account-switcher/session-bundle";
    private const string ProjectsRoot = "projects";
    private const string WorkdirsRoot = "workdirs";
    private const int CurrentVersion = 1;

    /// <summary>
    /// 작업 폴더 포함 시 제외하는, 무겁고 재생성 가능한 디렉터리 이름(대소문자 무시, 모든 깊이).
    /// 소스일 수 있는 애매한 이름(packages=pnpm 워크스페이스 소스, Debug/Release=일반 폴더명 등)은 넣지 않는다.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", "node_modules", "bin", "obj", "dist", "build", "out", "target",
        ".venv", "venv", "__pycache__", ".pytest_cache", ".mypy_cache", ".tox",
        ".next", ".nuxt", ".svelte-kit", ".turbo", ".parcel-cache", ".cache",
        ".gradle", ".idea", ".vs", ".vscode", "coverage", ".nyc_output",
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>
    /// 내보낼 세션들의 작업 폴더(cwd) 중 이 PC 에 실제로 존재하는 것들의 (폴더 수, 포함될 총 바이트).
    /// 제외 목록을 적용해 계산하므로 실제 담길 크기에 가깝다. 포함 여부 확인창에 표시할 값.
    /// </summary>
    public static (int Folders, long Bytes) ComputeWorkdirInfo(IEnumerable<SessionEntry> sessions)
    {
        long bytes = 0;
        int folders = 0;
        foreach (var cwd in DistinctExistingCwds(sessions))
        {
            folders++;
            foreach (var f in EnumerateIncludedFiles(cwd))
            {
                try { bytes += new FileInfo(f).Length; }
                catch { /* 접근 불가 파일은 건너뜀 */ }
            }
        }
        return (folders, bytes);
    }

    /// <summary>
    /// 선택한 세션들을 <paramref name="destPath"/> 번들 파일로 내보낸다(본문 + 사이드카/서브에이전트 + 메타,
    /// <paramref name="includeWorkdirs"/> 면 작업 폴더 내용도). 쓰다가 실패/취소하면 반쪽 파일이 남지 않도록
    /// 임시 파일에 쓴 뒤 옮긴다. 담은 세션 수를 반환.
    /// </summary>
    public static int Export(
        IReadOnlyList<SessionEntry> sessions,
        string destPath,
        string? appVersion,
        bool includeWorkdirs,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = new Manifest
        {
            Format = FormatId,
            Version = CurrentVersion,
            ExportedAt = DateTime.Now.ToString("o"),
            App = appVersion,
            Sessions = new List<ManifestSession>(),
            Workdirs = new List<ManifestWorkdir>(),
        };

        // 1) 담을 파일 목록을 먼저 만든다((원본경로, zip 엔트리명, 크기)) → 진행률 총량 계산.
        var files = new List<(string Source, string Entry, long Size)>();
        var addedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Plan(string source, string entry)
        {
            if (!File.Exists(source) || !addedEntries.Add(entry)) return;
            long size = 0;
            try { size = new FileInfo(source).Length; } catch { }
            files.Add((source, entry, size));
        }

        int count = 0;
        foreach (var s in sessions)
        {
            if (!File.Exists(s.FilePath)) continue;
            string encDir = $"{ProjectsRoot}/{s.ProjectFolder}";

            Plan(s.FilePath, $"{encDir}/{s.SessionId}.jsonl");

            string? srcDir = Path.GetDirectoryName(s.FilePath);
            if (srcDir is not null)
            {
                string sidecar = Path.Combine(srcDir, s.SessionId);
                if (Directory.Exists(sidecar))
                {
                    foreach (var f in Directory.EnumerateFiles(sidecar, "*", SearchOption.AllDirectories))
                        Plan(f, $"{encDir}/{s.SessionId}/{Rel(sidecar, f)}");
                }

                foreach (var agent in SessionStore.LinkedSubAgentFiles(srcDir, s.SessionId))
                    Plan(agent, $"{encDir}/{Path.GetFileName(agent)}");
            }

            manifest.Sessions.Add(new ManifestSession
            {
                Id = s.SessionId,
                ProjectFolder = s.ProjectFolder,
                Cwd = s.Cwd,
                Name = s.Name,
                Preview = s.Preview,
                LastModified = s.LastModified.ToString("o"),
                SourceProfileName = s.ProfileName,
                SourceProfileEmail = s.SourceEmail,
            });
            count++;
        }

        // 2) 작업 폴더(cwd) 내용 — 제외 목록 적용, enc 로 dedupe.
        if (includeWorkdirs)
        {
            var doneFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sessions)
            {
                if (string.IsNullOrEmpty(s.Cwd) || !Directory.Exists(s.Cwd)) continue;
                if (!doneFolders.Add(s.ProjectFolder)) continue;

                foreach (var f in EnumerateIncludedFiles(s.Cwd))
                    Plan(f, $"{WorkdirsRoot}/{s.ProjectFolder}/{Rel(s.Cwd, f)}");

                manifest.Workdirs!.Add(new ManifestWorkdir
                {
                    ProjectFolder = s.ProjectFolder,
                    Cwd = s.Cwd,
                    FolderName = FolderNameOf(s.Cwd),
                });
            }
        }

        long total = Math.Max(1, files.Sum(f => f.Size));
        long written = 0;

        string tmp = destPath + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var (source, entry, size) in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try { zip.CreateEntryFromFile(source, entry, CompressionLevel.Optimal); }
                    catch { /* 개별 파일 실패는 무시(잠김 등) */ }
                    written += size;
                    progress?.Report(Math.Min(99, written * 100.0 / total));
                }

                var manifestEntry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using var ms = manifestEntry.Open();
                JsonSerializer.Serialize(ms, manifest, JsonOpts);
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmp, destPath);
            progress?.Report(100);
            return count;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>
    /// 번들 파일을 임시 폴더에 풀고 안의 세션 목록을 <see cref="SessionEntry"/> 로 돌려준다.
    /// 각 항목의 <see cref="SessionEntry.FilePath"/> 는 풀린 본문 jsonl 을, 작업 폴더가 함께 담겼으면
    /// <see cref="SessionEntry.BundleWorkdirPath"/> 는 풀린 작업 폴더 루트를 가리킨다. 형식이 아니면 예외.
    /// </summary>
    public static IReadOnlyList<SessionEntry> Read(string bundlePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(bundlePath);

        var mEntry = zip.GetEntry(ManifestName)
            ?? throw new InvalidDataException("Not a valid session bundle (manifest.json missing).");

        Manifest? manifest;
        using (var ms = mEntry.Open())
            manifest = JsonSerializer.Deserialize<Manifest>(ms, JsonOpts);

        if (manifest?.Format != FormatId)
            throw new InvalidDataException("Not a valid session bundle (format mismatch).");

        string extractRoot = Path.Combine(
            Path.GetTempPath(), "ClaudeAccountSwitcher", "import", Path.GetRandomFileName());
        Directory.CreateDirectory(extractRoot);
        ExtractSafely(zip, extractRoot, progress, ct);

        // 작업 폴더가 실제로 풀린 projectFolder 집합.
        var workdirFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in manifest.Workdirs ?? new List<ManifestWorkdir>())
        {
            if (string.IsNullOrEmpty(w.ProjectFolder)) continue;
            string dir = Path.Combine(extractRoot, WorkdirsRoot, w.ProjectFolder);
            if (Directory.Exists(dir)) workdirFolders.Add(w.ProjectFolder);
        }

        var result = new List<SessionEntry>();
        foreach (var ms in manifest.Sessions ?? new List<ManifestSession>())
        {
            if (string.IsNullOrEmpty(ms.Id) || string.IsNullOrEmpty(ms.ProjectFolder)) continue;

            string file = Path.Combine(extractRoot, ProjectsRoot, ms.ProjectFolder, ms.Id + ".jsonl");
            if (!File.Exists(file)) continue;

            string? workdir = workdirFolders.Contains(ms.ProjectFolder)
                ? Path.Combine(extractRoot, WorkdirsRoot, ms.ProjectFolder)
                : null;

            result.Add(new SessionEntry
            {
                SessionId = ms.Id,
                ProjectFolder = ms.ProjectFolder,
                Cwd = ms.Cwd ?? "",
                FilePath = file,
                ProfileId = "",                       // 번들 출처(다른 PC 계정) → 로컬 계정과 매칭 안 됨
                ProfileName = string.IsNullOrEmpty(ms.SourceProfileName) ? "?" : ms.SourceProfileName!,
                SourceEmail = ms.SourceProfileEmail,
                LastModified = ParseDate(ms.LastModified),
                Preview = ms.Preview,
                Name = ms.Name,
                BundleWorkdirPath = workdir,
            });
        }

        return result;
    }

    /// <summary>번들에서 풀린 작업 폴더를 대상 폴더로 복사한다(이미 있는 파일은 보존). 진행률 보고.</summary>
    public static void RestoreWorkdir(string sourceRoot, string destFolder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var all = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToList();
        long total = Math.Max(1, all.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } }));
        long done = 0;
        foreach (var f in all)
        {
            ct.ThrowIfCancellationRequested();
            string target = Path.Combine(destFolder, Rel(sourceRoot, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
            {
                try { File.Copy(f, target); } catch { /* 개별 파일 실패는 무시 */ }
            }
            try { done += new FileInfo(f).Length; } catch { }
            progress?.Report(Math.Min(100, done * 100.0 / total));
        }
    }

    /// <summary>루트 아래 파일을 훑되, 제외 목록에 든 이름의 디렉터리는 통째로 건너뛴다(모든 깊이).</summary>
    private static IEnumerable<string> EnumerateIncludedFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { subdirs = Array.Empty<string>(); }
            foreach (var sub in subdirs)
            {
                string name = Path.GetFileName(sub);
                if (ExcludedDirNames.Contains(name)) continue;
                stack.Push(sub);
            }

            string[] filesInDir;
            try { filesInDir = Directory.GetFiles(dir); }
            catch { filesInDir = Array.Empty<string>(); }
            foreach (var f in filesInDir) yield return f;
        }
    }

    private static IEnumerable<string> DistinctExistingCwds(IEnumerable<SessionEntry> sessions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sessions)
        {
            if (string.IsNullOrEmpty(s.Cwd) || !Directory.Exists(s.Cwd)) continue;
            if (seen.Add(s.ProjectFolder)) yield return s.Cwd;
        }
    }

    private static string Rel(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string FolderNameOf(string cwd)
    {
        string n = Path.GetFileName(cwd.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(n) ? "project" : n;
    }

    /// <summary>zip 을 대상 폴더로 푼다. 엔트리 경로가 대상 밖으로 벗어나면(zip slip) 건너뛴다.</summary>
    private static void ExtractSafely(ZipArchive zip, string destRoot, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string rootFull = Path.GetFullPath(destRoot + Path.DirectorySeparatorChar);
        long total = Math.Max(1, zip.Entries.Sum(e => e.Length));
        long done = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue; // 디렉터리 엔트리
            string target = Path.GetFullPath(Path.Combine(destRoot, entry.FullName));
            if (target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            done += entry.Length;
            progress?.Report(Math.Min(100, done * 100.0 / total));
        }
    }

    private static DateTime ParseDate(string? iso) =>
        DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt : DateTime.MinValue;

    private sealed class Manifest
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public string? ExportedAt { get; set; }
        public string? App { get; set; }
        public List<ManifestSession>? Sessions { get; set; }
        public List<ManifestWorkdir>? Workdirs { get; set; }
    }

    private sealed class ManifestSession
    {
        public string? Id { get; set; }
        public string? ProjectFolder { get; set; }
        public string? Cwd { get; set; }
        public string? Name { get; set; }
        public string? Preview { get; set; }
        public string? LastModified { get; set; }
        public string? SourceProfileName { get; set; }
        public string? SourceProfileEmail { get; set; }
    }

    private sealed class ManifestWorkdir
    {
        public string? ProjectFolder { get; set; }
        public string? Cwd { get; set; }
        public string? FolderName { get; set; }
    }
}
