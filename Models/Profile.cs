using System.IO;
using System.Text.Json.Serialization;
using ClaudeAccountSwitcher.Services;

namespace ClaudeAccountSwitcher.Models;

/// <summary>
/// 하나의 Claude 계정 = 하나의 프로필.
/// 각 프로필은 자기만의 격리된 설정 폴더(CLAUDE_CONFIG_DIR)를 가진다.
/// </summary>
public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string? SubscriptionType { get; set; }
    public string? RateLimitTier { get; set; }
    public DateTime? LastUsed { get; set; }

    // 계정 식별 정보 (oauthAccount 에서 가져온다)
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? OrganizationName { get; set; }

    /// <summary>이 프로필 전용 격리 설정 폴더. 동시 실행 시 CLAUDE_CONFIG_DIR로 사용.</summary>
    [JsonIgnore]
    public string ConfigDir => Path.Combine(AppPaths.ProfilesDir, Id);

    /// <summary>이 프로필이 보관하는 자격증명 파일 경로.</summary>
    [JsonIgnore]
    public string CredentialsPath => Path.Combine(ConfigDir, ".credentials.json");

    /// <summary>격리 로그인 시 claude가 만드는 설정 파일(여기 oauthAccount 가 들어있음).</summary>
    [JsonIgnore]
    public string ClaudeJsonPath => Path.Combine(ConfigDir, ".claude.json");

    /// <summary>전환 시 ~/.claude.json 에 써넣을 oauthAccount 원본 보관 파일.</summary>
    [JsonIgnore]
    public string OAuthAccountPath => Path.Combine(ConfigDir, "oauthAccount.json");

    /// <summary>세션(5시간) 남은 사용량 표시 텍스트. 네트워크 조회 결과의 메모리 캐시(영속 안 함).</summary>
    [JsonIgnore]
    public string? SessionRemaining { get; set; }

    /// <summary>세션 남은 비율(0~100). 색 구간 표시용 메모리 캐시(영속 안 함).</summary>
    [JsonIgnore]
    public double? SessionPercent { get; set; }
}
