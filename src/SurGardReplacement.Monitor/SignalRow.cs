using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SurGardReplacement.Monitor;

internal sealed class SignalRow : INotifyPropertyChanged
{
    private string _delivery = "În așteptare";
    private string _delay = string.Empty;

    public required long Number { get; init; }
    public required string MessageId { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required string Protocol { get; init; }
    public required string ContactId { get; init; }
    public required string Source { get; init; }

    public string Id => ContactId.Length >= 4 ? ContactId[..4] : ContactId;
    public string DateAndTime => ReceivedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    public string Delay
    {
        get => _delay;
        set
        {
            if (_delay == value)
                return;
            _delay = value;
            OnPropertyChanged();
        }
    }

    public string Delivery
    {
        get => _delivery;
        set
        {
            if (_delivery == value)
                return;
            _delivery = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
