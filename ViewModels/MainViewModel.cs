using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GpxManager.Models;
using GpxManager.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace GpxManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private GpxFileViewModel? _selectedTab;

    public ObservableCollection<GpxFileViewModel> Tabs { get; } = [];

    public bool HasTabs => Tabs.Count > 0;

    public string StatusText => SelectedTab is { } tab
        ? $"{tab.File.FileName}  —  {tab.Tracks.Count} trace(s)  ·  {tab.Waypoints.Count} waypoint(s)"
        : "Aucun fichier chargé — ouvrez un fichier GPX pour commencer.";

    public MainViewModel()
    {
        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTabs));
            OnPropertyChanged(nameof(StatusText));
        };

        RestoreSession();
    }

    [RelayCommand]
    private void NewFile()
    {
        var (file, doc) = GpxParser.NewEmpty();
        var tab = CreateTab(file, doc);
        Tabs.Add(tab);
        SelectedTab = tab;
        SaveSession();
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Ouvrir un fichier GPX",
            Filter = "Fichiers GPX (*.gpx)|*.gpx|Tous les fichiers (*.*)|*.*",
            DefaultExt = ".gpx",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;
        LoadFiles(dialog.FileNames);
    }

    public void LoadFiles(IEnumerable<string> paths)
    {
        GpxFileViewModel? lastAdded = null;
        foreach (var fileName in paths)
        {
            var existing = Tabs.FirstOrDefault(t =>
                string.Equals(t.File.FilePath, fileName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SelectedTab = existing;
                continue;
            }

            try
            {
                var tab = CreateTab(GpxParser.Parse(fileName));
                Tabs.Add(tab);
                lastAdded = tab;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Impossible de charger {Path.GetFileName(fileName)} :\n{ex.Message}",
                    "Erreur GPX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        if (lastAdded != null)
            SelectedTab = lastAdded;

        SaveSession();
    }

    [RelayCommand]
    private void CloseAllTabs()
    {
        if (!ConfirmClose(Tabs.ToList())) return;
        SelectedTab = null;
        Tabs.Clear();
        SaveSession();
    }

    [RelayCommand]
    private void CloseOtherTabs(GpxFileViewModel? tab)
    {
        if (tab == null) return;
        var others = Tabs.Where(t => t != tab).ToList();
        if (!ConfirmClose(others)) return;
        foreach (var t in others)
            Tabs.Remove(t);
        SelectedTab = tab;
        SaveSession();
    }

    private static bool ConfirmClose(IList<GpxFileViewModel> tabs)
    {
        var dirty = tabs.Where(t => t.IsDirty).ToList();
        if (dirty.Count == 0) return true;
        var names = string.Join("\n", dirty.Select(t => $"  • {t.File.FileName}"));
        return MessageBox.Show(
            $"Les fichiers suivants ont des modifications non sauvegardées :\n\n{names}\n\nFermer quand même ?",
            "Modifications non sauvegardées",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private GpxFileViewModel CreateTab(GpxFile gpx, XDocument? doc = null)
    {
        var tab = new GpxFileViewModel(gpx, doc);
        tab.CloseRequested = () =>
        {
            int idx = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            if (Tabs.Count > 0)
                SelectedTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
            SaveSession();
        };
        return tab;
    }

    private void RestoreSession()
    {
        foreach (var path in SessionService.Load())
        {
            if (!File.Exists(path)) continue;
            try
            {
                Tabs.Add(CreateTab(GpxParser.Parse(path)));
            }
            catch { }
        }

        if (Tabs.Count > 0)
            SelectedTab = Tabs[0];
    }

    private void SaveSession() =>
        SessionService.Save(Tabs.Select(t => t.File.FilePath));
}
