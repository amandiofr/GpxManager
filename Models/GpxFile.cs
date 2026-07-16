namespace GpxManager.Models;

public class GpxFile
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string? Author { get; init; }
    public IReadOnlyList<Track> Tracks { get; init; } = [];
    public IReadOnlyList<Waypoint> Waypoints { get; init; } = [];
}
