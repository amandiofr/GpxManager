using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GpxManager.Models;

namespace GpxManager.Services;

public static class GpxParser
{
    private static readonly XNamespace Ns11 = "http://www.topografix.com/GPX/1/1";
    private static readonly XNamespace Ns10 = "http://www.topografix.com/GPX/1/0";

    public static GpxFile Parse(string filePath)
    {
        var doc = LoadXml(filePath);
        var root = doc.Root ?? throw new InvalidDataException("Fichier GPX invalide.");
        var ns = root.Name.Namespace == Ns11 ? Ns11 : Ns10;

        var tracks = root.Elements(ns + "trk")
            .Select(trk => ParseTrack(trk, ns))
            .ToList();

        var waypoints = root.Elements(ns + "wpt")
            .Select(wpt => ParseWaypoint(wpt, ns))
            .ToList();

        var author = root.Element(ns + "metadata")?.Element(ns + "author")?.Element(ns + "name")?.Value
                  ?? root.Element(ns + "author")?.Value;

        return new GpxFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Author = author,
            Tracks = tracks,
            Waypoints = waypoints
        };
    }

    public static XDocument LoadXml(string filePath)
    {
        try
        {
            return XDocument.Load(filePath);
        }
        catch (System.Xml.XmlException)
        {
            // Certains fichiers GPX ont des préfixes non déclarés (ex: geotracker:)
            // On les détecte et on les ajoute comme namespaces fictifs dans la balise racine
            var text = File.ReadAllText(filePath);

            var used = Regex.Matches(text, @"</?([a-zA-Z][a-zA-Z0-9_]*):[\w]")
                           .Cast<Match>().Select(m => m.Groups[1].Value)
                           .Distinct().ToHashSet();
            var declared = Regex.Matches(text, @"xmlns:([a-zA-Z][a-zA-Z0-9_]*)")
                               .Cast<Match>().Select(m => m.Groups[1].Value)
                               .ToHashSet();
            declared.UnionWith(new[] { "xml", "xmlns" });

            var missing = used.Except(declared).ToList();
            if (missing.Count == 0) throw;

            var decls = string.Join(" ", missing.Select(p => $"xmlns:{p}=\"urn:{p}\""));
            var fixed_ = Regex.Replace(text, @"(<gpx\b)", $"$1 {decls}", RegexOptions.IgnoreCase);
            return XDocument.Parse(fixed_);
        }
    }

    private static Track ParseTrack(XElement trk, XNamespace ns)
    {
        var name = trk.Element(ns + "name")?.Value ?? "Sans nom";
        var points = trk.Elements(ns + "trkseg")
            .SelectMany(seg => seg.Elements(ns + "trkpt"))
            .Select(pt => ParseTrackPoint(pt, ns))
            .ToList();

        return new Track { Name = name, Points = points };
    }

    private static TrackPoint ParseTrackPoint(XElement pt, XNamespace ns)
    {
        var lat = double.Parse(pt.Attribute("lat")!.Value, CultureInfo.InvariantCulture);
        var lon = double.Parse(pt.Attribute("lon")!.Value, CultureInfo.InvariantCulture);
        var ele = pt.Element(ns + "ele") is { } e
            ? double.Parse(e.Value, CultureInfo.InvariantCulture)
            : (double?)null;
        var time = pt.Element(ns + "time") is { } t
            ? DateTime.Parse(t.Value, null, DateTimeStyles.RoundtripKind)
            : (DateTime?)null;

        return new TrackPoint { Latitude = lat, Longitude = lon, Elevation = ele, Time = time };
    }

    private static Waypoint ParseWaypoint(XElement wpt, XNamespace ns)
    {
        var lat = double.Parse(wpt.Attribute("lat")!.Value, CultureInfo.InvariantCulture);
        var lon = double.Parse(wpt.Attribute("lon")!.Value, CultureInfo.InvariantCulture);
        var ele = wpt.Element(ns + "ele") is { } e
            ? double.Parse(e.Value, CultureInfo.InvariantCulture)
            : (double?)null;

        return new Waypoint
        {
            Name = wpt.Element(ns + "name")?.Value ?? "Waypoint",
            Latitude = lat,
            Longitude = lon,
            Elevation = ele,
            Description = wpt.Element(ns + "desc")?.Value
        };
    }
}
