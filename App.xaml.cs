using GpxManager.ViewModels;
using System.Linq;
using System.Windows;

namespace GpxManager;

public partial class App : Application
{
    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (MainWindow?.DataContext is not MainViewModel vm) return;
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
}

