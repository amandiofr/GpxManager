namespace GpxManager.Models;

public class Waypoint
{
    public string Name { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? Elevation { get; init; }
    public string? Description { get; init; }
}
