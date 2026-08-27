using System.IO;
using System.Text.Json;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 프로필 저장소 + 핵심 동작(캡처/전환/삭제). UI에 의존하지 않는다.
/// </summary>
public class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppData Data { get; private set; } = new();

    public void Load()
    {
        AppPaths.EnsureDirs();
        if (File.Exists(AppPaths.DataFile))
        {
            try
            {
                Data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(AppPaths.DataFile)) ?? new();
            }
            catch
            {
                Data = new();
            }
        }
    }

    public void Save()
    {
        AppPaths.EnsureDirs();
        File.WriteAllText(AppPaths.DataFile, JsonSerializer.Serialize(Data, JsonOpts));
    }

    public Profile? Active =>
        Data.ActiveProfileId is null ? null : Data.Profiles.FirstOrDefault(p => p.Id == Data.ActiveProfileId);

    public bool HasCredentials(Profile p) => File.Exists(p.CredentialsPath);

    /// <summary>
    /// ~/.claude 에 지금 들어있는 자격증명이 이 프로필의 것인지(계정 정보 ~/.claude.json 의 oauthAccount 로 판정).
    /// 전환 없이 오래 두면 ActiveProfileId 와 ~/.claude 의 실제 계정이 어긋날 수 있어(다른 계정의 낡은 토큰이
    /// 남아 있는 상태) 이 확인이 필요하다. 어느 한쪽 이메일을 모르면 true(예전 동작 유지) —
    /// **확실히 다를 때만** false 로 본다.
    /// </summary>
    public static bool HomeBelongsTo(Profile p)
    {
        string? homeEmail = ClaudeConfig.FromClaudeJson(ClaudeConfig.HomeConfigPath)?.Email;
        if (string.IsNullOrEmpty(homeEmail) || string.IsNullOrEmpty(p.Email)) return true;
        return string.Equals(homeEmail, p.Email, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 이 계정의 자격증명 후보 경로(우선순위 순). 사용량 조회·상태 판정에 쓴다.
    /// - 활성 프로필이면 ~/.claude 의 라이브 토큰이 먼저(단 그 계정이 이 프로필과 같을 때만).
    /// - 그 다음은 프로필 보관본 — 격리 실행(CLAUDE_CONFIG_DIR)으로 claude 가 계속 갱신하는 사본이라,
    ///   ~/.claude 가 오래돼 죽어 있어도 이쪽은 살아 있는 경우가 많다.
    /// 하나라도 살아 있으면 그 계정은 쓸 수 있는 것이므로 "다시 로그인 필요"로 단정하지 않는다.
    /// </summary>
    public IReadOnlyList<string> CredentialSources(Profile p)
    {
        var sources = new List<string>(2);
        if (p.Id == Data.ActiveProfileId && File.Exists(AppPaths.ClaudeCredentials) && HomeBelongsTo(p))
            sources.Add(AppPaths.ClaudeCredentials);
        if (File.Exists(p.CredentialsPath)) sources.Add(p.CredentialsPath);
        return sources;
    }

    public void RefreshMetadata(Profile p)
    {
        var meta = CredentialsReader.Read(p.CredentialsPath);
        if (meta is not null)
        {
            p.SubscriptionType = meta.SubscriptionType;
            p.RateLimitTier = meta.RateLimitTier;
        }

        // 계정 정보: 격리 로그인 폴더의 .claude.json 우선, 없으면 저장해 둔 oauthAccount.json
        var acct = ClaudeConfig.FromClaudeJson(p.ClaudeJsonPath)
                ?? ClaudeConfig.FromObjectFile(p.OAuthAccountPath);
        StoreAccount(p, acct);
    }

    /// <summary>계정 정보를 프로필에 반영하고 oauthAccount.json 으로 보관한다.</summary>
    private static void StoreAccount(Profile p, OAuthAccountInfo? info)
    {
        if (info is null) return;
        p.Email = info.Email;
        p.DisplayName = info.DisplayName;
        p.OrganizationName = info.OrganizationName;
        try { File.WriteAllText(p.OAuthAccountPath, info.Raw); }
        catch { /* best effort */ }
    }

    public void RefreshAll()
    {
        foreach (var p in Data.Profiles) RefreshMetadata(p);
    }

    /// <summary>현재 ~/.claude 에 로그인된 계정을 새 프로필로 캡처한다.</summary>
    public Profile CaptureCurrent(string name)
    {
        if (!File.Exists(AppPaths.ClaudeCredentials))
        {
            throw new InvalidOperationException(
                "현재 로그인된 Claude 계정이 없습니다. 먼저 터미널에서 'claude'로 로그인하세요.");
        }

        var p = new Profile { Name = name };
        Directory.CreateDirectory(p.ConfigDir);
        File.Copy(AppPaths.ClaudeCredentials, p.CredentialsPath, overwrite: true);
        RefreshMetadata(p);
        // 현재 전역 계정(~/.claude.json)의 oauthAccount 를 이 프로필에 저장
        StoreAccount(p, ClaudeConfig.FromClaudeJson(ClaudeConfig.HomeConfigPath));
        p.LastUsed = DateTime.Now;

        Data.Profiles.Add(p);
        Data.ActiveProfileId = p.Id; // 현재 자격증명 == 이 프로필
        Save();
        return p;
    }

    /// <summary>빈 프로필을 만든다. 이후 격리 로그인으로 자격증명을 채운다.</summary>
    public Profile CreateForLogin(string name)
    {
        var p = new Profile { Name = name };
        Directory.CreateDirectory(p.ConfigDir);
        Data.Profiles.Add(p);
        Save();
        return p;
    }

    /// <summary>
    /// 기존 프로필을 다시 로그인할 수 있도록 저장된 자격증명을 백업 후 비운다.
    /// 이후 이 프로필로 격리 실행하면(CLAUDE_CONFIG_DIR) 자격증명이 없어 claude 가 로그인 프롬프트를 띄운다.
    /// 구독/플랜이 바뀌었을 때 재로그인으로 갱신하는 용도(삭제 후 재추가와 동일 효과, 프로필·세션은 보존).
    /// .claude.json(온보딩/폴더 신뢰 상태)은 남겨 재프롬프트를 피한다. 활성 프로필이어도 ~/.claude(라이브
    /// 토큰)는 건드리지 않는다 — 격리 폴더 사본만 비우므로 현재 활성 세션은 그대로 유지된다.
    /// </summary>
    public void PrepareRelogin(Profile p)
    {
        Directory.CreateDirectory(p.ConfigDir);

        if (File.Exists(p.CredentialsPath))
        {
            try
            {
                string backup = Path.Combine(AppPaths.BackupsDir, $"credentials-{p.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(p.CredentialsPath, backup, overwrite: true);
                PruneBackups(20);
            }
            catch { /* best effort */ }

            try { File.Delete(p.CredentialsPath); } catch { /* best effort */ }
        }

        // 자격증명뿐 아니라 .claude.json 의 oauthAccount 도 비운다 — 남겨 두면 claude 가 로그인된 것으로
        // 보고 로그인 화면 없이 REPL 로 들어간다. 화면 표시용 이메일은 oauthAccount.json 사본에서 계속 읽는다.
        ClaudeConfig.RemoveOAuthAccount(p.ClaudeJsonPath);
    }

    /// <summary>대상 프로필을 ~/.claude 활성 계정으로 전환한다.</summary>
    public void SwitchTo(Profile target)
    {
        // 1) 떠나는 활성 프로필에 갱신된 토큰을 되돌려 저장.
        //    단 ~/.claude 가 정말 그 프로필 계정일 때만 — 아니면 남의(또는 낡은) 토큰으로
        //    멀쩡한 보관본을 덮어써 계정이 망가진다.
        var active = Active;
        if (active is not null && active.Id != target.Id && File.Exists(AppPaths.ClaudeCredentials)
            && HomeBelongsTo(active))
        {
            try
            {
                Directory.CreateDirectory(active.ConfigDir);
                File.Copy(AppPaths.ClaudeCredentials, active.CredentialsPath, overwrite: true);
                // 떠나는 계정의 oauthAccount 도 최신으로 보관
                StoreAccount(active, ClaudeConfig.FromClaudeJson(ClaudeConfig.HomeConfigPath));
            }
            catch { /* best effort */ }
        }

        if (!File.Exists(target.CredentialsPath))
        {
            throw new InvalidOperationException(
                $"'{target.Name}' 프로필에 저장된 로그인 정보가 없습니다. 먼저 이 프로필로 로그인하세요.");
        }

        // 2) 현재 ~/.claude 자격증명 백업
        Directory.CreateDirectory(AppPaths.ClaudeHome);
        if (File.Exists(AppPaths.ClaudeCredentials))
        {
            string backup = Path.Combine(AppPaths.BackupsDir, $"credentials-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try
            {
                File.Copy(AppPaths.ClaudeCredentials, backup, overwrite: true);
                PruneBackups(20);
            }
            catch { /* best effort */ }
        }

        // 3) 대상 활성화: 토큰 교체 + ~/.claude.json 의 oauthAccount 교체
        File.Copy(target.CredentialsPath, AppPaths.ClaudeCredentials, overwrite: true);
        var targetAcct = ClaudeConfig.FromObjectFile(target.OAuthAccountPath);
        if (targetAcct is not null)
            ClaudeConfig.PatchHomeOAuthAccount(targetAcct.Raw);
        target.LastUsed = DateTime.Now;
        Data.ActiveProfileId = target.Id;
        RefreshMetadata(target);
        Save();
    }

    public void Rename(Profile p, string newName)
    {
        p.Name = newName;
        Save();
    }

    /// <summary>표시 순서(프로필 Id 순서)에 맞춰 저장된 프로필 목록을 재정렬하고 저장한다.</summary>
    public void Reorder(IEnumerable<string> idOrder)
    {
        var order = idOrder.ToList();
        var ordered = Data.Profiles
            .OrderBy(p => { int i = order.IndexOf(p.Id); return i < 0 ? int.MaxValue : i; })
            .ToList();
        Data.Profiles.Clear();
        Data.Profiles.AddRange(ordered);
        Save();
    }

    public void Delete(Profile p)
    {
        try
        {
            if (Directory.Exists(p.ConfigDir)) Directory.Delete(p.ConfigDir, recursive: true);
        }
        catch { /* best effort */ }

        Data.Profiles.Remove(p);
        if (Data.ActiveProfileId == p.Id) Data.ActiveProfileId = null;
        Save();
    }

    private static void PruneBackups(int keep)
    {
        try
        {
            var stale = new DirectoryInfo(AppPaths.BackupsDir)
                .GetFiles("credentials-*.json")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(keep);
            foreach (var f in stale) f.Delete();
        }
        catch { /* best effort */ }
    }
}
