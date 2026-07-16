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

    private GpxFileViewModel CreateTab(GpxFile gpx)
    {
        var tab = new GpxFileViewModel(gpx);
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
