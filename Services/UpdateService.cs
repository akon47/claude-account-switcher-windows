using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ClaudeAccountSwitcher.Services;

/// <summary>발견된 새 버전 정보.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl);

/// <summary>
/// GitHub Releases 기반 최소 자동 업데이트.
/// 최신 릴리스의 태그(vX.Y.Z)를 현재 어셈블리 버전과 비교하고, 새 버전이면 Setup.exe 자산을
/// 내려받아 인스톨러를 실행한다(인스톨러가 실행 중인 앱을 종료·교체·재실행).
/// </summary>
public static class UpdateService
{
    private const string Owner = "akon47";
    private const string Repo = "claude-account-switcher-windows";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub API 는 User-Agent 가 없으면 403 을 준다.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Claude-Account-Switcher");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>현재 실행 중인 빌드의 버전(Major.Minor.Build).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return Normalize(v);
        }
    }

    /// <summary>최신 릴리스를 조회한다. 새 버전이 없거나 조회 실패면 null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        // 초안/사전 릴리스는 건너뛴다.
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var ver = ParseTag(tag);
        if (ver is null || ver <= CurrentVersion) return null;

        // 설치 파일 자산 찾기: "...Setup_v0.2.0.exe"(현재) 또는 "...Setup.exe"(구버전) 모두 허용.
        if (!root.TryGetProperty("assets", out var assets)) return null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null
                || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var dl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (!string.IsNullOrEmpty(dl)) return new UpdateInfo(ver, tag!, dl!);
        }
        return null;
    }

    /// <summary>설치 파일을 임시 폴더로 내려받아 경로를 반환한다. progress 로 0~100 진행률을 보고한다.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Claude-Account-Switcher-Setup-{info.Tag}.exe");
        using var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long? total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total is > 0) progress?.Report(read * 100.0 / total.Value);
        }
        progress?.Report(100);
        return path;
    }

    /// <summary>내려받은 인스톨러를 실행한다. 호출 후 앱을 종료해 교체가 가능하게 해야 한다.</summary>
    public static void RunInstaller(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });

    /// <summary>"v0.2.0" / "0.2.0" → Version. 실패 시 null.</summary>
    private static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    /// <summary>Revision/미지정 성분을 0으로 맞춰 비교를 안정화한다(Major.Minor.Build).</summary>
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);
}
