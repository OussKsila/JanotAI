using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.Embeddings;

#pragma warning disable SKEXP0001

namespace JanotAi.Http;

/// <summary>
/// Service d'embeddings direct pour Mistral AI.
/// Bypass complet du SDK OpenAI — un seul appel HTTP batch, auth garantie.
/// </summary>
public class MistralEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly HttpClient   _http;
    private readonly string       _model;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    public MistralEmbeddingService(string model, string apiKey, string baseUrl)
    {
        _model  = model;
        _http   = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Envoie tous les textes en un seul appel batch.</summary>
    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Microsoft.SemanticKernel.Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new EmbedRequest { Model = _model, Input = data.ToList() };
        var json    = JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");


        int[] delays = [2, 5, 15];
        HttpResponseMessage? response = null;

        for (int attempt = 0; attempt <= delays.Length; attempt++)
        {
            response = await _http.PostAsync("embeddings", content, cancellationToken);

            if ((int)response.StatusCode == 429 && attempt < delays.Length)
            {
                int wait = delays[attempt];
                if (response.Headers.TryGetValues("Retry-After", out var vals) &&
                    int.TryParse(vals.FirstOrDefault(), out int ra))
                    wait = Math.Min(ra + 1, 30);

                Console.Error.WriteLine($"[Embeddings] 429 — retry dans {wait}s...");
                await Task.Delay(TimeSpan.FromSeconds(wait), cancellationToken);
                content = new StringContent(json, Encoding.UTF8, "application/json");
                continue;
            }
            break;
        }

        response!.EnsureSuccessStatusCode();

        var body   = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<EmbedResponse>(body, JsonOpts)
            ?? throw new InvalidOperationException("Réponse embeddings vide.");

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => new ReadOnlyMemory<float>(d.Embedding))
            .ToList();
    }

    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?>();

    // ── DTOs ────────────────────────────────────────────────────────────────

    private sealed class EmbedRequest
    {
        public string       Model { get; set; } = "";
        public List<string> Input { get; set; } = [];
    }

    private sealed class EmbedResponse
    {
        public List<EmbedData> Data { get; set; } = [];
    }

    private sealed class EmbedData
    {
        public int     Index     { get; set; }
        public float[] Embedding { get; set; } = [];
    }
}

#pragma warning restore SKEXP0001
