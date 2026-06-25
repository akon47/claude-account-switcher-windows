using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeAccountSwitcher.Models;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 프로필 폴더(CLAUDE_CONFIG_DIR)에 Claude Code statusLine 을 설치한다.
/// 그 계정으로 띄운 터미널의 claude 하단에 "👤 이메일 · 플랜 · 이름" 한 줄이 항상 보이게 한다.
///
/// 설계 요지(견고성):
/// - statusLine 명령은 Windows 에서 Git Bash 또는 PowerShell 로 실행될 수 있다(불확정).
///   둘 다 'powershell' 을 선두 토큰으로 실행하므로 `powershell -File &lt;ps1&gt;` 로 통일한다.
/// - 인코딩 함정을 원천 차단하려고, ps1 은 고정 앞부분(👤 이메일·플랜·이름)을 UTF-8 '바이트 배열'로 박아
///   표준출력에 쓴다(이모지/한글이 셸 코드페이지·Console 인코딩에 영향받지 않음). 스크립트는 순수 ASCII.
/// - 세션 잔량(5시간 창)은 claude 가 stdin 으로 주는 JSON 의 rate_limits.five_hour.used_percentage 를 ps1 이
///   읽어 "· 잔량%" 꼬리로 매 호출마다 붙인다(메시지마다 갱신 → 실시간, 우리 쪽 네트워크 호출 없음).
///   Claude.ai(Pro/Max) + 세션 첫 응답 이후에만 들어오므로 없으면 생략한다.
///   고정 앞부분은 설치 시점에 굳으므로 이름 변경/재로그인은 다음 실행에 반영된다.
/// - 사용자 보존: settings.json/settings.local.json 에 사용자 statusLine 이 있으면 손대지 않는다.
///   설정 우선순위가 더 높은 settings.local.json 에 사용자 것이 있으면 우리 것은 가려지므로 아예 빠진다.
///   우리가 깐 것(cas-statusline.ps1 참조)만 매 실행마다 현재 경로/내용으로 갱신한다.
/// </summary>
public static class StatusLineProvisioner
{
    private const string ScriptName = "cas-statusline.ps1";

    // settings.json 은 사실상 JSONC 로 다뤄지는 경우가 많다 — 주석/후행 콤마를 허용해 파싱 실패로 영구 무동작이 되지 않게.
    private static readonly JsonDocumentOptions ParseOpts =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static void Ensure(Profile p, bool enabled)
    {
        if (!enabled) { Remove(p); return; }

        try
        {
            Directory.CreateDirectory(p.ConfigDir);

            // settings.local.json 이 settings.json 보다 우선한다 — 거기에 사용자 statusLine 이 있으면 존중하고 빠진다.
            if (HasForeignStatusLine(Path.Combine(p.ConfigDir, "settings.local.json"))) return;

            string settingsPath = Path.Combine(p.ConfigDir, "settings.json");
            JsonObject root = new();
            bool indented = true;

            if (File.Exists(settingsPath))
            {
                string text = File.ReadAllText(settingsPath);
                JsonObject? parsed = TryParseObject(text);
                if (parsed is null) return; // 파싱 불가 → 사용자 파일 보존(절대 덮어쓰지 않음)
                root = parsed;
                indented = text.Contains('\n');

                // 어떤 형태든(오브젝트/문자열…) 우리 것이 아니면 사용자 것 → 보존.
                if (root["statusLine"] is JsonNode sl && !IsOurs(sl)) return;
            }

            string ps1 = Path.Combine(p.ConfigDir, ScriptName);
            WriteScript(ps1, BuildPrefix(p));

            root["statusLine"] = new JsonObject
            {
                ["type"] = "command",
                ["command"] = "powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""
                              + ps1.Replace('\\', '/') + "\"",
            };
            File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = indented,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 명령의 따옴표·경로를 읽기 쉬운 형태로
            }));
        }
        catch { /* best effort — 상태줄 설치 실패해도 실행은 정상 동작 */ }
    }

    /// <summary>상태줄 끄기: 우리가 설치한 statusLine 만 settings.json 에서 빼고 ps1 을 지운다(사용자 것은 보존).</summary>
    private static void Remove(Profile p)
    {
        try
        {
            string settingsPath = Path.Combine(p.ConfigDir, "settings.json");
            if (File.Exists(settingsPath))
            {
                string text = File.ReadAllText(settingsPath);
                JsonObject? root = TryParseObject(text);
                if (root is not null && root["statusLine"] is JsonNode sl && IsOurs(sl))
                {
                    root.Remove("statusLine");
                    if (root.Count == 0)
                    {
                        File.Delete(settingsPath);
                    }
                    else
                    {
                        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions
                        {
                            WriteIndented = text.Contains('\n'),
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        }));
                    }
                }
            }

            string ps1 = Path.Combine(p.ConfigDir, ScriptName);
            if (File.Exists(ps1)) File.Delete(ps1);
        }
        catch { /* best effort */ }
    }

    /// <summary>해당 파일이 '우리 것이 아닌' statusLine 을 정의하면 true(주석/후행 콤마 허용).</summary>
    private static bool HasForeignStatusLine(string path)
    {
        if (!File.Exists(path)) return false;
        try { return TryParseObject(File.ReadAllText(path))?["statusLine"] is JsonNode sl && !IsOurs(sl); }
        catch { return false; }
    }

    /// <summary>statusLine 노드가 우리가 설치한 것(cas-statusline.ps1 참조)인지.</summary>
    private static bool IsOurs(JsonNode? statusLine)
    {
        string? cmd = (statusLine as JsonObject)?["command"] is JsonValue v && v.TryGetValue(out string? c) ? c : null;
        return cmd is not null && cmd.Contains(ScriptName);
    }

    private static JsonObject? TryParseObject(string text)
    {
        try { return JsonNode.Parse(text, documentOptions: ParseOpts)?.AsObject(); }
        catch { return null; }
    }

    /// <summary>고정 앞부분 "👤 이메일 · 플랜 · 이름"(세션 잔량은 ps1 이 동적으로 덧붙인다). 이메일 없으면 가진 것만.</summary>
    private static string BuildPrefix(Profile p)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Email)) parts.Add(p.Email!);
        string plan = PlanFormatter.Format(p.SubscriptionType, p.RateLimitTier);
        if (plan != "—") parts.Add(plan);
        if (!string.IsNullOrWhiteSpace(p.Name)) parts.Add(p.Name);
        if (parts.Count == 0) parts.Add("Claude");

        // U+1F464 BUST IN SILHOUETTE + U+00B7 MIDDLE DOT (소스 인코딩 무관하도록 코드포인트로 구성)
        return char.ConvertFromUtf32(0x1F464) + " " + string.Join(" " + (char)0x00B7 + " ", parts);
    }

    /// <summary>
    /// statusLine 스크립트를 만든다. 고정 앞부분은 UTF-8 바이트 배열로 박고,
    /// claude 가 stdin 으로 주는 JSON 의 rate_limits.five_hour.used_percentage 를 읽어
    /// "· 잔량%" 꼬리를 매 호출마다 덧붙인다(없으면 생략). 0(=사용 0%)을 부재로 오인하지 않게 $null 비교.
    /// 스크립트는 순수 ASCII(바이트 배열 + 코드포인트)라 파일 인코딩/BOM·셸 코드페이지에 의존하지 않는다.
    /// </summary>
    private static void WriteScript(string path, string prefix)
    {
        string bytes = string.Join(",", Encoding.UTF8.GetBytes(prefix)); // 0~255 십진수
        string script =
            "$ErrorActionPreference='SilentlyContinue'\r\n" +
            "$p=[byte[]](" + bytes + ")\r\n" +
            "$s=''\r\n" +
            "try {\r\n" +
            "  $in=[Console]::In.ReadToEnd()\r\n" +
            "  if ($in) {\r\n" +
            "    $u=($in | ConvertFrom-Json).rate_limits.five_hour.used_percentage\r\n" +
            "    if ($u -ne $null) { $s=' '+[char]0x00B7+' '+[math]::Round([math]::Max(0,100-[double]$u))+'%' }\r\n" +
            "  }\r\n" +
            "} catch { }\r\n" +
            "$o=[System.Console]::OpenStandardOutput()\r\n" +
            "$o.Write($p,0,$p.Length)\r\n" +
            "if ($s) { $sb=[System.Text.Encoding]::UTF8.GetBytes($s); $o.Write($sb,0,$sb.Length) }\r\n" +
            "$o.Flush()\r\n";
        File.WriteAllText(path, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
