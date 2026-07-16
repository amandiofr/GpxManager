namespace GpxManager.Models;

public class Track
{
    public string Name { get; init; } = string.Empty;
    public List<TrackPoint> Points { get; init; } = [];

    public double DistanceKm
    {
        get
        {
            double total = 0;
            for (int i = 1; i < Points.Count; i++)
                total += Haversine(Points[i - 1], Points[i]);
            return total;
        }
    }

    public double? ElevationGainM
    {
        get
        {
            if (Points.All(p => p.Elevation == null)) return null;
            double gain = 0;
            for (int i = 1; i < Points.Count; i++)
                if (Points[i].Elevation > Points[i - 1].Elevation)
                    gain += Points[i].Elevation!.Value - Points[i - 1].Elevation!.Value;
            return gain;
        }
    }

    public double? ElevationMinM
    {
        get
        {
            var pts = Points.Where(p => p.Elevation.HasValue).Select(p => p.Elevation!.Value).ToList();
            return pts.Count > 0 ? pts.Min() : null;
        }
    }

    public double? ElevationMaxM
    {
        get
        {
            var pts = Points.Where(p => p.Elevation.HasValue).Select(p => p.Elevation!.Value).ToList();
            return pts.Count > 0 ? pts.Max() : null;
        }
    }

    public double? ElevationLossM
    {
        get
        {
            if (Points.All(p => p.Elevation == null)) return null;
            double loss = 0;
            for (int i = 1; i < Points.Count; i++)
                if (Points[i].Elevation < Points[i - 1].Elevation)
                    loss += Points[i - 1].Elevation!.Value - Points[i].Elevation!.Value;
            return loss;
        }
    }

    private static double Haversine(TrackPoint a, TrackPoint b)
    {
        const double R = 6371;
        double dLat = ToRad(b.Latitude - a.Latitude);
        double dLon = ToRad(b.Longitude - a.Longitude);
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(ToRad(a.Latitude)) * Math.Cos(ToRad(b.Latitude))
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Asin(Math.Sqrt(h));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}
