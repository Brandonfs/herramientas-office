using System.Reflection;
using System.Security.Principal;

namespace HerramientasOffice.Services;

public static class AppInfo
{
    public const string RepoOwner = "Brandonfs";
    public const string RepoName = "herramientas-office";
    public const string ExeName = "HerramientasOffice.exe";

    public static string RepoUrl => $"https://github.com/{RepoOwner}/{RepoName}";

    public static bool IsAdmin { get; } = DetectAdmin();

    /// <summary>Version actual normalizada a Major.Minor.Build.</summary>
    public static Version CurrentVersion { get; } = Normalize(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0));

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static bool DetectAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
