using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 프로필의 격리 설정(CLAUDE_CONFIG_DIR)으로 새 터미널에서 claude를 실행한다.
/// 셸(PowerShell/cmd) · --dangerously-skip-permissions 부여 여부 · 관리자 권한 승격을 선택할 수 있다.
/// </summary>
public static class Launcher
{
    /// <summary>
    /// 세션 자동 유지가 headless 로 보내는 한마디. 이 프롬프트로 시작된 sdk 트랜스크립트는 세션 브라우저에서 숨긴다
    /// (SessionStore 가 참조). 바꾸면 예전 기록은 다시 보이게 되므로 함부로 바꾸지 말 것.
    /// </summary>
    public const string KeepAlivePrompt = "hi";

    public static void LaunchInProfile(Profile p, string? workingDir, ShellKind shell, bool skipPermissions, bool statusLine, string? resumeSessionId = null, bool runAsAdmin = false)
    {
        Directory.CreateDirectory(p.ConfigDir);
        // 이 계정으로 띄운 claude 하단의 계정 상태줄: 설정이 켜져 있으면 설치/갱신, 꺼져 있으면 우리 것 제거.
        StatusLineProvisioner.Ensure(p, statusLine);

        string cwd = workingDir is not null && Directory.Exists(workingDir) ? workingDir : AppPaths.UserHome;
        string command = skipPermissions ? "claude --dangerously-skip-permissions" : "claude";
        // 특정 세션 이어하기(--resume <id>): 세션 파일은 호출 측에서 이 프로필 폴더로 복사해 둔다.
        if (!string.IsNullOrEmpty(resumeSessionId)) command += " --resume " + resumeSessionId;

        Start(p, cwd, shell, command, "Claude: " + p.Name, runAsAdmin);
    }

    /// <summary>
    /// 이 프로필로 로그인만 시킨다(`claude auth login`). REPL 로 들어가지 않으므로 자격증명이 비어 있어도
    /// "로그인하라"는 에러 대신 로그인 화면이 곧바로 뜬다(= claude 안에서 /login 을 친 것과 같음).
    /// 새 계정 추가·다시 로그인에서 사용. 브라우저 인증이 끼어들므로 관리자 권한으로는 승격하지 않는다
    /// (승격된 셸에서 브라우저를 띄우면 로그인 세션이 없는 별도 인스턴스가 열릴 수 있음).
    /// </summary>
    public static void LaunchLogin(Profile p, ShellKind shell)
    {
        Directory.CreateDirectory(p.ConfigDir);
        Start(p, AppPaths.UserHome, shell, "claude auth login", "Claude login: " + p.Name, runAsAdmin: false);
    }

    /// <summary>
    /// 격리 설정(CLAUDE_CONFIG_DIR)을 건 새 셸 창에서 명령을 실행한다.
    /// CLAUDE_CONFIG_DIR 은 ProcessStartInfo 가 아니라 셸 명령 안에서 설정한다 — 관리자 승격(runas)은
    /// ShellExecute 경로라 EnvironmentVariables 를 쓸 수 없기 때문에 두 경우를 같은 방식으로 통일한다.
    /// </summary>
    private static void Start(Profile p, string cwd, ShellKind shell, string command, string title, bool runAsAdmin)
    {
        var psi = shell switch
        {
            ShellKind.Cmd => new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/K set \"CLAUDE_CONFIG_DIR={p.ConfigDir}\" & title {EscapeCmdTitle(title)} & {command}",
            },
            _ => new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"$env:CLAUDE_CONFIG_DIR='{PsQuote(p.ConfigDir)}'; $Host.UI.RawUI.WindowTitle='{PsQuote(title)}'; {command}\"",
            },
        };

        psi.WorkingDirectory = cwd;

        if (runAsAdmin)
        {
            // UAC 승격은 ShellExecute 로만 가능하다(앱 자신은 일반 권한이므로 실행할 때마다 UAC 창이 뜬다).
            psi.UseShellExecute = true;
            psi.Verb = "runas";
        }
        else
        {
            psi.UseShellExecute = false;
        }

        try
        {
            Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — 사용자가 UAC 창에서 취소. 오류로 취급하지 않는다.
        }
    }

    /// <summary>
    /// 창 없이 claude 에 한마디(`claude -p "hi"`)를 보내 5시간 세션 창을 시작/갱신한다(세션 자동 유지).
    /// <paramref name="useHome"/> 이면 ~/.claude(라이브 토큰)로, 아니면 CLAUDE_CONFIG_DIR=프로필폴더(격리 보관본)로
    /// 실행한다. 호출부는 활성 프로필이라도 ~/.claude 가 정말 그 계정일 때만 useHome 을 준다.
    /// 보이는 창/콘솔 없이 조용히 돌리고(fire-and-forget), 실패해도 앱에는 영향 없다.
    /// </summary>
    public static void FireKeepAlive(Profile p, bool useHome)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                // claude 는 보통 npm 셸(.cmd) 이라 직접 실행이 안 되므로 cmd 로 감싼다.
                FileName = "cmd.exe",
                Arguments = $"/c claude -p \"{KeepAlivePrompt}\"",
                UseShellExecute = false,
                CreateNoWindow = true,            // 콘솔 창 안 뜸
                WorkingDirectory = AppPaths.UserHome, // 신뢰된 폴더(폴더 신뢰 프롬프트 회피)
            };

            // 라이브(~/.claude) 자격증명을 쓸 때만 오버라이드하지 않는다.
            if (!useHome) psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = p.ConfigDir;

            Process.Start(psi);
        }
        catch { /* best effort — claude 미설치/일시 오류여도 감시는 계속 */ }
    }

    /// <summary>cmd 의 `title` 명령에 안전하게 넘기도록 메타문자를 캐럿 이스케이프한다.</summary>
    private static string EscapeCmdTitle(string s) =>
        s.Replace("^", "^^").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>").Replace("|", "^|");

    /// <summary>PowerShell 작은따옴표 리터럴에 넣도록 작은따옴표를 이중화한다.</summary>
    private static string PsQuote(string s) => s.Replace("'", "''");
}
