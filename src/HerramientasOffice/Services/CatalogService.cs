using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text.RegularExpressions;

namespace HerramientasOffice.Services;

public sealed record CatalogItem(string Kb, string Title, string Product, DateTime Date, bool IsSecurity);

/// <summary>
/// Lista las actualizaciones publicadas por Microsoft en el Catalogo de Microsoft Update
/// (todas las del producto, no solo las aplicables a este equipo).
/// </summary>
public static class CatalogService
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly Regex RowRegex = new(@"<tr id=""[0-9a-f-]{36}_R\d+""[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex CellRegex = new(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HerramientasOffice", AppInfo.CurrentVersion.ToString()));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
        return client;
    }

    /// <summary>Actualizaciones de Excel y Word 2016 publicadas desde el 1 de enero de <paramref name="fromYear"/>.</summary>
    public static async Task<List<CatalogItem>> GetRecentAsync(int fromYear, bool is64Bit, CancellationToken ct = default)
    {
        string edition = is64Bit ? "64-Bit" : "32-Bit";
        var queries = new (string Product, string Query)[]
        {
            ("Excel", $"Excel 2016 {edition} Edition"),
            ("Word", $"Word 2016 {edition} Edition"),
        };

        var tasks = queries.Select(q => FetchAsync(q.Product, q.Query, fromYear, ct)).ToList();
        var results = new List<CatalogItem>();
        Exception? error = null;

        foreach (var task in tasks)
        {
            try { results.AddRange(await task); }
            catch (Exception ex) { error = ex; }
        }

        // Si fallo todo, se informa; si fallo solo una parte, se muestra lo que llego.
        if (results.Count == 0 && error is not null) throw error;
        return results;
    }

    private static async Task<List<CatalogItem>> FetchAsync(string product, string query, int fromYear, CancellationToken ct)
    {
        string url = "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(query);
        string html = await Http.GetStringAsync(url, ct);

        var items = new List<CatalogItem>();
        foreach (Match row in RowRegex.Matches(html))
        {
            var cells = CellRegex.Matches(row.Groups[1].Value)
                .Select(c => SpaceRegex.Replace(WebUtility.HtmlDecode(TagRegex.Replace(c.Groups[1].Value, "")), " ").Trim())
                .ToList();
            if (cells.Count < 5) continue;

            string title = cells[1];
            string kb = OfficeProducts.ExtractKb(title);
            if (kb.Length == 0) continue;
            if (OfficeProducts.Detect(title) != product) continue; // descarta resultados de otros productos

            if (!DateTime.TryParseExact(cells[4], "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (date.Year < fromYear) continue;

            items.Add(new CatalogItem(kb, title, product, date, IsSecurityTitle(title)));
        }
        return items;
    }

    public static bool IsSecurityTitle(string title) =>
        title.Contains("Security", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("seguridad", StringComparison.OrdinalIgnoreCase);
}
