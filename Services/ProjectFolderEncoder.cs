using System.Text.RegularExpressions;

namespace ClaudeAccountSwitcher.Services;

/// <summary>
/// 작업 폴더(cwd)를 claude 의 <c>projects\&lt;enc&gt;</c> 폴더명으로 인코딩한다.
/// claude 는 cwd 의 영숫자([a-zA-Z0-9])가 아닌 모든 문자를 <c>-</c> 로 바꾼다(문자당 1개, 병합 없음).
/// 실제 폴더로 검증: <c>C:\Users\..\OneDrive\문서\..</c> → <c>C--Users-..-OneDrive----..</c>
/// (<c>\문서\</c> = 4문자 → <c>----</c>), <c>aurora_player_mobile</c> → <c>aurora-player-mobile</c>.
/// 다른 PC 로 옮겨 작업 폴더가 바뀌었을 때, 새 폴더 기준으로 대상 enc 폴더명을 다시 만들어 resume 가
/// 세션을 찾게 하려고 쓴다.
/// </summary>
public static class ProjectFolderEncoder
{
    private static readonly Regex NonAlphaNum = new("[^a-zA-Z0-9]", RegexOptions.Compiled);

    public static string Encode(string cwd) => NonAlphaNum.Replace(cwd ?? string.Empty, "-");
}
