using System.IO;
using System.Text.Json;

namespace ClaudeAccountSwitcher.Services;

/// <summary>.credentials.json 의 표시용 메타데이터.</summary>
public sealed record CredentialsMeta(string? SubscriptionType, string? RateLimitTier);

/// <summary>.credentials.json 에서 표시용 메타데이터만 읽는다(토큰 값은 다루지 않음).</summary>
public static class CredentialsReader
{
    public static CredentialsMeta? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o) || o.ValueKind != JsonValueKind.Object)
                return null;

            return new CredentialsMeta(
                o.TryGetProperty("subscriptionType", out var s) ? s.GetString() : null,
                o.TryGetProperty("rateLimitTier", out var t) ? t.GetString() : null);
        }
        catch { return null; }
    }
}
