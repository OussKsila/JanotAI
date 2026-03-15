using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using JanotAi.Services;

namespace JanotAi.Plugins;

/// <summary>
/// Plugin RAG : recherche sémantique dans la base de connaissances personnelle (dossier wiki/).
/// L'agent peut interroger les docs indexés pour répondre à des questions précises.
/// </summary>
public class WikiPlugin(SimpleVectorMemory memory)
{
    [KernelFunction("search_wiki")]
    [Description(
        "Recherche des informations dans la base de connaissances personnelle de l'utilisateur (notes, recettes, projets, contacts, commandes, etc.). " +
        "TOUJOURS appeler cette fonction en premier quand l'utilisateur pose une question sur un sujet qui pourrait être dans ses documents. " +
        "Exemples : recettes, ingrédients, contacts, projets, commandes, procédures, idées, notes personnelles.")]
    public async Task<string> SearchWikiAsync(
        [Description("La question ou le sujet à rechercher dans les documents")]
        string query,

        [Description("Nombre maximum de passages à retourner (1-8, défaut 4)")]
        int maxResults = 4)
    {
        var results = await memory.SearchAsync(query, limit: maxResults, minScore: 0.40);

        if (results.Count == 0)
            return "Aucun document pertinent trouvé dans le wiki. " +
                   "Essaie des mots-clés différents ou vérifie que tes fichiers sont bien dans le dossier wiki/.";

        var sb = new StringBuilder();
        foreach (var (i, r) in results.Select((r, i) => (i, r)))
        {
            if (i > 0) sb.AppendLine("\n---");
            sb.AppendLine($"**{r.Description}** *(pertinence : {r.Score:P0})*");
            sb.AppendLine(r.Text);
        }
        return sb.ToString();
    }

    [KernelFunction("list_wiki_docs")]
    [Description("Liste les documents disponibles dans la base de connaissances wiki.")]
    public Task<string> ListWikiDocsAsync()
    {
        return Task.FromResult(memory.Count == 0
            ? "Aucun document indexé. Ajoute tes fichiers .md ou .txt dans le dossier wiki/ puis redémarre Janot.ia."
            : $"Wiki RAG actif — {memory.Count} chunks indexés. " +
              "Utilise search_wiki pour interroger le contenu.");
    }
}
