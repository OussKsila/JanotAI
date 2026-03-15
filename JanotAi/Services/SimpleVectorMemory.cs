using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.Embeddings;

namespace JanotAi.Services;

/// <summary>
/// Store vectoriel in-memory minimaliste.
/// Pas de dépendance externe — utilise cosine similarity en pur C#.
/// Suffisant pour des wikis jusqu'à ~5 000 chunks (quelques secondes de recherche).
/// Supporte la persistance sur disque (SaveToFileAsync / LoadFromFileAsync).
/// </summary>
public class SimpleVectorMemory
{
#pragma warning disable SKEXP0001
    private readonly ITextEmbeddingGenerationService _embedder;

    private record Entry(string Id, string Description, string Text, ReadOnlyMemory<float> Vec);
    private readonly List<Entry> _store = [];

    public SimpleVectorMemory(ITextEmbeddingGenerationService embedder) => _embedder = embedder;

    // ── Indexation ───────────────────────────────────────────────────────────

    public async Task SaveAsync(string id, string text, string description = "")
    {
        var vec = await _embedder.GenerateEmbeddingAsync(text);
        _store.RemoveAll(e => e.Id == id);            // idempotent
        _store.Add(new Entry(id, description, text, vec));
    }

    // ── Recherche ────────────────────────────────────────────────────────────

    public async Task<List<SearchResult>> SearchAsync(
        string query, int limit = 5, double minScore = 0.50)
    {
        if (_store.Count == 0) return [];

        var qVec = await _embedder.GenerateEmbeddingAsync(query);

        return _store
            .Select(e => new SearchResult(e.Text, e.Description, Cosine(qVec.Span, e.Vec.Span)))
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    public int Count => _store.Count;

    // ── Persistance disque ───────────────────────────────────────────────────

    /// <summary>Sauvegarde le store sur disque (JSON).</summary>
    public async Task SaveToFileAsync(string filePath)
    {
        var dtos = _store.Select(e => new EntryDto(e.Id, e.Description, e.Text, e.Vec.ToArray())).ToList();
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await using var fs = File.Create(filePath);
        await JsonSerializer.SerializeAsync(fs, dtos, SerializerOptions);
    }

    /// <summary>
    /// Charge le store depuis un fichier JSON.
    /// Retourne le nombre d'entrées chargées (0 si le fichier n'existe pas).
    /// </summary>
    public async Task<int> LoadFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return 0;
        await using var fs = File.OpenRead(filePath);
        var dtos = await JsonSerializer.DeserializeAsync<List<EntryDto>>(fs, SerializerOptions);
        if (dtos is null) return 0;
        _store.Clear();
        foreach (var d in dtos)
            _store.Add(new Entry(d.Id, d.Description, d.Text, d.Vec));
        return _store.Count;
    }

    // ── DTO + options JSON ───────────────────────────────────────────────────

    private sealed record EntryDto(string Id, string Description, string Text, float[] Vec);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    // ── Cosine similarity ────────────────────────────────────────────────────

    private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int len = Math.Min(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            na  += a[i] * a[i];
            nb  += b[i] * b[i];
        }
        return (na == 0 || nb == 0) ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
#pragma warning restore SKEXP0001
}

public record SearchResult(string Text, string Description, double Score);
