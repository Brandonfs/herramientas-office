using System.IO;
using System.Text.Json;

namespace HerramientasOffice.Services;

/// <summary>Lista persistente de KBs marcados como problematicos por el usuario.</summary>
public static class ProblemKbStore
{
    private static readonly string[] Defaults = { "KB5002914", "KB5002934" };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HerramientasOffice", "kbs-problematicos.json");

    public static HashSet<string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
                if (list is not null)
                    return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Archivo corrupto: se usan los valores por defecto.
        }
        return new HashSet<string>(Defaults, StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(IEnumerable<string> kbs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(kbs.OrderBy(k => k).ToList()));
    }
}
