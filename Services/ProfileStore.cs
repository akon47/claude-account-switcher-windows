using System.IO;
using System.Text.Json;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>프로필 저장소 + 핵심 동작(캡처/전환/삭제). UI 에 의존하지 않는다.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppData Data { get; private set; } = new();

    /// <summary>
    /// profiles.json 을 읽는다. 깨져 있으면(쓰다 꺼짐 등) 직전 저장 때 남긴 .bak 로 폴백한다 —
    /// 설정 파일 하나가 망가졌다고 계정 목록이 통째로 사라지지 않게.
    /// </summary>
    public void Load()
    {
        AppPaths.EnsureDirs();
        Data = TryRead(AppPaths.DataFile) ?? TryRead(AppPaths.DataFile + ".bak") ?? new();

        static AppData? TryRead(string path)
        {
            try { return File.Exists(path) ? JsonSerializer.Deserialize<AppData>(File.ReadAllText(path)) : null; }
            catch { return null; }
        }
    }

    /// <summary>임시 파일에 쓴 뒤 교체하고, 직전 내용은 .bak 로 남긴다(쓰는 도중 꺼져도 둘 중 하나는 온전하다).</summary>
    public void Save()
    {
        AppPaths.EnsureDirs();
        string path = AppPaths.DataFile;
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, JsonOpts));
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }

    /// <summary>
    /// profiles.json 에 없는 프로필 폴더 중 사용자 데이터가 전혀 없는 빈 껍데기(자격증명도 세션도 없음 —
    /// 로그인 전에 창을 닫고 삭제했거나 삭제가 일부 실패해 남은 plugins/ 만 든 폴더)만 지운다.
    /// 자격증명이나 projects 가 있는 폴더는 profiles.json 이 깨졌을 가능성에 대비해 절대 건드리지 않는다.
    /// </summary>
    public void PruneOrphanDirs()
    {
        try
        {
            if (!Directory.Exists(AppPaths.ProfilesDir)) return;
            HashSet<string> known = new(Data.Profiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            foreach (string dir in Directory.GetDirectories(AppPaths.ProfilesDir))
            {
                if (known.Contains(Path.GetFileName(dir))) continue;
                if (File.Exists(Path.Combine(dir, ".credentials.json"))) continue;
                if (File.Exists(Path.Combine(dir, ".claude.json"))) continue;
                if (Directory.Exists(Path.Combine(dir, "projects"))) continue;
                try { Directory.Delete(dir, recursive: true); } catch { /* 잠김 등 — 다음 시작 때 다시 */ }
            }
        }
        catch { /* best effort */ }
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
        List<string> sources = new(2);
        if (p.Id == Data.ActiveProfileId && File.Exists(AppPaths.ClaudeCredentials) && HomeBelongsTo(p))
            sources.Add(AppPaths.ClaudeCredentials);
        if (File.Exists(p.CredentialsPath)) sources.Add(p.CredentialsPath);
        return sources;
    }

    public void RefreshMetadata(Profile p)
    {
        if (CredentialsReader.Read(p.CredentialsPath) is { } meta)
        {
            p.SubscriptionType = meta.SubscriptionType;
            p.RateLimitTier = meta.RateLimitTier;
        }

        // 계정 정보: 격리 로그인 폴더의 .claude.json 우선, 없으면 저장해 둔 oauthAccount.json
        StoreAccount(p, ClaudeConfig.FromClaudeJson(p.ClaudeJsonPath) ?? ClaudeConfig.FromObjectFile(p.OAuthAccountPath));
    }

    /// <summary>계정 정보를 프로필에 반영하고 oauthAccount.json 으로 보관한다(내용이 달라졌을 때만 쓴다).</summary>
    private static void StoreAccount(Profile p, OAuthAccountInfo? info)
    {
        if (info is null) return;
        p.Email = info.Email;
        p.DisplayName = info.DisplayName;
        p.OrganizationName = info.OrganizationName;
        p.PlanDisplayName = info.PlanDisplayName;
        try
        {
            if (File.Exists(p.OAuthAccountPath) && File.ReadAllText(p.OAuthAccountPath) == info.Raw) return;
            File.WriteAllText(p.OAuthAccountPath, info.Raw);
        }
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

        Profile p = new() { Name = name };
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
        Profile p = new() { Name = name };
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
            BackupCredentials(p.CredentialsPath, $"credentials-{p.Id}");
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
        if (Active is { } active && active.Id != target.Id && File.Exists(AppPaths.ClaudeCredentials)
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
        if (File.Exists(AppPaths.ClaudeCredentials)) BackupCredentials(AppPaths.ClaudeCredentials, "credentials");

        // 3) 대상 활성화: 토큰 교체 + ~/.claude.json 의 oauthAccount 교체
        File.Copy(target.CredentialsPath, AppPaths.ClaudeCredentials, overwrite: true);
        if (ClaudeConfig.FromObjectFile(target.OAuthAccountPath) is { } targetAcct)
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
            .OrderBy(p => order.IndexOf(p.Id) is var i and >= 0 ? i : int.MaxValue)
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

    /// <summary>자격증명 파일을 backups\ 에 타임스탬프 사본으로 남기고 오래된 것은 정리한다(20개 보관).</summary>
    private static void BackupCredentials(string source, string prefix)
    {
        try
        {
            File.Copy(source, Path.Combine(AppPaths.BackupsDir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: true);
            foreach (var f in new DirectoryInfo(AppPaths.BackupsDir)
                         .GetFiles("credentials-*.json")
                         .OrderByDescending(f => f.CreationTimeUtc)
                         .Skip(20))
            {
                f.Delete();
            }
        }
        catch { /* best effort */ }
    }
}
