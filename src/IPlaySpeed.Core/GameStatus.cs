namespace IPlaySpeed.Core;

/// <summary>한 게임의 오늘 진행 상태.</summary>
public enum GameStatus
{
    /// <summary>아직 한 번도 활성 플레이하지 않음.</summary>
    NotStarted,

    /// <summary>플레이 중이나 임계 시간 미달.</summary>
    InProgress,

    /// <summary>폴더가 크게 바뀌었고(업데이트) 플레이는 미달 → 미완료로 표시.</summary>
    UpdateOnly,

    /// <summary>활성 플레이가 임계 시간 이상 → 일일퀘스트 완료.</summary>
    Completed
}
