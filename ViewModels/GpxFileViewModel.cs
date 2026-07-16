using Microsoft.Win32;
using BruTile.Predefined;
using BruTile.Web;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GpxManager.Models;
using GpxManager.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using NetTopologySuite.Geometries;
using System.Collections.ObjectModel;
using System.Globalization;

namespace GpxManager.ViewModels;

public enum MapSource { Map, Satellite, Topo }

public partial class GpxFileViewModel : ObservableObject
{
    public GpxFile File { get; private set; }
    public string TabTitle => IsDirty ? $"* {File.FileName}" : File.FileName;
    public Mapsui.Map Map { get; }
    public MRect? TrackExtent { get; }

    private XDocument? _pendingDoc;
    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TabTitle));
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private XDocument GetWorkingDoc()
    {
        _pendingDoc ??= GpxParser.LoadXml(File.FilePath);
        return _pendingDoc;
    }

    private void MarkDirty()
    {
        _gpxText = null;
        IsDirty   = true;
        OnPropertyChanged(nameof(GpxText));
        OnPropertyChanged(nameof(HasStrippableTags));
    }

    private bool CanSave() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_pendingDoc == null) return;

        string path = File.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            var dlg = new SaveFileDialog { Filter = "GPX|*.gpx", FileName = File.FileName };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
            File = new GpxFile { FilePath = path, FileName = System.IO.Path.GetFileName(path) };
            OnPropertyChanged(nameof(TabTitle));
        }

        try
        {
            _pendingDoc.Save(path);
            _pendingDoc = null;
            _gpxText    = null;
            IsDirty     = false;
            OnPropertyChanged(nameof(GpxText));
            OnPropertyChanged(nameof(HasStrippableTags));
            // La version simplifiée devient le nouvel état de référence
            _simplifyCache.Clear();
            _suppressSimplify = true;
            IsSimplified = false;
            _suppressSimplify = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'enregistrement :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [ObservableProperty]
    private TrackViewModel? _selectedTrack;

    private static readonly double[] SimplifySteps = [1.0, 3.0, 10.0, 20.0];

    [ObservableProperty]
    private int _simplifyStep = 1; // défaut = 3 m

    [ObservableProperty]
    private double _simplifyEpsilon = 3.0;

    partial void OnSimplifyStepChanged(int value)
    {
        SimplifyEpsilon = SimplifySteps[Math.Clamp(value, 0, SimplifySteps.Length - 1)];
    }

    [ObservableProperty]
    private bool _isSimplified = false;

    private readonly Dictionary<Track, List<TrackPoint>> _simplifyCache = new();
    private bool _suppressSimplify;

    partial void OnIsSimplifiedChanged(bool value)
    {
        if (_suppressSimplify) return;
        if (value)
        {
            ApplySimplify();
        }
        else
        {
            foreach (var vm in Tracks)
            {
                if (_simplifyCache.TryGetValue(vm.Track, out var original))
                    UpdateSimplifiedPoints(vm, original);
            }
            _simplifyCache.Clear();
            RefreshTrackLayers();
        }
    }

    partial void OnSimplifyEpsilonChanged(double value)
    {
        if (IsSimplified) ApplySimplify();
    }

    private void ApplySimplify()
    {
        foreach (var vm in Tracks)
        {
            var track = vm.Track;
            if (!_simplifyCache.ContainsKey(track))
                _simplifyCache[track] = track.Points.ToList();
            UpdateSimplifiedPoints(vm, RdpSimplify(_simplifyCache[track], SimplifyEpsilon));
        }
        RefreshTrackLayers();
        MarkDirty();
    }

    private void UpdateSimplifiedPoints(TrackViewModel vm, List<TrackPoint> pts)
    {
        try
        {
            var doc = GetWorkingDoc();
            var ns  = doc.Root!.Name.Namespace;
            int idx = Tracks.IndexOf(vm);
            var trkElem = doc.Root.Elements(ns + "trk").ElementAt(idx);
            trkElem.Elements(ns + "trkseg").Remove();
            trkElem.Add(new XElement(ns + "trkseg",
                pts.Select(p =>
                {
                    var pt = new XElement(ns + "trkpt",
                        new XAttribute("lat", p.Latitude.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("lon", p.Longitude.ToString(CultureInfo.InvariantCulture)));
                    if (p.Elevation.HasValue)
                        pt.Add(new XElement(ns + "ele", p.Elevation.Value.ToString(CultureInfo.InvariantCulture)));
                    if (p.Time.HasValue)
                        pt.Add(new XElement(ns + "time", p.Time.Value.ToString("O")));
                    return pt;
                })));
        }
        catch { return; }
        _coordsCache.Remove(vm.Track);
        vm.Track.Points.Clear();
        vm.Track.Points.AddRange(pts);
        vm.Refresh();
    }

    [ObservableProperty]
    private bool _isEraserMode;

    partial void OnIsEraserModeChanged(bool value) { if (value) IsSplitMode = false; }

    [ObservableProperty]
    private bool _isSplitMode;

    partial void OnIsSplitModeChanged(bool value) { if (value) IsEraserMode = false; }

    [ObservableProperty]
    private bool _isPeloteMode;

    partial void OnIsPeloteModeChanged(bool value) => RefreshPeloteLayer();

    private ILayer? _peloteLayer;
    private ILayer? _peloteSegmentLayer;

    private static double TurnAngle(IList<TrackPoint> pts, int a, int b, int c)
    {
        double dx1 = pts[b].Longitude - pts[a].Longitude, dy1 = pts[b].Latitude - pts[a].Latitude;
        double dx2 = pts[c].Longitude - pts[b].Longitude, dy2 = pts[c].Latitude - pts[b].Latitude;
        double len = Math.Sqrt((dx1*dx1 + dy1*dy1) * (dx2*dx2 + dy2*dy2));
        if (len < 1e-15) return 0;
        return Math.Acos(Math.Clamp((dx1*dx2 + dy1*dy2) / len, -1.0, 1.0));
    }

    private static double DistM(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double mlat = (lat1 + lat2) / 2 * Math.PI / 180;
        double dx   = dLon * Math.Cos(mlat) * 6371000;
        double dy   = dLat * 6371000;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private List<(TrackViewModel track, int iStart, int iEnd, double cX, double cY, double radiusWorld, int count)> DetectPelotes()
    {
        const double maxSpreadM = 60;
        const double minPathM   = 80;
        const int    minCount   = 8;

        var result = new List<(TrackViewModel, int, int, double, double, double, int)>();

        foreach (var trackVm in Tracks)
        {
            var pts = trackVm.Track.Points;
            int i = 0;
            while (i < pts.Count)
            {
                double minLat = pts[i].Latitude,  maxLat = pts[i].Latitude;
                double minLon = pts[i].Longitude, maxLon = pts[i].Longitude;
                int j = i + 1;

                while (j < pts.Count)
                {
                    double nMinLat = Math.Min(minLat, pts[j].Latitude);
                    double nMaxLat = Math.Max(maxLat, pts[j].Latitude);
                    double nMinLon = Math.Min(minLon, pts[j].Longitude);
                    double nMaxLon = Math.Max(maxLon, pts[j].Longitude);
                    if (DistM(nMinLat, nMinLon, nMaxLat, nMaxLon) > maxSpreadM) break;
                    minLat = nMinLat; maxLat = nMaxLat;
                    minLon = nMinLon; maxLon = nMaxLon;
                    j++;
                }

                int count = j - i;
                if (count >= minCount)
                {
                    double path = 0;
                    for (int k = i + 1; k < j; k++)
                        path += DistM(pts[k-1].Latitude, pts[k-1].Longitude,
                                      pts[k].Latitude,   pts[k].Longitude);

                    if (path >= minPathM)
                    {
                        // Rogner les extrémités rectilignes : avancer iCore tant que l'angle
                        // de changement de direction est inférieur au seuil (segment d'approche droit)
                        const double minTurnRad = Math.PI / 6; // 30°
                        int iCore = i, jCore = j;

                        while (iCore + 2 < jCore && jCore - iCore > minCount)
                        {
                            if (TurnAngle(pts, iCore, iCore + 1, iCore + 2) >= minTurnRad) break;
                            iCore++;
                        }
                        while (jCore - 3 >= iCore && jCore - iCore > minCount)
                        {
                            if (TurnAngle(pts, jCore - 3, jCore - 2, jCore - 1) >= minTurnRad) break;
                            jCore--;
                        }

                        // Centroïde = centre de la boîte englobante du noyau [iCore, jCore)
                        double cMinLat = pts[iCore].Latitude,  cMaxLat = pts[iCore].Latitude;
                        double cMinLon = pts[iCore].Longitude, cMaxLon = pts[iCore].Longitude;
                        for (int k = iCore + 1; k < jCore; k++)
                        {
                            cMinLat = Math.Min(cMinLat, pts[k].Latitude);
                            cMaxLat = Math.Max(cMaxLat, pts[k].Latitude);
                            cMinLon = Math.Min(cMinLon, pts[k].Longitude);
                            cMaxLon = Math.Max(cMaxLon, pts[k].Longitude);
                        }
                        var (xSW, ySW) = SphericalMercator.FromLonLat(cMinLon, cMinLat);
                        var (xNE, yNE) = SphericalMercator.FromLonLat(cMaxLon, cMaxLat);
                        double cX = (xSW + xNE) / 2, cY = (ySW + yNE) / 2;

                        double maxR = 0;
                        for (int k = iCore; k < jCore; k++)
                        {
                            var (wx, wy) = SphericalMercator.FromLonLat(pts[k].Longitude, pts[k].Latitude);
                            double ddx = wx - cX, ddy = wy - cY;
                            maxR = Math.Max(maxR, Math.Sqrt(ddx * ddx + ddy * ddy));
                        }

                        result.Add((trackVm, iCore, jCore, cX, cY, Math.Max(maxR * 1.3, 20), jCore - iCore));
                        i = j;
                        continue;
                    }
                }
                i++;
            }
        }

        return result;
    }

    private void RefreshPeloteLayer()
    {
        if (_peloteSegmentLayer != null) { Map.Layers.Remove(_peloteSegmentLayer); _peloteSegmentLayer = null; }
        if (_peloteLayer        != null) { Map.Layers.Remove(_peloteLayer);        _peloteLayer        = null; }
        if (!IsPeloteMode) return;

        var pelotes = DetectPelotes();
        if (pelotes.Count == 0) return;

        // Layer 1 : segments de la pelote surlignés en orange
        var segFeatures = pelotes.Select(p =>
        {
            var tpts = p.track.Track.Points;
            var coords = Enumerable.Range(p.iStart, p.iEnd - p.iStart)
                .Select(k =>
                {
                    var (wx, wy) = SphericalMercator.FromLonLat(tpts[k].Longitude, tpts[k].Latitude);
                    return new Coordinate(wx, wy);
                })
                .ToArray();
            return (IFeature)new GeometryFeature(new LineString(coords));
        }).ToList();

        _peloteSegmentLayer = new MemoryLayer
        {
            Name     = "PeloteSegments",
            Features = segFeatures,
            Style    = new VectorStyle { Line = new Pen(Color.Orange, 4) }
        };
        Map.Layers.Add(_peloteSegmentLayer);

        // Layer 2 : cercle centré sur la pelote
        var features = pelotes.Select(p =>
            (IFeature)new PointFeature(new MPoint(p.cX, p.cY))
        ).ToList();

        _peloteLayer = new MemoryLayer
        {
            Name     = "Pelotes",
            Features = features,
            Style    = new ImageStyle
            {
                Image       = "embedded://GpxManager.Assets.pelote.svg",
                SymbolScale = 1.0
            }
        };
        Map.Layers.Add(_peloteLayer);
    }

    public void SplitTrack(TrackViewModel trackVm, int splitIndex)
    {
        var points = trackVm.Track.Points;
        if (splitIndex <= 0 || splitIndex >= points.Count - 1) return;

        var baseName = trackVm.Name;
        var track1 = new Track { Name = $"{baseName} (1)", Points = points.Take(splitIndex + 1).ToList() };
        var track2 = new Track { Name = $"{baseName} (2)", Points = points.Skip(splitIndex).ToList() };

        // XML
        var doc = GetWorkingDoc();
        var ns  = doc.Root!.Name.Namespace;
        var trkElements = doc.Root.Elements(ns + "trk").ToList();
        var original    = trkElements[trackVm.Number - 1];

        original.AddAfterSelf(BuildTrkElement(track2, ns));
        original.AddAfterSelf(BuildTrkElement(track1, ns));
        original.Remove();

        // ViewModel
        int insertIdx = Tracks.IndexOf(trackVm);
        Tracks.Remove(trackVm);
        Tracks.Insert(insertIdx,     new TrackViewModel(track1, insertIdx + 1));
        Tracks.Insert(insertIdx + 1, new TrackViewModel(track2, insertIdx + 2));
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Number = i + 1;

        IsSplitMode = false;
        MarkDirty();
        RefreshTrackLayers();
    }

    private static XElement BuildTrkElement(Track track, XNamespace ns)
    {
        var seg = new XElement(ns + "trkseg");
        foreach (var pt in track.Points)
        {
            var trkpt = new XElement(ns + "trkpt",
                new XAttribute("lat", pt.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("lon", pt.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (pt.Elevation.HasValue)
                trkpt.Add(new XElement(ns + "ele", pt.Elevation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (pt.Time.HasValue)
                trkpt.Add(new XElement(ns + "time", pt.Time.Value.ToString("o")));
            seg.Add(trkpt);
        }
        return new XElement(ns + "trk", new XElement(ns + "name", track.Name), seg);
    }

    public void EraseZone(MRect worldRect)
    {
        var (minLon, minLat) = SphericalMercator.ToLonLat(worldRect.MinX, worldRect.MinY);
        var (maxLon, maxLat) = SphericalMercator.ToLonLat(worldRect.MaxX, worldRect.MaxY);

        bool InZone(double lat, double lon) =>
            lat >= minLat && lat <= maxLat && lon >= minLon && lon <= maxLon;

        bool InZoneEl(XElement e)
        {
            if (!double.TryParse(e.Attribute("lat")?.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lat)) return false;
            if (!double.TryParse(e.Attribute("lon")?.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lon)) return false;
            return InZone(lat, lon);
        }

        var doc = GetWorkingDoc();
        var ns  = doc.Root!.Name.Namespace;

        var trkPtsToRemove = doc.Descendants(ns + "trkpt").Where(InZoneEl).ToList();
        var wptToRemove    = doc.Descendants(ns + "wpt"  ).Where(InZoneEl).ToList();

        if (trkPtsToRemove.Count == 0 && wptToRemove.Count == 0) return;

        trkPtsToRemove.ForEach(e => e.Remove());

        foreach (var trackVm in Tracks)
        {
            int before = trackVm.Track.Points.Count;
            trackVm.Track.Points.RemoveAll(p => InZone(p.Latitude, p.Longitude));
            if (trackVm.Track.Points.Count != before)
            {
                _coordsCache.Remove(trackVm.Track);
                trackVm.Refresh();
            }
        }

        if (wptToRemove.Count > 0)
        {
            wptToRemove.ForEach(e => e.Remove());
            var removedWpts = Waypoints.Where(w => InZone(w.Latitude, w.Longitude)).ToList();
            removedWpts.ForEach(w => Waypoints.Remove(w));
            RefreshWaypointLayer();
        }

        MarkDirty();
        RefreshTrackLayers();
        if (IsPeloteMode) RefreshPeloteLayer();
    }

    private void RefreshWaypointLayer()
    {
        if (_waypointLayer != null)
        {
            Map.Layers.Remove(_waypointLayer);
            _waypointLayer = null;
        }
        WaypointPositions = [];
        BuildWaypointLayer(Map);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMapMode))]
    [NotifyPropertyChangedFor(nameof(IsSatelliteMode))]
    [NotifyPropertyChangedFor(nameof(IsTopoMode))]
    private MapSource _selectedMapSource = MapSource.Map;

    public bool IsMapMode
    {
        get => SelectedMapSource == MapSource.Map;
        set { if (value) SelectedMapSource = MapSource.Map; }
    }

    public bool IsSatelliteMode
    {
        get => SelectedMapSource == MapSource.Satellite;
        set { if (value) SelectedMapSource = MapSource.Satellite; }
    }

    public bool IsTopoMode
    {
        get => SelectedMapSource == MapSource.Topo;
        set { if (value) SelectedMapSource = MapSource.Topo; }
    }

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];
    public ObservableCollection<Waypoint> Waypoints { get; } = [];

    public Action? CloseRequested { get; set; }

    private string? _gpxText;
    public string GpxText => _gpxText ??= LoadGpxText();

    private string LoadGpxText()
    {
        if (_pendingDoc != null)
        {
            using var sw = new System.IO.StringWriter();
            _pendingDoc.Save(sw);
            return sw.ToString();
        }
        try { return System.IO.File.ReadAllText(File.FilePath); }
        catch { return "(impossible de lire le fichier)"; }
    }

    public HashSet<TrackViewModel> SelectedTracks { get; } = [];
    public event Action<IReadOnlyList<TrackViewModel>, TrackViewModel?>? SelectionUpdated;

    private ILayer _bgLayer = null!;
    private ILayer? _waypointLayer;
    private readonly List<ILayer> _trackLineLayers = [];
    private readonly List<ILayer> _arrowLayers     = [];
    private readonly Dictionary<ILayer, TrackViewModel> _layerToTrack  = [];
    private readonly Dictionary<Track, Coordinate[]>    _coordsCache   = [];
    private double _arrowSpacingWorld = 5000;  // mis à jour par SetArrowSpacing

    public ILayer? WaypointLayer => _waypointLayer;
    public IReadOnlyList<(MPoint WorldPos, Waypoint Waypoint)> WaypointPositions { get; private set; } = [];

    public GpxFileViewModel(GpxFile file, XDocument? initialDoc = null, bool isDirty = false)
    {
        File        = file;
        _pendingDoc = initialDoc;
        _isDirty    = isDirty;
        foreach (var (track, i) in file.Tracks.Select((t, i) => (t, i)))
            Tracks.Add(new TrackViewModel(track, i + 1));
        foreach (var wp in file.Waypoints)
            Waypoints.Add(wp);

        TrackExtent = ComputeExtent();
        Map = BuildMap();
    }

    // Constantes exposées pour la détection de survol dans MapView
    public const double WptSvgWidth  = 24.0;
    public const double WptSvgHeight = 36.0;
    public const double WptScale     = 0.7;
    public const double WptOffsetY   = WptSvgHeight / 2;   // 18 px, convention Y-up Mapsui

    private static readonly string PinUri =
        "embedded://GpxManager.Assets.pin.svg";

    private void BuildWaypointLayer(Mapsui.Map map)
    {
        if (Waypoints.Count == 0) return;

        var positions = new List<(MPoint, Waypoint)>();
        var features  = new List<IFeature>();

        foreach (var wp in Waypoints)
        {
            var (x, y) = SphericalMercator.FromLonLat(wp.Longitude, wp.Latitude);
            positions.Add((new MPoint(x, y), wp));
            features.Add(new GeometryFeature(new NetTopologySuite.Geometries.Point(
                new NetTopologySuite.Geometries.Coordinate(x, y))));
        }

        WaypointPositions = positions;
        _waypointLayer = new MemoryLayer
        {
            Name  = "Waypoints",
            Features = features,
            Style = new ImageStyle
            {
                Image       = PinUri,
                SymbolScale = 0.7,
                Offset      = new Offset(0, WptOffsetY)  // Y-up : centre au-dessus, pointe sur le point géo
            }
        };
        map.Layers.Add(_waypointLayer);
    }

    partial void OnSelectedMapSourceChanged(MapSource value) => SwapBackgroundLayer();

    private MRect? ComputeExtent()
    {
        var pts = File.Tracks.SelectMany(t => t.Points).ToList();
        if (pts.Count == 0) return null;
        var (minX, minY) = SphericalMercator.FromLonLat(pts.Min(p => p.Longitude), pts.Min(p => p.Latitude));
        var (maxX, maxY) = SphericalMercator.FromLonLat(pts.Max(p => p.Longitude), pts.Max(p => p.Latitude));
        var raw = new MRect(minX, minY, maxX, maxY);
        return raw.Grow(Math.Max(raw.Width, raw.Height) * 0.15 + 500);
    }

    private static readonly Mapsui.Styles.Color ColorRed  = new(210, 0, 0);
    private static readonly Mapsui.Styles.Color ColorBlue = new(30, 100, 220);

    private Mapsui.Map BuildMap()
    {
        var map = new Mapsui.Map();
        _bgLayer = OpenStreetMap.CreateTileLayer("GpxManager");
        map.Layers.Add(_bgLayer);

        // Étend le zoom jusqu'au niveau 22 (OSM natif = 19) pour édition précise
        const double res0 = 156543.0337;
        map.Navigator.OverrideResolutions = Enumerable.Range(0, 23)
            .Select(z => res0 / Math.Pow(2, z))
            .OrderByDescending(r => r)
            .ToList();

        RefreshTrackLayers(map);
        BuildWaypointLayer(map);
        return map;
    }

    // Appelé sur changement de sélection
    private void RefreshTrackLayers(Mapsui.Map? target = null)
    {
        var map = target ?? Map;

        // Supprime lignes ET flèches
        foreach (var l in _trackLineLayers) map.Layers.Remove(l);
        foreach (var l in _arrowLayers)     map.Layers.Remove(l);
        _trackLineLayers.Clear();
        _arrowLayers.Clear();
        _layerToTrack.Clear();

        void AddTrack(TrackViewModel trackVm, bool selected)
        {
            if (trackVm.Track.Points.Count < 2) return;
            var lineLayer = BuildLineLayer(trackVm.Track, selected);
            map.Layers.Add(lineLayer);
            _trackLineLayers.Add(lineLayer);
            _layerToTrack[lineLayer] = trackVm;
        }

        foreach (var t in Tracks.Where(t => !SelectedTracks.Contains(t))) AddTrack(t, false);
        foreach (var t in SelectedTracks)                                  AddTrack(t, true);

        RefreshArrowLayers(map);
    }

    // Appelé sur changement de zoom/taille de la carte
    public void SetArrowSpacing(double viewportWidthPx, double resolution)
    {
        _arrowSpacingWorld = viewportWidthPx / 4.0 * resolution;
        RefreshArrowLayers(Map);
    }

    private void RefreshArrowLayers(Mapsui.Map map)
    {
        foreach (var l in _arrowLayers) map.Layers.Remove(l);
        _arrowLayers.Clear();

        void AddArrows(TrackViewModel trackVm, bool selected)
        {
            if (trackVm.Track.Points.Count < 2) return;
            var coords = GetCoords(trackVm.Track);
            var layer  = BuildArrowLayer(coords, selected ? ColorBlue : ColorRed);
            map.Layers.Add(layer);
            _arrowLayers.Add(layer);
        }

        foreach (var t in Tracks.Where(t => !SelectedTracks.Contains(t))) AddArrows(t, false);
        foreach (var t in SelectedTracks)                                  AddArrows(t, true);

        // Waypoints toujours au-dessus
        if (_waypointLayer != null)
        {
            map.Layers.Remove(_waypointLayer);
            map.Layers.Add(_waypointLayer);
        }
    }

    private Coordinate[] GetCoords(Track track)
    {
        if (!_coordsCache.TryGetValue(track, out var c))
        {
            c = track.Points
                .Select(p => SphericalMercator.FromLonLat(p.Longitude, p.Latitude))
                .Select(pt => new Coordinate(pt.x, pt.y))
                .ToArray();
            _coordsCache[track] = c;
        }
        return c;
    }

    public void MoveTrack(TrackViewModel track, int newIndex)
    {
        int old = Tracks.IndexOf(track);
        if (old < 0 || old == newIndex) return;
        int moveIndex = old < newIndex ? newIndex - 1 : newIndex;

        // Réordonner les <trk> dans le document de travail
        var doc = GetWorkingDoc();
        var ns  = doc.Root!.Name.Namespace;
        var elements = doc.Root.Elements(ns + "trk").ToList();
        var el = elements[old];
        elements.RemoveAt(old);
        elements.Insert(moveIndex, el);
        foreach (var e in doc.Root.Elements(ns + "trk").ToList()) e.Remove();
        foreach (var e in elements) doc.Root.Add(e);

        Tracks.Move(old, Math.Clamp(moveIndex, 0, Tracks.Count - 1));
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Number = i + 1;
        MarkDirty();
        RefreshTrackLayers();
    }

    public void SetSelection(IList<TrackViewModel> all, TrackViewModel? primary)
    {
        SelectedTracks.Clear();
        foreach (var t in all) SelectedTracks.Add(t);
        SelectedTrack = primary;
        SelectionUpdated?.Invoke([.. SelectedTracks], primary);
        RefreshTrackLayers();
        JoinTracksCommand.NotifyCanExecuteChanged();
    }

    private bool CanJoinTracks() => SelectedTracks.Count == 2;

    [RelayCommand(CanExecute = nameof(CanJoinTracks))]
    private void JoinTracks()
    {
        var ordered  = SelectedTracks.OrderBy(t => Tracks.IndexOf(t)).ToList();
        var firstVm  = ordered[0];
        var secondVm = ordered[1];
        int firstIdx  = Tracks.IndexOf(firstVm);
        int secondIdx = Tracks.IndexOf(secondVm);

        var mergedName = $"{firstVm.Name} + {secondVm.Name}";

        try
        {
            var doc     = GetWorkingDoc();
            var ns      = doc.Root!.Name.Namespace;
            var trkList = doc.Root.Elements(ns + "trk").ToList();

            var allSegs = trkList[firstIdx].Elements(ns + "trkseg")
                          .Concat(trkList[secondIdx].Elements(ns + "trkseg"))
                          .Select(s => new XElement(s))
                          .ToList();

            var merged = new XElement(ns + "trk",
                new XElement(ns + "name", mergedName),
                allSegs);

            trkList[firstIdx].ReplaceWith(merged);
            trkList[secondIdx].Remove();
            MarkDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la jointure :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Mise à jour du ViewModel
        _coordsCache.Remove(firstVm.Track);
        _coordsCache.Remove(secondVm.Track);

        var mergedPoints = firstVm.Track.Points.Concat(secondVm.Track.Points).ToList();
        var mergedTrack  = new Track { Name = mergedName, Points = mergedPoints };

        Tracks.Remove(firstVm);
        Tracks.Remove(secondVm);
        Tracks.Insert(firstIdx, new TrackViewModel(mergedTrack, firstIdx + 1));
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Number = i + 1;

        SetSelection([], null);
    }

    private static List<TrackPoint> RdpSimplify(IList<TrackPoint> pts, double epsilonM)
    {
        if (pts.Count <= 2) return [.. pts];
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        RdpRecurse(pts, 0, pts.Count - 1, epsilonM, keep);
        return pts.Where((_, i) => keep[i]).ToList();
    }

    private static void RdpRecurse(IList<TrackPoint> pts, int lo, int hi, double epsilonM, bool[] keep)
    {
        if (hi - lo < 2) return;
        double maxD = 0; int maxI = lo;
        for (int i = lo + 1; i < hi; i++)
        {
            double d = SegDistM(pts[i], pts[lo], pts[hi]);
            if (d > maxD) { maxD = d; maxI = i; }
        }
        if (maxD > epsilonM)
        {
            keep[maxI] = true;
            RdpRecurse(pts, lo, maxI, epsilonM, keep);
            RdpRecurse(pts, maxI, hi, epsilonM, keep);
        }
    }

    private static double SegDistM(TrackPoint p, TrackPoint a, TrackPoint b)
    {
        const double R = 6371000.0;
        double cosLat = Math.Cos(a.Latitude * Math.PI / 180.0);
        double mDeg   = R * Math.PI / 180.0;
        double bx = (b.Longitude - a.Longitude) * cosLat * mDeg;
        double by = (b.Latitude  - a.Latitude)  * mDeg;
        double px = (p.Longitude - a.Longitude) * cosLat * mDeg;
        double py = (p.Latitude  - a.Latitude)  * mDeg;
        double len2 = bx * bx + by * by;
        if (len2 < 1e-10) return Math.Sqrt(px * px + py * py);
        double t  = Math.Clamp((px * bx + py * by) / len2, 0, 1);
        double dx = px - t * bx, dy = py - t * by;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public TrackViewModel? FindTrackByLayer(ILayer layer)
        => _layerToTrack.TryGetValue(layer, out var vm) ? vm : null;

    private static MemoryLayer BuildLineLayer(Track track, bool selected)
    {
        var color = selected ? ColorBlue : ColorRed;
        var coords = track.Points
            .Select(p => SphericalMercator.FromLonLat(p.Longitude, p.Latitude))
            .Select(pt => new Coordinate(pt.x, pt.y))
            .ToArray();
        var line = new GeometryFeature(new LineString(coords));
        line.Styles.Add(new VectorStyle { Line = new Pen(color, selected ? 4 : 3), Fill = null });
        return new MemoryLayer { Name = track.Name, Features = [line], Style = null };
    }

    private MemoryLayer BuildArrowLayer(Coordinate[] coords, Mapsui.Styles.Color color)
    {
        var features = BuildArrowFeatures(coords, color, _arrowSpacingWorld).ToList();
        return new MemoryLayer { Name = "Arrows", Features = features, Style = null };
    }

    private static IEnumerable<IFeature> BuildArrowFeatures(
        Coordinate[] coords, Mapsui.Styles.Color color, double spacingWorld)
    {
        if (coords.Length < 2) yield break;

        double totalLen = 0;
        var segLens = new double[coords.Length - 1];
        for (int i = 0; i < coords.Length - 1; i++)
        {
            var dx = coords[i + 1].X - coords[i].X;
            var dy = coords[i + 1].Y - coords[i].Y;
            segLens[i] = Math.Sqrt(dx * dx + dy * dy);
            totalLen += segLens[i];
        }
        if (totalLen < 10) yield break;

        // Nombre de flèches : au moins 1, espacées de ~spacingWorld
        int count = Math.Max(1, (int)Math.Floor(totalLen / spacingWorld));

        // Si 1 seule flèche : mi-parcours ; sinon répartition uniforme
        double[] targets = count == 1
            ? [totalLen / 2.0]
            : Enumerable.Range(1, count).Select(k => k * totalLen / (count + 1)).ToArray();

        double traveled = 0;
        int ti = 0;
        for (int i = 0; i < coords.Length - 1 && ti < targets.Length; i++)
        {
            double seg = segLens[i];
            while (ti < targets.Length && traveled + seg >= targets[ti])
            {
                double t     = (targets[ti] - traveled) / seg;
                double cx    = coords[i].X + t * (coords[i + 1].X - coords[i].X);
                double cy    = coords[i].Y + t * (coords[i + 1].Y - coords[i].Y);
                double theta = Math.Atan2(coords[i + 1].X - coords[i].X,
                                          coords[i + 1].Y - coords[i].Y);

                var f = new GeometryFeature(
                    new NetTopologySuite.Geometries.Point(new Coordinate(cx, cy)));
                f.Styles.Add(new SymbolStyle
                {
                    Fill           = new Brush(color),
                    Line           = null,
                    SymbolType     = SymbolType.Triangle,
                    SymbolScale    = 0.44,
                    SymbolRotation = theta * 180 / Math.PI
                });
                yield return f;
                ti++;
            }
            traveled += seg;
        }
    }

    private void SwapBackgroundLayer()
    {
        Map.Layers.Remove(_bgLayer);
        _bgLayer = SelectedMapSource switch
        {
            MapSource.Satellite => new TileLayer(new HttpTileSource(
                new GlobalSphericalMercator(),
                "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}")),
            MapSource.Topo => new TileLayer(new HttpTileSource(
                new GlobalSphericalMercator(),
                "https://services.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}")),
            _ => OpenStreetMap.CreateTileLayer("GpxManager")
        };
        Map.Layers.Insert(0, _bgLayer);
    }

    private static readonly HashSet<string> TagsToStrip =
        ["extensions", "geotracker", "metadata"];

    public bool HasStrippableTags => TagsToStrip.Any(tag =>
        GpxText.Contains($"<{tag}", StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void Epurer()
    {
        var confirm = MessageBox.Show(
            $"Supprimer les balises suivantes de :\n{File.FileName}\n\n"
            + string.Join("\n", TagsToStrip.Select(t => $"  • <{t}>")),
            "Épurer le GPX",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var doc = GetWorkingDoc();

            doc.Descendants()
               .Where(e => TagsToStrip.Contains(e.Name.LocalName))
               .ToList()
               .ForEach(e => e.Remove());

            doc.Root!.Attributes()
               .Where(a => a.Name.LocalName != "version")
               .ToList()
               .ForEach(a => a.Remove());

            MarkDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'épuration :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void RenameTrack(TrackViewModel trackVm)
    {
        var dialog = new Window
        {
            Title = "Renommer la trace",
            Width = 360, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        var tb = new TextBox { Text = trackVm.Name };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok     = new Button { Content = "OK",      Width = 75, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Annuler", Width = 75, IsCancel = true };
        ok.Click += (_, _) => dialog.DialogResult = true;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        sp.Children.Add(new TextBlock { Text = "Nom de la trace :", Margin = new Thickness(0, 0, 0, 6) });
        sp.Children.Add(tb);
        sp.Children.Add(btns);
        dialog.Content = sp;
        dialog.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };

        if (dialog.ShowDialog() != true) return;
        var newName = tb.Text.Trim();
        if (string.IsNullOrEmpty(newName) || newName == trackVm.Name) return;

        try
        {
            var doc = GetWorkingDoc();
            var ns = doc.Root!.Name.Namespace;
            var trkList = doc.Root.Elements(ns + "trk").ToList();
            var idx = trackVm.Number - 1;
            if (idx >= 0 && idx < trkList.Count)
            {
                var nameEl = trkList[idx].Element(ns + "name");
                if (nameEl != null) nameEl.Value = newName;
                else trkList[idx].AddFirst(new XElement(ns + "name", newName));
                MarkDirty();
            }
            trackVm.Name = newName;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors du renommage :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static List<Track> _trackClipboard = [];

    private static Track CloneTrack(Track src) => new()
    {
        Name   = src.Name,
        Points = src.Points.Select(p => new TrackPoint
        {
            Latitude  = p.Latitude,
            Longitude = p.Longitude,
            Elevation = p.Elevation,
            Time      = p.Time
        }).ToList()
    };

    [RelayCommand]
    private void CopyTracks(TrackViewModel trackVm)
    {
        var toCopy = SelectedTracks.Contains(trackVm) && SelectedTracks.Count > 1
            ? SelectedTracks.ToList()
            : [trackVm];
        _trackClipboard = toCopy.Select(vm => CloneTrack(vm.Track)).ToList();
        PasteTracksCommand.NotifyCanExecuteChanged();
    }

    private static bool CanPasteTracks() => _trackClipboard.Count > 0;

    [RelayCommand(CanExecute = nameof(CanPasteTracks))]
    private void PasteTracks()
    {
        var doc = GetWorkingDoc();
        var ns  = doc.Root!.Name.Namespace;

        foreach (var src in _trackClipboard)
        {
            var copy = CloneTrack(src);
            doc.Root!.Add(BuildTrkElement(copy, ns));
            Tracks.Add(new TrackViewModel(copy, Tracks.Count + 1));
        }

        MarkDirty();
        RefreshTrackLayers();
    }

    [RelayCommand]
    private void DeleteTrack(TrackViewModel trackVm)
    {
        var result = MessageBox.Show(
            $"Supprimer la trace \"{trackVm.Name}\" ?",
            "Supprimer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        var doc = GetWorkingDoc();
        var ns  = doc.Root!.Name.Namespace;
        doc.Root.Elements(ns + "trk").ToList()[trackVm.Number - 1].Remove();

        Tracks.Remove(trackVm);
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Number = i + 1;

        MarkDirty();
        RefreshTrackLayers();
    }

    [RelayCommand]
    private void Close()
    {
        if (IsDirty)
        {
            var result = System.Windows.MessageBox.Show(
                $"« {File.FileName} » a des modifications non sauvegardées.\n\nFermer quand même ?",
                "Modifications non sauvegardées",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
            if (result != System.Windows.MessageBoxResult.Yes) return;
        }
        CloseRequested?.Invoke();
    }
}
