using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeAccountSwitcher.Services;

/// <summary>oauthAccount 에서 뽑은 계정 식별 정보. Raw 는 전환 시 ~/.claude.json 에 그대로 써넣을 원본 JSON.</summary>
public sealed record OAuthAccountInfo(string Raw, string? Email, string? DisplayName, string? OrganizationName, string? PlanDisplayName);

/// <summary>
/// Claude Code 의 계정 정보(oauthAccount)를 다룬다.
/// - 기본 전역 설정은 ~/.claude.json
/// - CLAUDE_CONFIG_DIR 을 쓰면 그 폴더 안의 .claude.json
/// oauthAccount 에는 emailAddress / displayName / organizationName / planDisplayName 등 계정 식별 정보가 들어있다.
/// </summary>
public static class ClaudeConfig
{
    /// <summary>plain `claude` 가 사용하는 전역 설정 파일(~/.claude.json).</summary>
    public static string HomeConfigPath => Path.Combine(AppPaths.UserHome, ".claude.json");

    /// <summary>.claude.json(루트에 oauthAccount 키) 에서 계정 정보 추출.</summary>
    public static OAuthAccountInfo? FromClaudeJson(string claudeJsonPath)
    {
        try
        {
            if (!File.Exists(claudeJsonPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(claudeJsonPath));
            return doc.RootElement.TryGetProperty("oauthAccount", out var oa) && oa.ValueKind == JsonValueKind.Object
                ? Extract(oa) : null;
        }
        catch { return null; }
    }

    /// <summary>oauthAccount 객체만 담긴 파일(우리가 저장한 oauthAccount.json)에서 추출.</summary>
    public static OAuthAccountInfo? FromObjectFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object ? Extract(doc.RootElement) : null;
        }
        catch { return null; }
    }

    private static OAuthAccountInfo Extract(JsonElement oa) => new(
        oa.GetRawText(),
        Str(oa, "emailAddress"),
        Str(oa, "displayName"),
        Str(oa, "organizationName"),
        Str(oa, "planDisplayName"));

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// 주어진 .claude.json 에서 oauthAccount 키만 제거한다(제거 전 백업). 나머지 상태(온보딩·폴더 신뢰 등)는 보존.
    /// 다시 로그인 준비용 — oauthAccount 가 남아 있으면 claude 가 "이미 로그인된 계정"으로 보고
    /// 로그인 화면 없이 REPL 로 들어가 버린다(첫 대화에서야 인증 오류가 난다).
    /// </summary>
    public static void RemoveOAuthAccount(string claudeJsonPath)
    {
        try
        {
            if (!File.Exists(claudeJsonPath)) return;

            string text = File.ReadAllText(claudeJsonPath);
            if (JsonNode.Parse(text) is not JsonObject root || !root.ContainsKey("oauthAccount")) return;

            Backup(claudeJsonPath, "claude.json-relogin");
            root.Remove("oauthAccount");
            SavePreservingStyle(claudeJsonPath, root, text);
        }
        catch { /* best effort — 실패해도 로그인 실행 자체는 진행한다 */ }
    }

    /// <summary>~/.claude.json 의 oauthAccount 를 주어진 JSON 객체로 교체한다(교체 전 백업).</summary>
    public static void PatchHomeOAuthAccount(string rawOAuthAccountJson)
    {
        string path = HomeConfigPath;
        if (!File.Exists(path)) return;

        string text = File.ReadAllText(path);
        Backup(path, "claude.json");

        if (JsonNode.Parse(text)?.AsObject() is not { } root) return;
        root["oauthAccount"] = JsonNode.Parse(rawOAuthAccountJson);
        SavePreservingStyle(path, root, text);
    }

    private static void Backup(string path, string prefix)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            File.Copy(path, Path.Combine(AppPaths.BackupsDir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.bak"), overwrite: true);
        }
        catch { /* best effort */ }
    }

    /// <summary>원본이 들여쓰기 형식이면 최대한 유지해서 저장한다.</summary>
    private static void SavePreservingStyle(string path, JsonObject root, string original)
    {
        bool indented = original.Contains("\n  \"") || original.Contains("\n    \"");
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = indented }));
    }
}
