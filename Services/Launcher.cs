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

    /// <summary>
    /// 창 없이 claude 에 한마디(`claude -p "hi"`)를 보내 5시간 세션 창을 시작/갱신한다(세션 자동 유지).
    /// 활성 프로필이면 ~/.claude(라이브 토큰)로, 그 외엔 CLAUDE_CONFIG_DIR=프로필폴더(격리 보관본)로 실행한다.
    /// 보이는 창/콘솔 없이 조용히 돌리고(fire-and-forget), 실패해도 앱에는 영향 없다.
    /// </summary>
    public static void FireKeepAlive(Profile p, bool isActive)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                // claude 는 보통 npm 셸(.cmd) 이라 직접 실행이 안 되므로 cmd 로 감싼다.
                FileName = "cmd.exe",
                Arguments = "/c claude -p \"hi\"",
                UseShellExecute = false,
                CreateNoWindow = true,            // 콘솔 창 안 뜸
                WorkingDirectory = AppPaths.UserHome, // 신뢰된 폴더(폴더 신뢰 프롬프트 회피)
            };

            // 활성 계정은 ~/.claude 의 라이브 자격증명을 쓰도록 오버라이드하지 않는다.
            if (!isActive) psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = p.ConfigDir;

            Process.Start(psi);
        }
        catch { /* best effort — claude 미설치/일시 오류여도 감시는 계속 */ }
    }

    /// <summary>cmd 의 `title` 명령에 안전하게 넘기도록 메타문자를 캐럿 이스케이프한다.</summary>
    private static string EscapeCmdTitle(string s) =>
        s.Replace("^", "^^").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>").Replace("|", "^|");
}
