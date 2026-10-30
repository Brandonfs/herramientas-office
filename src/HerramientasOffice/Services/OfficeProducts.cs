using System.Text.RegularExpressions;

namespace HerramientasOffice.Services;

public static class OfficeProducts
{
    private static readonly Regex KbRegex = new(@"KB\s?(\d{6,8})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Orden importa: los productos especificos primero, "Office" generico al final.
    private static readonly (string Label, string Needle)[] Products =
    {
        ("Excel", "Excel"),
        ("Word", "Microsoft Word"),
        ("Outlook", "Outlook"),
        ("PowerPoint", "PowerPoint"),
        ("Access", "Microsoft Access"),
        ("OneNote", "OneNote"),
        ("Visio", "Visio"),
        ("Publisher", "Microsoft Publisher"),
        ("Project", "Microsoft Project"),
        ("Skype", "Skype for Business"),
        ("Skype", "Lync"),
        ("Office", "Office"),
    };

    public static string ExtractKb(string text)
    {
        var m = KbRegex.Match(text);
        return m.Success ? "KB" + m.Groups[1].Value : "";
    }

    public static string? Detect(string text)
    {
        foreach (var (label, needle) in Products)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return label;
        }
        return null;
    }
}
