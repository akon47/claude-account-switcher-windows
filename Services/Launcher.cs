using System.Diagnostics;
using System.IO;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 프로필의 격리 설정(CLAUDE_CONFIG_DIR)으로 새 터미널에서 claude를 실행한다.
/// 셸(PowerShell/cmd)과 --dangerously-skip-permissions 부여 여부를 선택할 수 있다.
/// </summary>
public static class Launcher
{
    public static void LaunchInProfile(Profile p, string? workingDir, ShellKind shell, bool skipPermissions, bool statusLine)
    {
        Directory.CreateDirectory(p.ConfigDir);
        // 이 계정으로 띄운 claude 하단의 계정 상태줄: 설정이 켜져 있으면 설치/갱신, 꺼져 있으면 우리 것 제거.
        StatusLineProvisioner.Ensure(p, statusLine);

        string cwd = workingDir is not null && Directory.Exists(workingDir) ? workingDir : AppPaths.UserHome;
        string command = skipPermissions ? "claude --dangerously-skip-permissions" : "claude";
        string title = "Claude: " + p.Name;

        var psi = shell switch
        {
            ShellKind.Cmd => new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/K title {EscapeCmdTitle(title)} & {command}",
            },
            _ => new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"$Host.UI.RawUI.WindowTitle='{title.Replace("'", "''")}'; {command}\"",
            },
        };

        psi.UseShellExecute = false;
        psi.WorkingDirectory = cwd;
        // 이 창에서 실행되는 claude는 격리된 설정 폴더를 사용한다.
        psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = p.ConfigDir;

        Process.Start(psi);
    }

    /// <summary>cmd 의 `title` 명령에 안전하게 넘기도록 메타문자를 캐럿 이스케이프한다.</summary>
    private static string EscapeCmdTitle(string s) =>
        s.Replace("^", "^^").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>").Replace("|", "^|");
}
