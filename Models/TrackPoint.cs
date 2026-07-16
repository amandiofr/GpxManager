namespace GpxManager.Models;

public class TrackPoint
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? Elevation { get; init; }
    public DateTime? Time { get; init; }
}
