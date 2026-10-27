using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ObsVoiceAssistant.Overlay;

/// <summary>
/// Состояние окна оверлея. При сетевой ошибке последнее число не стирается,
/// а приглушается; при офлайне окно скрывается.
/// </summary>
public sealed class ViewerCountOverlayViewModel : INotifyPropertyChanged
{
    private string countText = "0";
    private string labelText = "зрителей";
    private bool isVisible;
    private double opacity = 1;
    private int? lastViewers;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CountText
    {
        get => countText;
        private set => Set(ref countText, value);
    }

    public string LabelText
    {
        get => labelText;
        private set => Set(ref labelText, value);
    }

    public bool IsVisible
    {
        get => isVisible;
        private set => Set(ref isVisible, value);
    }

    public double Opacity
    {
        get => opacity;
        private set => Set(ref opacity, value);
    }

    public void Apply(ViewerCountResult result)
    {
        switch (result.State)
        {
            case ViewerCountState.Live:
                lastViewers = Math.Max(0, result.Viewers);
                CountText = ViewerCountFormatter.FormatCount(lastViewers.Value);
                LabelText = ViewerCountFormatter.FormatLabel(lastViewers.Value);
                IsVisible = true;
                Opacity = 1;
                break;

            case ViewerCountState.Offline:
            case ViewerCountState.NotConnected:
                lastViewers = null;
                IsVisible = false;
                Opacity = 1;
                break;

            case ViewerCountState.Unavailable:
                // Если данных ещё не было, показывать «0 зрителей» нельзя:
                // это выглядело бы как факт, а не как отсутствие связи.
                IsVisible = lastViewers.HasValue;
                Opacity = lastViewers.HasValue ? 0.45 : 1;
                break;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
