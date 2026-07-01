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
}
