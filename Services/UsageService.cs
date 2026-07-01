using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 세션 사용량. RemainingPercent = 100 - 5시간 창 사용률(%). ResetsAt = 5시간 창 리셋 시각(원본).
/// 주간(7일) 한도도 함께 담는다 — 주간이 소진되면 5시간 창이 100%라도 실제로는 사용할 수 없다.
/// </summary>
public record SessionUsage(
    double RemainingPercent,
    DateTimeOffset? ResetsAt,
    double? WeeklyRemainingPercent = null,
    DateTimeOffset? WeeklyResetsAt = null)
{
    /// <summary>주간 한도 소진(만료) 여부. 소진 시 표시상 0%로 취급한다.</summary>
    public bool WeeklyExhausted => WeeklyRemainingPercent is <= 0;

    /// <summary>화면 표시용 남은 비율: 주간이 소진됐으면 0%, 아니면 5시간 창 잔여.</summary>
    public double DisplayPercent => WeeklyExhausted ? 0 : RemainingPercent;

    /// <summary>화면 표시용 리셋 시각: 주간 소진 시 주간 리셋까지, 아니면 5시간 창 리셋까지.</summary>
    public DateTimeOffset? DisplayResetsAt => WeeklyExhausted ? WeeklyResetsAt : ResetsAt;
}

/// <summary>
/// 각 프로필의 Claude 세션 사용량을 oauth/usage 엔드포인트에서 조회한다.
/// 저장된 액세스 토큰이 만료됐으면 refresh_token으로 갱신한 뒤 자격증명 파일에 되돌려 저장한다.
/// (토큰 회전·저장 방식은 claude 자신이 하는 것과 동일하다. 갱신은 성공 시에만 기록한다.)
/// </summary>
public sealed class UsageService
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string TokenUrl = "https://console.anthropic.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string BetaHeader = "oauth-2025-04-20";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // 계정별 결과 캐시. usage는 5시간 창이라 자주 부를 필요가 없고,
    // 과도한 호출은 429(rate limit)를 유발하므로 TTL 동안 캐시한다.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, (SessionUsage? Usage, DateTime At)> _cache = new();
    private readonly object _cacheLock = new();

    /// <summary>
    /// 캐시를 적용해 세션 사용량을 조회한다. cacheKey는 보통 프로필 Id.
    /// force=true면 TTL을 무시하고 새로 조회한다(새로고침 버튼).
    /// 실패(429 등) 시엔 직전 캐시 값을 유지하고 재시도 시점을 늦춘다.
    /// </summary>
    public async Task<SessionUsage?> GetSessionUsageAsync(string credentialsPath, string cacheKey, bool force = false)
    {
        lock (_cacheLock)
        {
            if (!force && _cache.TryGetValue(cacheKey, out var c) && DateTime.UtcNow - c.At < CacheTtl)
                return c.Usage;
        }

        var usage = await FetchWithRefreshAsync(credentialsPath);

        lock (_cacheLock)
        {
            if (usage is not null)
            {
                _cache[cacheKey] = (usage, DateTime.UtcNow);
            }
            else if (_cache.TryGetValue(cacheKey, out var prev))
            {
                // 조회 실패: 마지막 값을 유지하되 At을 갱신해 잠시 재시도하지 않는다(백오프).
                _cache[cacheKey] = (prev.Usage, DateTime.UtcNow);
                return prev.Usage;
            }
        }
        return usage;
    }

    private async Task<SessionUsage?> FetchWithRefreshAsync(string credentialsPath)
    {
        try
        {
            if (!File.Exists(credentialsPath)) return null;

            var (token, refresh, expiresAt) = ReadTokens(credentialsPath);
            if (token is null) return null;

            // 만료(또는 임박)면 먼저 갱신
            bool nearExpiry = expiresAt is null || expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1);
            if (nearExpiry && refresh is not null)
            {
                var refreshed = await TryRefreshAsync(refresh);
                if (refreshed is { } r1)
                {
                    token = r1.Access;
                    WriteTokens(credentialsPath, r1.Access, r1.Refresh, r1.ExpiresAtMs);
                }
            }

            var (usage, unauthorized) = await FetchUsageAsync(token);
            if (usage is null && unauthorized && refresh is not null)
            {
                // 토큰이 (예상과 달리) 거부됨 → 한 번 더 갱신 재시도
                var refreshed = await TryRefreshAsync(refresh);
                if (refreshed is { } r2)
                {
                    WriteTokens(credentialsPath, r2.Access, r2.Refresh, r2.ExpiresAtMs);
                    (usage, _) = await FetchUsageAsync(r2.Access);
                }
            }
            return usage;
        }
        catch { return null; }
    }

    /// <summary>usage 엔드포인트 호출. (결과, 인증실패여부). 인증실패면 토큰 갱신이 필요하다는 신호.</summary>
    private static async Task<(SessionUsage? Usage, bool Unauthorized)> FetchUsageAsync(string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
            req.Headers.UserAgent.ParseAdd("claude-cli/2.0.0 (external, cli)");

            using var resp = await Http.SendAsync(req);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (null, true);
            if (!resp.IsSuccessStatusCode) return (null, false);

            return (Parse(await resp.Content.ReadAsStringAsync()), false);
        }
        catch { return (null, false); }
    }

    private static SessionUsage? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("five_hour", out var fh) || fh.ValueKind != JsonValueKind.Object)
                return null;

            var (remaining, reset) = ReadWindow(fh);

            // 주간(7일) 한도. 없으면 null(=미소진 취급).
            double? weeklyRemaining = null;
            DateTimeOffset? weeklyReset = null;
            if (root.TryGetProperty("seven_day", out var wk) && wk.ValueKind == JsonValueKind.Object)
                (weeklyRemaining, weeklyReset) = ReadWindow(wk);

            return new SessionUsage(remaining, reset, weeklyRemaining, weeklyReset);
        }
        catch { return null; }
    }

    /// <summary>usage 창 객체(five_hour/seven_day)에서 (남은 %, 리셋 시각)을 뽑는다.</summary>
    private static (double Remaining, DateTimeOffset? ResetsAt) ReadWindow(JsonElement win)
    {
        double util = win.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
            ? u.GetDouble() : 0;

        DateTimeOffset? reset = null;
        if (win.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(r.GetString(), out var dt))
        {
            reset = dt;
        }

        return (Math.Clamp(100 - util, 0, 100), reset);
    }

    private record RefreshedTokens(string Access, string Refresh, long ExpiresAtMs);

    private static async Task<RefreshedTokens?> TryRefreshAsync(string refreshToken)
    {
        try
        {
            string body = new JsonObject
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = ClientId,
            }.ToJsonString();

            using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            string? access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(access)) return null;

            // refresh_token은 회전될 수 있다(없으면 기존 것 유지).
            string refresh = root.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { } s && s.Length > 0
                ? s : refreshToken;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long expiresAtMs = root.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number
                ? nowMs + ei.GetInt64() * 1000
                : nowMs + 3600_000;

            return new RefreshedTokens(access, refresh, expiresAtMs);
        }
        catch { return null; }
    }

    private static (string? Token, string? Refresh, DateTimeOffset? ExpiresAt) ReadTokens(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o) || o.ValueKind != JsonValueKind.Object)
                return (null, null, null);

            string? t = o.TryGetProperty("accessToken", out var a) ? a.GetString() : null;
            string? r = o.TryGetProperty("refreshToken", out var rf) ? rf.GetString() : null;
            DateTimeOffset? exp = null;
            if (o.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number)
                exp = DateTimeOffset.FromUnixTimeMilliseconds(e.GetInt64());
            return (t, r, exp);
        }
        catch { return (null, null, null); }
    }

    /// <summary>구조를 유지한 채 claudeAiOauth 의 토큰 3개 필드만 갱신해 되돌려 쓴다.</summary>
    private static void WriteTokens(string path, string access, string refresh, long expiresAtMs)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return;
            if (root["claudeAiOauth"] is not JsonObject o) return;
            o["accessToken"] = access;
            o["refreshToken"] = refresh;
            o["expiresAt"] = expiresAtMs;
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort: 실패해도 화면 표시만 영향 */ }
    }
}
