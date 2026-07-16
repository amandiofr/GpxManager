using CommunityToolkit.Mvvm.ComponentModel;
using GpxManager.Models;

namespace GpxManager.ViewModels;

public partial class TrackViewModel : ObservableObject
{
    public Track Track { get; }

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private string _name;

    public TrackViewModel(Track track, int number)
    {
        Track   = track;
        _number = number;
        _name   = track.Name;
    }

    public int PointCount => Track.Points.Count;
    public string Distance     => $"{Track.DistanceKm:F2} km";
    public string ElevationGain => Track.ElevationGainM is { } g ? $"+{g:F0} m" : "N/A";
    public string ElevationLoss => Track.ElevationLossM is { } l ? $"-{l:F0} m" : "N/A";
    public string ElevationMin  => Track.ElevationMinM  is { } m ? $"{m:F0} m" : "N/A";
    public string ElevationMax  => Track.ElevationMaxM  is { } m ? $"{m:F0} m" : "N/A";

    public string Duration
    {
        get
        {
            var pts = Track.Points;
            if (pts.Count < 2 || pts[0].Time is null || pts[^1].Time is null) return "N/A";
            var d = pts[^1].Time!.Value - pts[0].Time!.Value;
            return $"{(int)d.TotalHours}h{d.Minutes:D2}";
        }
    }

    public string StartTime =>
        Track.Points.FirstOrDefault()?.Time?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";

    public string EndTime =>
        Track.Points.LastOrDefault()?.Time?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";

    public void Refresh()
    {
        OnPropertyChanged(nameof(PointCount));
        OnPropertyChanged(nameof(Distance));
        OnPropertyChanged(nameof(ElevationGain));
        OnPropertyChanged(nameof(ElevationLoss));
        OnPropertyChanged(nameof(ElevationMin));
        OnPropertyChanged(nameof(ElevationMax));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(StartTime));
        OnPropertyChanged(nameof(EndTime));
    }
}
