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

/// <summary>사용량 조회 결과의 상태.</summary>
public enum UsageState
{
    /// <summary>정상 조회.</summary>
    Ok,

    /// <summary>저장된 자격증명이 더 이상 유효하지 않다(리프레시 토큰 거부/토큰 없음) → 다시 로그인 필요.</summary>
    Unauthorized,

    /// <summary>일시적 실패(오프라인·429·서버 오류·무료 플랜 등). 자격증명 문제라고 단정할 수 없다.</summary>
    Unavailable,
}

/// <summary>사용량 조회 결과. Usage 는 State == Ok 일 때만 채워진다.</summary>
public record UsageResult(SessionUsage? Usage, UsageState State)
{
    public static readonly UsageResult Unauthorized = new(null, UsageState.Unauthorized);
    public static readonly UsageResult Unavailable = new(null, UsageState.Unavailable);

    /// <summary>이 계정은 다시 로그인해야 한다(토큰 만료·폐기).</summary>
    public bool NeedsRelogin => State == UsageState.Unauthorized;
}

/// <summary>
/// 각 프로필의 Claude 세션 사용량을 oauth/usage 엔드포인트에서 조회한다.
/// 저장된 액세스 토큰이 만료됐으면 refresh_token으로 갱신한 뒤 자격증명 파일에 되돌려 저장한다.
/// (토큰 회전·저장 방식은 claude 자신이 하는 것과 동일하다. 갱신은 성공 시에만 기록한다.)
/// 갱신 자체가 거부되면(=재로그인 필요) Unauthorized 로 알려 목록이 "다시 로그인 필요"를 보여줄 수 있게 한다.
/// </summary>
public sealed class UsageService
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    // 토큰 갱신 엔드포인트. console.anthropic.com/v1/oauth/token 은 404(이전됨) —
    // 잘못된 주소로 보내면 갱신이 조용히 실패해 토큰이 그대로 만료된다.
    private const string TokenUrl = "https://api.anthropic.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string BetaHeader = "oauth-2025-04-20";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // 계정별 결과 캐시. usage는 5시간 창이라 자주 부를 필요가 없고,
    // 과도한 호출은 429(rate limit)를 유발하므로 TTL 동안 캐시한다.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, (UsageResult Result, DateTime At, long Stamp)> _cache = new();
    private readonly object _cacheLock = new();

    /// <summary>
    /// 캐시를 적용해 세션 사용량을 조회한다. cacheKey는 보통 프로필 Id.
    /// credentialsPaths 는 이 계정의 자격증명 후보(우선순위 순) — 활성 프로필이면 ~/.claude 의 라이브
    /// 토큰과 프로필 보관본이 모두 후보가 된다. 하나라도 살아 있으면 그 계정은 쓸 수 있는 것이다.
    /// force=true면 TTL을 무시하고 새로 조회한다(새로고침 버튼).
    /// 자격증명 파일이 바뀌면(재로그인·토큰 회전) TTL 이 남아 있어도 캐시를 버린다 —
    /// 로그인 직후까지 "다시 로그인 필요"가 남아 보이지 않게 한다.
    /// 일시적 실패(429 등) 시엔 직전 정상값을 유지하고 재시도 시점을 늦춘다.
    /// </summary>
    public async Task<UsageResult> GetSessionUsageAsync(IReadOnlyList<string> credentialsPaths, string cacheKey, bool force = false)
    {
        if (credentialsPaths.Count == 0) return UsageResult.Unauthorized; // 저장된 자격증명 자체가 없음

        var stamp = Stamp(credentialsPaths);
        lock (_cacheLock)
        {
            if (!force && _cache.TryGetValue(cacheKey, out var c)
                && c.Stamp == stamp && DateTime.UtcNow - c.At < CacheTtl)
            {
                return c.Result;
            }
        }

        var result = await FetchFirstUsableAsync(credentialsPaths);
        var after = Stamp(credentialsPaths); // 조회 중 토큰이 갱신됐을 수 있다

        lock (_cacheLock)
        {
            if (result.State is UsageState.Unavailable
                && _cache.TryGetValue(cacheKey, out var prev) && prev.Result.State is UsageState.Ok)
            {
                // 일시적 실패: 마지막 정상값을 유지하되 At을 갱신해 잠시 재시도하지 않는다(백오프).
                _cache[cacheKey] = (prev.Result, DateTime.UtcNow, after);
                return prev.Result;
            }

            _cache[cacheKey] = (result, DateTime.UtcNow, after);
        }
        return result;
    }

    /// <summary>자격증명 파일들의 마지막 쓰기 시각을 뭉친 값. 파일이 바뀌면 값이 달라져 캐시가 무효화된다.</summary>
    private static long Stamp(IReadOnlyList<string> paths)
    {
        long stamp = 0;
        foreach (var p in paths)
        {
            try { if (File.Exists(p)) stamp = (stamp * 31) + File.GetLastWriteTimeUtc(p).Ticks; }
            catch { /* 접근 실패는 0 으로 취급 */ }
        }
        return stamp;
    }

    /// <summary>
    /// 자격증명 후보를 순서대로 시도한다. 정상 결과가 나오면 그것으로 끝.
    /// 거부(Unauthorized)면 다음 후보로 넘어가고, 불확실(Unavailable)이면 거기서 멈춘다
    /// (오프라인·429 상황에서 후보마다 호출을 반복하지 않기 위해).
    /// **모든** 후보가 거부돼야 "다시 로그인 필요"로 확정한다 — 하나라도 살아 있으면 계정은 멀쩡하다.
    /// </summary>
    private async Task<UsageResult> FetchFirstUsableAsync(IReadOnlyList<string> paths)
    {
        UsageResult last = UsageResult.Unauthorized;
        foreach (var path in paths)
        {
            last = await FetchWithRefreshAsync(path);
            if (last.State is UsageState.Ok or UsageState.Unavailable) return last;
        }
        return last;
    }

    private async Task<UsageResult> FetchWithRefreshAsync(string credentialsPath)
    {
        try
        {
            if (!File.Exists(credentialsPath)) return UsageResult.Unauthorized;

            var (token, refresh, expiresAt) = ReadTokens(credentialsPath);
            if (token is null) return UsageResult.Unauthorized; // 자격증명 파일에 토큰이 없다

            // 만료(또는 임박)면 먼저 갱신
            bool nearExpiry = expiresAt is null || expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1);
            if (nearExpiry)
            {
                if (refresh is null) return UsageResult.Unauthorized; // 만료됐는데 갱신 수단이 없다

                var (tokens, rejected) = await TryRefreshAsync(refresh);
                if (tokens is { } r1)
                {
                    token = r1.Access;
                    WriteTokens(credentialsPath, r1.Access, r1.Refresh, r1.ExpiresAtMs);
                }
                else if (rejected)
                {
                    return UsageResult.Unauthorized; // 리프레시 토큰이 폐기됨 → 재로그인만이 답
                }

                // 거부가 아니면 네트워크 문제일 수 있으니 기존 토큰으로 한 번 시도해 본다.
            }

            var (usage, unauthorized) = await FetchUsageAsync(token);
            if (usage is not null) return new UsageResult(usage, UsageState.Ok);
            if (!unauthorized) return UsageResult.Unavailable; // 429·오프라인·무료 플랜 등

            // 토큰이 (예상과 달리) 거부됨 → 한 번 더 갱신 재시도
            if (refresh is null) return UsageResult.Unauthorized;

            var (retry, retryRejected) = await TryRefreshAsync(refresh);
            if (retry is not { } r2) return retryRejected ? UsageResult.Unauthorized : UsageResult.Unavailable;

            WriteTokens(credentialsPath, r2.Access, r2.Refresh, r2.ExpiresAtMs);
            var (usage2, unauthorized2) = await FetchUsageAsync(r2.Access);
            if (usage2 is not null) return new UsageResult(usage2, UsageState.Ok);
            return unauthorized2 ? UsageResult.Unauthorized : UsageResult.Unavailable;
        }
        catch { return UsageResult.Unavailable; }
    }

    /// <summary>usage 엔드포인트 호출. (결과, 인증실패여부). 인증실패면 토큰 갱신이 필요하다는 신호.</summary>
    private static async Task<(SessionUsage? Usage, bool Unauthorized)> FetchUsageAsync(string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            AddCliHeaders(req);

            using var resp = await Http.SendAsync(req);
            // 401 만 "이 토큰은 못 쓴다"로 본다. 403 은 WAF 차단·권한 없음 등일 수 있어 재로그인 신호가 아니다.
            if (resp.StatusCode is HttpStatusCode.Unauthorized)
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

    /// <summary>
    /// 리프레시 토큰으로 액세스 토큰을 재발급한다.
    /// 반환: (토큰, 거부됨). 거부됨(4xx) = 리프레시 토큰이 폐기/만료 → 재로그인 필요.
    /// 네트워크 오류·5xx·429 는 거부가 아니라 일시적 실패로 본다.
    /// </summary>
    private static async Task<(RefreshedTokens? Tokens, bool Rejected)> TryRefreshAsync(string refreshToken)
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
            AddCliHeaders(req); // User-Agent 가 없으면 Cloudflare 가 1010(403)으로 막는다

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                // 상태코드만으로 판단하지 않는다 — 404(엔드포인트 이전)·403(WAF)·5xx·429 를 "재로그인 필요"로
                // 오인하면 멀쩡한 계정이 만료로 보인다. 서버가 OAuth 거부를 명시했을 때만 거부로 본다.
                return (null, IsOAuthRejection(await resp.Content.ReadAsStringAsync()));
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            string? access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(access)) return (null, false);

            // refresh_token은 회전될 수 있다(없으면 기존 것 유지).
            string refresh = root.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { } s && s.Length > 0
                ? s : refreshToken;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long expiresAtMs = root.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number
                ? nowMs + ei.GetInt64() * 1000
                : nowMs + 3600_000;

            return (new RefreshedTokens(access, refresh, expiresAtMs), false);
        }
        catch { return (null, false); }
    }

    /// <summary>claude CLI 와 동일한 헤더. User-Agent 가 없으면 Cloudflare 가 요청을 막는다(오류 1010).</summary>
    private static void AddCliHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        req.Headers.UserAgent.ParseAdd("claude-cli/2.0.0 (external, cli)");
        req.Headers.Accept.ParseAdd("application/json");
    }

    /// <summary>
    /// 응답 본문이 "이 자격증명은 더 이상 유효하지 않다"는 OAuth 거부인지 판정한다.
    /// (invalid_grant = 리프레시 토큰 만료/폐기). 그 외의 오류는 일시적/인프라 문제로 본다.
    /// </summary>
    private static bool IsOAuthRejection(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("error", out var e)) return false;

            string? code = e.ValueKind switch
            {
                JsonValueKind.String => e.GetString(),
                JsonValueKind.Object => e.TryGetProperty("type", out var t) ? t.GetString() : null,
                _ => null,
            };
            return code is "invalid_grant" or "invalid_client" or "unauthorized_client" or "invalid_token";
        }
        catch { return false; }
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
