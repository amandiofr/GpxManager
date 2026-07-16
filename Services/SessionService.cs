using System.IO;
using System.Text.Json;

namespace GpxManager.Services;

public static class SessionService
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GpxManager",
        "session.json");

    public static void Save(IEnumerable<string> filePaths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
        File.WriteAllText(SessionPath, JsonSerializer.Serialize(filePaths.ToList()));
    }

    public static IReadOnlyList<string> Load()
    {
        if (!File.Exists(SessionPath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SessionPath)) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
