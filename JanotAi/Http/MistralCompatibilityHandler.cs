using System.Text;
using System.Text.Json.Nodes;

namespace JanotAi.Http;

/// <summary>
/// Retire les champs non supportés par l'API Mistral avant chaque requête.
/// En cas de 422, affiche le corps de la réponse pour diagnostic.
/// </summary>
public class MistralCompatibilityHandler : DelegatingHandler
{
    private readonly string? _apiKey;

    public MistralCompatibilityHandler(string? apiKey = null) : base(new HttpClientHandler())
    {
        _apiKey = apiKey;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Toujours forcer notre clé — le SDK OpenAI avec HttpClient custom peut injecter
        // un header vide ou incorrect. On écrase pour garantir la bonne valeur.
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Remove("Authorization");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            var node = JsonNode.Parse(body);
            if (node is JsonObject obj)
            {
                obj.Remove("stream_options");
                obj.Remove("logprobs");
                obj.Remove("top_logprobs");
                obj.Remove("parallel_tool_calls");  // non supporté par Mistral

                // Mistral utilise "max_tokens" et non "max_completion_tokens"
                if (obj["max_completion_tokens"] is JsonNode maxTokens)
                {
                    obj.Remove("max_completion_tokens");
                    obj["max_tokens"] = maxTokens.DeepClone();
                }

                // Mistral n'accepte pas le champ "name" sur les messages
                // + exige que le dernier message soit user/tool (pas assistant)
                if (obj["messages"] is JsonArray messages)
                {
                    foreach (var msg in messages)
                    {
                        if (msg is JsonObject m)
                            m.Remove("name");
                    }

                    // Si le dernier message est "assistant", injecter un relais user
                    var last = messages.LastOrDefault();
                    if (last is JsonObject lastObj &&
                        lastObj["role"]?.GetValue<string>() == "assistant")
                    {
                        messages.Add(new JsonObject
                        {
                            ["role"]    = "user",
                            ["content"] = "Continue."
                        });
                    }
                }

                var newBody = obj.ToJsonString();
                request.Content = new StringContent(newBody, Encoding.UTF8, "application/json");
            }
        }

        return await SendWithRetryAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int[] delaysSeconds = [2, 5, 15];

        for (int attempt = 0; ; attempt++)
        {
            // On doit recréer le contenu à chaque tentative car il n'est lisible qu'une fois
            HttpRequestMessage req = request;
            if (attempt > 0 && request.Content is not null)
            {
                // Le contenu a déjà été lu — on relit depuis la copie en mémoire
                req = await CloneRequestAsync(request, cancellationToken);
            }

            var response = await base.SendAsync(req, cancellationToken);

            if ((int)response.StatusCode == 429 && attempt < delaysSeconds.Length)
            {
                // Lire le header Retry-After s'il est présent
                int delay = delaysSeconds[attempt];
                if (response.Headers.TryGetValues("Retry-After", out var values) &&
                    int.TryParse(values.FirstOrDefault(), out int retryAfter))
                {
                    delay = Math.Min(retryAfter + 1, 30);
                }

                Console.Error.WriteLine(
                    $"[MistralHandler] 429 — Rate limit. Nouvelle tentative dans {delay}s " +
                    $"(essai {attempt + 1}/{delaysSeconds.Length})…");

                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.Error.WriteLine($"[MistralHandler] {(int)response.StatusCode} — {errorBody}");
                response.Content = new StringContent(errorBody, Encoding.UTF8, "application/json");
            }

            return response;
        }
    }

    /// <summary>Clone une requête HTTP pour pouvoir la renvoyer après un 429.</summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage original, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (original.Content is not null)
        {
            var body = await original.Content.ReadAsStringAsync(ct);
            clone.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return clone;
    }
}
