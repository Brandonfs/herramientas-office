using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HerramientasOffice.Services;

public static class UninstallRunner
{
    private static readonly Regex QuotedExe = new(@"^\s*""([^""]+)""\s*(.*)$", RegexOptions.Compiled);

    /// <summary>Ejecuta el desinstalador registrado y espera a que termine. Devuelve el codigo de salida.</summary>
    public static int Run(string uninstallString)
    {
        var m = QuotedExe.Match(uninstallString);
        var psi = m.Success
            ? new ProcessStartInfo(m.Groups[1].Value, m.Groups[2].Value)
            : new ProcessStartInfo("cmd.exe", "/c " + uninstallString);
        psi.UseShellExecute = true;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("No se pudo iniciar el desinstalador.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
