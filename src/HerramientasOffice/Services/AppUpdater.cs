using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace HerramientasOffice.Services;

public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string PageUrl,
    string DownloadUrl,
    string? Sha256);

/// <summary>Actualizacion de la propia app desde los Releases de GitHub.</summary>
public static class AppUpdater
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HerramientasOffice", AppInfo.CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Devuelve el ultimo release si es mas nuevo que la version instalada; si no, null.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest";
        using var response = await Http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // Aun no hay releases publicados.
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return null;
        var version = AppInfo.Normalize(parsed);
        if (version <= AppInfo.CurrentVersion) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AppInfo.ExeName, StringComparison.OrdinalIgnoreCase))
                continue;

            string? digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            string? sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..]
                : null;

            return new ReleaseInfo(
                version, tag,
                root.GetProperty("html_url").GetString() ?? AppInfo.RepoUrl,
                asset.GetProperty("browser_download_url").GetString()!,
                sha256);
        }

        return null; // El release no trae el ejecutable.
    }

    /// <summary>
    /// Descarga el nuevo ejecutable, verifica su SHA-256 y lanza el reemplazo.
    /// Despues de llamar a este metodo hay que cerrar la aplicacion.
    /// </summary>
    public static async Task DownloadAndStageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        var current = Environment.ProcessPath;
        if (current is null || !string.Equals(Path.GetFileName(current), AppInfo.ExeName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La actualizacion automatica solo funciona con el ejecutable publicado (HerramientasOffice.exe).");

        if (release.Sha256 is null)
            throw new InvalidOperationException("El release no incluye un hash SHA-256 para verificar la descarga; no se aplicara la actualizacion.");

        string dir = Path.Combine(Path.GetTempPath(), "HerramientasOffice-update");
        Directory.CreateDirectory(dir);
        string downloaded = Path.Combine(dir, AppInfo.ExeName);

        using (var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(downloaded);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report((double)read / total.Value);
            }
        }

        string actual = Convert.ToHexString(await ComputeHashAsync(downloaded, ct));
        if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(downloaded);
            throw new InvalidOperationException("El archivo descargado no coincide con el hash publicado. Se cancelo la actualizacion.");
        }

        // Script auxiliar: espera a que esta app cierre, reemplaza el .exe y la vuelve a abrir.
        string script = Path.Combine(dir, "aplicar-actualizacion.cmd");
        File.WriteAllText(script, $"""
            @echo off
            set tries=0
            :retry
            set /a tries+=1
            move /y "{downloaded}" "{current}" >nul 2>&1
            if errorlevel 1 (
                if %tries% geq 30 exit /b 1
                timeout /t 1 /nobreak >nul
                goto retry
            )
            start "" "{current}"
            del "%~f0"
            """);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static async Task<byte[]> ComputeHashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream, ct);
    }
}
