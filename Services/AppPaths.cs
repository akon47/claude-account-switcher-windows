using System.IO;

namespace ClaudeAccountSwitcher.Services;

/// <summary>앱이 사용하는 모든 경로를 한곳에서 관리.</summary>
public static class AppPaths
{
    public static string UserHome =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>plain `claude` 명령이 사용하는 기본 설정 폴더(~/.claude).</summary>
    public static string ClaudeHome => Path.Combine(UserHome, ".claude");

    public static string ClaudeCredentials => Path.Combine(ClaudeHome, ".credentials.json");

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeAccountSwitcher");

    public static string ProfilesDir => Path.Combine(AppDataDir, "profiles");
    public static string BackupsDir => Path.Combine(AppDataDir, "backups");
    public static string DataFile => Path.Combine(AppDataDir, "profiles.json");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(BackupsDir);
    }
}
