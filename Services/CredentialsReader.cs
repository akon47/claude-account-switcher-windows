using System.IO;
using System.Text.Json;

namespace ClaudeAccountSwitcher.Services;

public record CredentialsMeta(string? SubscriptionType, string? RateLimitTier, DateTimeOffset? ExpiresAt);

/// <summary>.credentials.json 에서 표시용 메타데이터만 읽는다. (토큰 값은 다루지 않음)</summary>
public static class CredentialsReader
{
    public static CredentialsMeta? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o))
                return null;

            string? sub = o.TryGetProperty("subscriptionType", out var s) ? s.GetString() : null;
            string? tier = o.TryGetProperty("rateLimitTier", out var t) ? t.GetString() : null;

            DateTimeOffset? exp = null;
            if (o.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number)
                exp = DateTimeOffset.FromUnixTimeMilliseconds(e.GetInt64());

            return new CredentialsMeta(sub, tier, exp);
        }
        catch
        {
            return null;
        }
    }
}
