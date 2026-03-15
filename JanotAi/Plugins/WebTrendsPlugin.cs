using System.ComponentModel;
using System.Net;
using HtmlAgilityPack;
using Microsoft.SemanticKernel;

namespace JanotAi.Plugins;

/// <summary>
/// Plugin SK natif pour récupérer les tendances Twitter/X du jour
/// via le site public trends24.in (sans API key).
/// </summary>
public class WebTrendsPlugin
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36" },
            { "Accept-Language", "fr-FR,fr;q=0.9,en;q=0.8" }
        }
    };

    [KernelFunction("get_twitter_trends")]
    [Description("Retourne les tendances Twitter/X du jour pour un pays donné")]
    public async Task<string> GetTwitterTrendsAsync(
        [Description("Pays en anglais (minuscules, tirets) : france, united-states, united-kingdom, canada, germany, japan, brazil, worldwide…")]
        string country = "france")
    {
        country = country.ToLowerInvariant().Trim().Replace(" ", "-");
        var url = $"https://trends24.in/{country}/";

        string html;
        try
        {
            html = await _http.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            return $"Erreur lors de la récupération des tendances ({url}) : {ex.Message}";
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // trends24.in structure : <ol class="trend-card__list"> contenant des <li><a>
        var trendNodes = doc.DocumentNode
            .SelectNodes("//ol[contains(@class,'trend-card__list')]//li/a");

        if (trendNodes is null || trendNodes.Count == 0)
        {
            // Fallback : chercher n'importe quel lien avec href commençant par /trend/
            trendNodes = doc.DocumentNode
                .SelectNodes("//a[starts-with(@href,'/trend/')]");
        }

        if (trendNodes is null || trendNodes.Count == 0)
            return $"Aucune tendance trouvée pour '{country}'. Vérifiez que le nom du pays est correct (ex: france, united-states).";

        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<string>();

        foreach (var node in trendNodes)
        {
            var text = WebUtility.HtmlDecode(node.InnerText).Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) continue;
            if (!seen.Add(text)) continue;

            items.Add(text);
            if (items.Count >= 20) break;
        }

        if (items.Count == 0)
            return "Impossible d'extraire les tendances. Le site a peut-être changé de structure.";

        var lines = items.Select((t, i) => $"{i + 1,2}. {t}");
        return $"Tendances Twitter/X — {country} (source: trends24.in)\n\n" +
               string.Join("\n", lines);
    }
}
