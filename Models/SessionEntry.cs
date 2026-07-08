namespace ClaudeAccountSwitcher.Models;

/// <summary>
/// 한 프로필의 대화 세션 하나(claude 트랜스크립트 <c>&lt;configdir&gt;\projects\&lt;enc&gt;\&lt;id&gt;.jsonl</c>).
/// 계정 간 이어하기(resume) 목록/실행에 필요한 최소 메타만 담는다(파일 내용은 지연 로드).
/// </summary>
public sealed class SessionEntry
{
    /// <summary>세션 UUID(= 파일명, --resume 인자).</summary>
    public required string SessionId { get; init; }

    /// <summary>projects 아래 인코딩된 프로젝트 폴더명. cwd 로만 결정되어 계정과 무관 → 복사 시 그대로 재사용.</summary>
    public required string ProjectFolder { get; init; }

    /// <summary>트랜스크립트에서 읽은 실제 작업 폴더(절대경로). resume 실행 위치.</summary>
    public required string Cwd { get; init; }

    /// <summary>이 세션 파일의 원본 경로.</summary>
    public required string FilePath { get; init; }

    /// <summary>소스 프로필 id/이름(표시용).</summary>
    public required string ProfileId { get; init; }

    public required string ProfileName { get; init; }

    /// <summary>파일 최종 수정 시각(최근 대화 정렬용).</summary>
    public DateTime LastModified { get; init; }

    /// <summary>요약 또는 첫 사용자 메시지 미리보기(없으면 null).</summary>
    public string? Preview { get; init; }

    /// <summary>
    /// Claude Code 가 붙인 세션 이름(트랜스크립트의 <c>ai-title</c>). 대화가 진행되며 갱신되므로
    /// 마지막 값이 현재 이름이다. 없으면 null(초기/짧은 세션).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>소스 계정 이메일(표시용, 없으면 null). 파일 번들에서 온 세션의 원본 계정 식별에도 쓴다.</summary>
    public string? SourceEmail { get; init; }

    /// <summary>
    /// 번들에 이 세션의 작업 폴더(cwd 내용)가 함께 담겨 있을 때, 풀어놓은 임시 폴더의 경로(없으면 null).
    /// 가져오기 후 원본 cwd 가 이 PC 에 없으면 여기서 사용자가 고른 위치로 복원해 그 폴더에서 이어한다.
    /// </summary>
    public string? BundleWorkdirPath { get; init; }
}
