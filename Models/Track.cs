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
            var s = SmoothedElevations();
            if (s == null) return null;
            double gain = 0;
            for (int i = 1; i < s.Length; i++)
                if (s[i] > s[i - 1]) gain += s[i] - s[i - 1];
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
            var s = SmoothedElevations();
            if (s == null) return null;
            double loss = 0;
            for (int i = 1; i < s.Length; i++)
                if (s[i] < s[i - 1]) loss += s[i - 1] - s[i];
            return loss;
        }
    }

    // Moyenne glissante sur les altitudes (demi-fenêtre = 20 points)
    // pour éliminer le bruit GPS avant de calculer le dénivelé cumulé.
    private double[]? SmoothedElevations(int half = 20)
    {
        if (Points.All(p => p.Elevation == null)) return null;
        var result = new double[Points.Count];
        for (int i = 0; i < Points.Count; i++)
        {
            double sum = 0; int n = 0;
            for (int j = Math.Max(0, i - half); j <= Math.Min(Points.Count - 1, i + half); j++)
            {
                if (Points[j].Elevation.HasValue) { sum += Points[j].Elevation!.Value; n++; }
            }
            result[i] = n > 0 ? sum / n : 0;
        }
        return result;
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
