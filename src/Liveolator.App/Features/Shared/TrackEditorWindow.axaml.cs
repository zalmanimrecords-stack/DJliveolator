using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Shared;

public partial class TrackEditorWindow : Window
{
    private sealed record KeyOption(string Code, string Label);

    public TrackEditorWindow()
    {
        InitializeComponent();
        KeyBox.ItemsSource = Enumerable.Range(1, 12)
            .SelectMany(number => new[] { $"{number}A", $"{number}B" })
            .Select(code =>
            {
                Camelot.TryToMusicalKey(code, out MusicalKey? key);
                return new KeyOption(code, $"{code}  ·  {key!.Name}");
            })
            .ToArray();
    }

    public void Load(MusicTrack track)
    {
        BpmBox.Text = track.Bpm?.Bpm.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        KeyBox.SelectedItem = (KeyBox.ItemsSource as IEnumerable<KeyOption>)?
            .FirstOrDefault(option => option.Code == track.Key?.Camelot);
        GenreBox.Text = track.Metadata?.Genre ?? string.Empty;
        NotesBox.Text = track.Metadata?.Comment ?? string.Empty;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!double.TryParse(
                BpmBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm)
            || bpm <= 0)
        {
            ErrorText.Text = "Enter a positive BPM.";
            return;
        }

        string? camelot = (KeyBox.SelectedItem as KeyOption)?.Code;
        if (!Camelot.TryToMusicalKey(camelot, out _))
        {
            ErrorText.Text = "Choose a key.";
            return;
        }

        Close(new TrackEditResult(bpm, camelot!, GenreBox.Text, NotesBox.Text));
    }
}
