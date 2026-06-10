using System.ComponentModel;
using System.Windows.Media.Imaging;
using IPlaySpeed.App.Native;
using IPlaySpeed.Core;

namespace IPlaySpeed.App.ViewModels;

/// <summary>UI에 한 게임을 표시하기 위한 관찰 가능한 행 모델.</summary>
public sealed class GameRow : INotifyPropertyChanged
{
    public GameEntry Entry { get; }
    public GameDailyState State { get; }

    public GameRow(GameEntry entry, GameDailyState state)
    {
        Entry = entry;
        State = state;
        Icon = IconExtractor.LoadImage(entry.IconPath);
    }

    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public BitmapImage? Icon { get; }

    private GameStatus _status = GameStatus.NotStarted;
    public GameStatus Status
    {
        get => _status;
        private set { if (_status != value) { _status = value; OnChanged(nameof(Status)); OnChanged(nameof(StatusText)); OnChanged(nameof(IsCompleted)); } }
    }

    public bool IsCompleted => Status == GameStatus.Completed;

    public string StatusText => Status switch
    {
        GameStatus.Completed => "완료",
        GameStatus.InProgress => $"진행 중 ({Minutes}분)",
        GameStatus.UpdateOnly => "업데이트만 함 · 미완료",
        _ => "대기"
    };

    public int Minutes => State.ActivePlaySeconds / 60;

    /// <summary>완료로 인정할 최소 플레이 시간(분). UI에서 게임별로 수정 가능.</summary>
    public int ThresholdMinutes
    {
        get => Entry.ThresholdMinutes;
        set
        {
            int v = System.Math.Clamp(value, 1, 600);
            if (Entry.ThresholdMinutes != v)
            {
                Entry.ThresholdMinutes = v;
                OnChanged(nameof(ThresholdMinutes));
                OnChanged(nameof(StatusText));
            }
        }
    }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (_isCurrent != value) { _isCurrent = value; OnChanged(nameof(IsCurrent)); } }
    }

    /// <summary>엔진이 매 틱마다 호출해 상태/시간 표시를 갱신.</summary>
    public void Refresh()
    {
        Status = CompletionJudge.Judge(State, Entry.ThresholdMinutes);
        OnChanged(nameof(Minutes));
        OnChanged(nameof(StatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
