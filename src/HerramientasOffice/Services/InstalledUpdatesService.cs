using HerramientasOffice.Models;
using Microsoft.Win32;

namespace HerramientasOffice.Services;

public static class InstalledUpdatesService
{
    private static readonly string[] UninstallPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    public static List<UpdateEntry> GetInstalled()
    {
        var results = new List<UpdateEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        foreach (var path in UninstallPaths)
        {
            using var root = hklm.OpenSubKey(path);
            if (root is null) continue;

            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is not string display || display.Length == 0) continue;

                    var kb = OfficeProducts.ExtractKb(display);
                    if (kb.Length == 0) continue;

                    var product = OfficeProducts.Detect(display);
                    if (product is null) continue;

                    if (!seen.Add(display)) continue;

                    DateTime? installedOn = null;
                    if (key.GetValue("InstallDate") is string raw &&
                        DateTime.TryParseExact(raw, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d))
                        installedOn = d;

                    results.Add(new UpdateEntry
                    {
                        Kb = kb,
                        Title = display,
                        Product = product,
                        State = UpdateState.Instalada,
                        UninstallString = key.GetValue("UninstallString") as string,
                        InstalledDate = installedOn,
                    });
                }
                catch
                {
                    // Clave ilegible: se ignora y se sigue con la siguiente.
                }
            }
        }

        return results;
    }
}
