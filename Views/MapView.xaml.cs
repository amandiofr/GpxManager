using GpxManager.ViewModels;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GpxManager.Views;

public partial class MapView : UserControl
{
    private readonly DispatcherTimer _arrowTimer;

    public MapView()
    {
        InitializeComponent();
        MapCtrl.Loaded      += (_, _) => ZoomToTrack();
        MapCtrl.Info        += OnMapInfo;
        MapCtrl.SizeChanged += (_, _) => ScheduleArrowRefresh();

        _arrowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _arrowTimer.Tick += (_, _) => { _arrowTimer.Stop(); ApplyArrowSpacing(); };
    }

    private void ScheduleArrowRefresh()
    {
        _arrowTimer.Stop();
        _arrowTimer.Start();
    }

    private void ApplyArrowSpacing()
    {
        if (DataContext is not GpxFileViewModel vm) return;
        var map = MapCtrl.Map;
        if (map == null) return;
        vm.SetArrowSpacing(MapCtrl.ActualWidth, map.Navigator.Viewport.Resolution);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        ScheduleArrowRefresh();  // capturé avant que Mapsui ne marque l'event Handled
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is GpxFileViewModel vm)
        {
            MapCtrl.Map = vm.Map;
            Dispatcher.BeginInvoke(ZoomToTrack, DispatcherPriority.Render);
            ScheduleArrowRefresh();
        }
    }

    private void ZoomToTrack()
    {
        if (DataContext is GpxFileViewModel vm && vm.TrackExtent is { } extent && MapCtrl.IsLoaded)
            MapCtrl.Map?.Navigator.ZoomToBox(extent);
    }

    private void OnFitClick(object sender, RoutedEventArgs e) => ZoomToTrack();

    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        if (DataContext is not GpxFileViewModel vm) return;
        var mapInfo = e.GetMapInfo(vm.Map.Layers);

        if (vm.IsSplitMode)
        {
            var worldPos = mapInfo?.WorldPosition;
            if (worldPos == null) return;
            var (track, idx) = FindClosestTrackPoint(vm, worldPos);
            if (track != null) vm.SplitTrack(track, idx);
            return;
        }

        if (mapInfo?.Layer != null && mapInfo.Layer == vm.WaypointLayer) return;
        var selectedTrack = mapInfo?.Layer != null ? vm.FindTrackByLayer(mapInfo.Layer) : null;
        vm.SetSelection(selectedTrack != null ? [selectedTrack] : [], selectedTrack);
    }

    private static (TrackViewModel? track, int index) FindClosestTrackPoint(GpxFileViewModel vm, MPoint worldPos)
    {
        var lonLat   = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);
        double cLon  = lonLat.lon;
        double cLat  = lonLat.lat;

        TrackViewModel? bestTrack = null;
        int    bestIndex = -1;
        double bestDist  = double.MaxValue;

        foreach (var track in vm.Tracks)
        {
            var pts = track.Track.Points;
            for (int i = 0; i < pts.Count; i++)
            {
                double dx = pts[i].Longitude - cLon;
                double dy = pts[i].Latitude  - cLat;
                double d  = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; bestTrack = track; bestIndex = i; }
            }
        }

        return (bestTrack, bestIndex);
    }

    private void OnMapMouseMove(object sender, MouseEventArgs e)
    {
        TryRightPan(e);
        if (DataContext is not GpxFileViewModel vm)
        {
            WaypointTooltip.Visibility   = Visibility.Collapsed;
            SplitPreviewDot.Visibility   = Visibility.Collapsed;
            return;
        }

        var screenPos = e.GetPosition(MapCtrl);
        var map = MapCtrl.Map;
        if (map == null)
        {
            WaypointTooltip.Visibility = Visibility.Collapsed;
            SplitPreviewDot.Visibility = Visibility.Collapsed;
            return;
        }
        var viewport = map.Navigator.Viewport;

        // Mode Diviser : afficher le point de coupe le plus proche
        if (vm.IsSplitMode)
        {
            WaypointTooltip.Visibility = Visibility.Collapsed;
            var worldPos = viewport.ScreenToWorld(screenPos.X, screenPos.Y);
            var (track, idx) = FindClosestTrackPoint(vm, worldPos);
            if (track != null)
            {
                var pt     = track.Track.Points[idx];
                var world  = SphericalMercator.FromLonLat(pt.Longitude, pt.Latitude);
                var screen = viewport.WorldToScreen(world.x, world.y);
                const double r = 7;
                Canvas.SetLeft(SplitPreviewDot, screen.X - r);
                Canvas.SetTop(SplitPreviewDot,  screen.Y - r);
                SplitPreviewDot.Visibility = Visibility.Visible;
            }
            else
            {
                SplitPreviewDot.Visibility = Visibility.Collapsed;
            }
            return;
        }

        SplitPreviewDot.Visibility = Visibility.Collapsed;

        if (vm.WaypointPositions.Count == 0)
        {
            WaypointTooltip.Visibility = Visibility.Collapsed;
            return;
        }

        // Dimensions de l'icône en pixels écran
        double halfW        = GpxFileViewModel.WptSvgWidth  * GpxFileViewModel.WptScale / 2; // 8.4 px
        double halfH        = GpxFileViewModel.WptSvgHeight * GpxFileViewModel.WptScale / 2; // 12.6 px
        double offsetScreen = GpxFileViewModel.WptOffsetY   * GpxFileViewModel.WptScale;     // 12.6 px au-dessus du point géo
        const double pad    = 4;

        foreach (var (worldPos, wp) in vm.WaypointPositions)
        {
            var s  = viewport.WorldToScreen(worldPos.X, worldPos.Y);
            double icX = s.X;
            double icY = s.Y - offsetScreen;   // centre de l'icône (Y-up → au-dessus du point géo)

            if (screenPos.X >= icX - halfW - pad && screenPos.X <= icX + halfW + pad &&
                screenPos.Y >= icY - halfH - pad && screenPos.Y <= icY + halfH + pad)
            {
                WptName.Text = wp.Name;

                // Altitude — masquée si la description la contient déjà
                bool descHasElev = wp.Elevation.HasValue &&
                                   wp.Description?.Contains($"{wp.Elevation:F0}") == true;
                WptElevation.Text       = (wp.Elevation.HasValue && !descHasElev) ? $"Alt. {wp.Elevation:F0} m" : "";
                WptElevation.Visibility = WptElevation.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

                WptDescription.Text       = wp.Description ?? "";
                WptDescription.Visibility = string.IsNullOrWhiteSpace(wp.Description) ? Visibility.Collapsed : Visibility.Visible;

                Canvas.SetLeft(WaypointTooltip, screenPos.X + 14);
                Canvas.SetTop(WaypointTooltip,  icY - halfH - 2);
                WaypointTooltip.Visibility = Visibility.Visible;
                return;
            }
        }

        WaypointTooltip.Visibility = Visibility.Collapsed;
    }

    private void OnMapMouseLeave(object sender, MouseEventArgs e)
    {
        WaypointTooltip.Visibility = Visibility.Collapsed;
        SplitPreviewDot.Visibility = Visibility.Collapsed;
    }

    // --- Effacement par zone (rubber-band) ---

    private void OnEraserMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent
        };
        MapCtrl.RaiseEvent(args);
        e.Handled = true;
    }

    // --- Pan bouton droit ---

    private bool  _rightPanActive;
    private Point _rightPanPrev;

    private void OnRightPanDown(object sender, MouseButtonEventArgs e)
    {
        _rightPanActive = true;
        _rightPanPrev   = e.GetPosition(MapCtrl);
        e.Handled = true;
    }

    private void OnRightPanUp(object sender, MouseButtonEventArgs e)
    {
        _rightPanActive = false;
        e.Handled = true;
    }

    private void TryRightPan(MouseEventArgs e)
    {
        var pos = e.GetPosition(MapCtrl);
        if (e.RightButton != MouseButtonState.Pressed) { _rightPanActive = false; return; }
        if (!_rightPanActive) return;

        var map = MapCtrl.Map;
        if (map == null) return;

        var vp = map.Navigator.Viewport;
        double dx = pos.X - _rightPanPrev.X;
        double dy = pos.Y - _rightPanPrev.Y;
        if (dx != 0 || dy != 0)
        {
            map.Navigator.CenterOn(new MPoint(vp.CenterX - dx * vp.Resolution, vp.CenterY + dy * vp.Resolution));
            MapCtrl.RefreshGraphics();
            ScheduleArrowRefresh();
        }
        _rightPanPrev = pos;
    }

    // --- Effacement par zone (rubber-band) ---

    private bool _eraserActive;
    private Point _eraserStart;

    private void OnEraserMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not GpxFileViewModel { IsEraserMode: true }) return;
        _eraserActive = true;
        _eraserStart  = e.GetPosition(EraserCanvas);
        EraserCanvas.CaptureMouse();
        Canvas.SetLeft(SelectionRect, _eraserStart.X);
        Canvas.SetTop(SelectionRect,  _eraserStart.Y);
        SelectionRect.Width      = 0;
        SelectionRect.Height     = 0;
        SelectionRect.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnEraserMouseMove(object sender, MouseEventArgs e)
    {
        TryRightPan(e);
        if (!_eraserActive) return;
        var pos = e.GetPosition(EraserCanvas);
        Canvas.SetLeft(SelectionRect, Math.Min(pos.X, _eraserStart.X));
        Canvas.SetTop(SelectionRect,  Math.Min(pos.Y, _eraserStart.Y));
        SelectionRect.Width  = Math.Abs(pos.X - _eraserStart.X);
        SelectionRect.Height = Math.Abs(pos.Y - _eraserStart.Y);
        e.Handled = true;
    }

    private void OnEraserMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_eraserActive) return;
        _eraserActive = false;
        EraserCanvas.ReleaseMouseCapture();
        SelectionRect.Visibility = Visibility.Collapsed;

        if (DataContext is not GpxFileViewModel vm) return;
        var endPos = e.GetPosition(EraserCanvas);

        // Zone trop petite → ignorer sans désactiver le mode
        if (Math.Abs(endPos.X - _eraserStart.X) < 5 || Math.Abs(endPos.Y - _eraserStart.Y) < 5)
        {
            e.Handled = true;
            return;
        }

        var map = MapCtrl.Map;
        if (map == null) { vm.IsEraserMode = false; return; }
        var vp = map.Navigator.Viewport;

        var sw = vp.ScreenToWorld(_eraserStart.X, _eraserStart.Y);
        var ew = vp.ScreenToWorld(endPos.X,       endPos.Y);

        vm.EraseZone(new MRect(
            Math.Min(sw.X, ew.X), Math.Min(sw.Y, ew.Y),
            Math.Max(sw.X, ew.X), Math.Max(sw.Y, ew.Y)));

        e.Handled = true;
    }
}
