using GpxManager.ViewModels;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GpxManager;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // --- Confirmation à la fermeture si modifications non sauvegardées ---

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dirty = vm.Tabs.Where(t => t.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var names = string.Join("\n", dirty.Select(t => $"  • {t.File.FileName}"));
        var result = MessageBox.Show(
            $"Les fichiers suivants ont des modifications non sauvegardées :\n\n{names}\n\nQuitter quand même ?",
            "Modifications non sauvegardées",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    // --- Drag & drop de fichiers GPX sur la fenêtre ---

    private static bool HasGpxFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) &&
        e.Data.GetData(DataFormats.FileDrop) is string[] files &&
        files.Any(f => Path.GetExtension(f).Equals(".gpx", StringComparison.OrdinalIgnoreCase));

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasGpxFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!HasGpxFiles(e)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var gpxFiles = files.Where(f => Path.GetExtension(f).Equals(".gpx", StringComparison.OrdinalIgnoreCase));
        if (DataContext is MainViewModel vm)
            vm.LoadFiles(gpxFiles);
        e.Handled = true;
    }

    // Clé pour stocker l'état de drag dans les propriétés de chaque ListBox
    private static readonly DependencyProperty DragStateProperty =
        DependencyProperty.RegisterAttached("DragState", typeof((TrackViewModel? Track, Point Start)),
            typeof(MainWindow));

    // Clic gauche : mémorise le point de départ + désélection sur zone vide
    private void OnTrackListMouseDown(object sender, MouseButtonEventArgs e)
    {
        var lb        = (ListBox)sender;
        var container = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject);
        if (container == null)
        {
            lb.UnselectAll();
            lb.SetValue(DragStateProperty, ((TrackViewModel?)null, new Point()));
        }
        else
        {
            var track = FindAncestorDataContext<TrackViewModel>(e.OriginalSource as DependencyObject);
            lb.SetValue(DragStateProperty, (track, e.GetPosition(null)));
        }
    }

    private void OnTrackListMouseUp(object sender, MouseButtonEventArgs e)
        => ((ListBox)sender).SetValue(DragStateProperty, ((TrackViewModel?)null, new Point()));

    private void OnTrackListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var lb    = (ListBox)sender;
        var state = ((TrackViewModel? Track, Point Start))lb.GetValue(DragStateProperty);
        if (state.Track == null) return;

        var diff = e.GetPosition(null) - state.Start;
        if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance) return;

        lb.SetValue(DragStateProperty, ((TrackViewModel?)null, new Point()));
        DragDrop.DoDragDrop(lb, state.Track, DragDropEffects.Move);
    }

    private static T? FindAncestorDataContext<T>(DependencyObject? obj) where T : class
    {
        while (obj != null)
        {
            if (obj is FrameworkElement       fe  && fe.DataContext  is T t1) return t1;
            if (obj is FrameworkContentElement fce && fce.DataContext is T t2) return t2;
            obj = obj is Visual or Visual3D
                ? VisualTreeHelper.GetParent(obj)
                : LogicalTreeHelper.GetParent(obj);
        }
        return null;
    }

    // --- Indicateur visuel d'insertion (Adorner) ---
    private InsertionLineAdorner? _insertionAdorner;

    private (TrackViewModel? target, bool after) GetDropTarget(ListBox lb, DragEventArgs e)
    {
        var hit    = lb.InputHitTest(e.GetPosition(lb)) as DependencyObject;
        var target = FindAncestorDataContext<TrackViewModel>(hit);
        if (target == null) return (null, false);
        bool after = lb.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement ctr
                     && e.GetPosition(ctr).Y > ctr.ActualHeight / 2;
        return (target, after);
    }

    private void ShowInsertionLine(ListBox lb, TrackViewModel? target, bool after)
    {
        var layer = AdornerLayer.GetAdornerLayer(lb);
        if (layer == null) return;

        if (_insertionAdorner != null) { layer.Remove(_insertionAdorner); _insertionAdorner = null; }
        if (target == null) return;
        if (lb.ItemContainerGenerator.ContainerFromItem(target) is not FrameworkElement item) return;

        var pt = item.TranslatePoint(new Point(0, after ? item.ActualHeight : 0), lb);
        _insertionAdorner = new InsertionLineAdorner(lb, pt.Y);
        layer.Add(_insertionAdorner);
    }

    private void OnTrackListDragOver(object sender, DragEventArgs e)
    {
        var lb = (ListBox)sender;
        if (!e.Data.GetDataPresent(typeof(TrackViewModel)))
        {
            // Laisser les drops de fichiers remonter à la Window
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var (target, after) = GetDropTarget(lb, e);
        ShowInsertionLine(lb, target, after);
    }

    private void OnTrackListDragLeave(object sender, DragEventArgs e)
    {
        var lb  = (ListBox)sender;
        var pos = e.GetPosition(lb);
        if (pos.X < 0 || pos.Y < 0 || pos.X > lb.ActualWidth || pos.Y > lb.ActualHeight)
            ShowInsertionLine(lb, null, false);
    }

    private void OnTrackListDrop(object sender, DragEventArgs e)
    {
        var lb = (ListBox)sender;
        ShowInsertionLine(lb, null, false);

        // Laisser les drops de fichiers remonter à la Window
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        if (lb.DataContext is not GpxFileViewModel vm) return;
        if (e.Data.GetData(typeof(TrackViewModel)) is not TrackViewModel dragged) return;

        var (target, after) = GetDropTarget(lb, e);
        if (target == null || target == dragged) return;

        int newIdx = vm.Tracks.IndexOf(target);
        if (after) newIdx++;
        vm.MoveTrack(dragged, newIdx);
    }

    private bool _syncingListBoxToVm;

    private void OnTrackListLoaded(object sender, RoutedEventArgs e)
    {
        var lb = (ListBox)sender;
        if (lb.DataContext is not GpxFileViewModel vm) return;

        void handler(IReadOnlyList<TrackViewModel> tracks, TrackViewModel? primary)
        {
            _syncingListBoxToVm = true;
            lb.UnselectAll();
            foreach (var t in tracks) lb.SelectedItems.Add(t);
            _syncingListBoxToVm = false;
        }

        vm.SelectionUpdated += handler;
        lb.Unloaded += (_, _) => vm.SelectionUpdated -= handler;
    }

    private void OnTrackListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingListBoxToVm) return;
        var lb = (ListBox)sender;
        if (lb.DataContext is not GpxFileViewModel vm) return;
        vm.SetSelection(lb.SelectedItems.Cast<TrackViewModel>().ToList(), lb.SelectedItem as TrackViewModel);
    }

    // Adorner qui dessine la ligne d'insertion
    private sealed class InsertionLineAdorner : Adorner
    {
        private static readonly Pen LinePen;
        private readonly double _y;

        static InsertionLineAdorner()
        {
            LinePen = new Pen(Brushes.DodgerBlue, 2);
            LinePen.Freeze();
        }

        public InsertionLineAdorner(UIElement target, double y) : base(target)
        {
            IsHitTestVisible = false;
            _y = y;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = AdornedElement.RenderSize.Width;
            dc.DrawEllipse(Brushes.DodgerBlue, null, new Point(6,      _y), 3, 3);
            dc.DrawLine(LinePen,                      new Point(6,      _y), new Point(w - 6, _y));
            dc.DrawEllipse(Brushes.DodgerBlue, null, new Point(w - 6,  _y), 3, 3);
        }
    }
}
