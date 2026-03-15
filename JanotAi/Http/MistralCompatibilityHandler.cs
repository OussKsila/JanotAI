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
        // Injecter la clé si absente (SDK OpenAI parfois ne la transmet pas avec un HttpClient custom)
        if (_apiKey is not null && !request.Headers.Contains("Authorization"))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

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

        var response = await base.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.Error.WriteLine($"[MistralHandler] {(int)response.StatusCode} — {errorBody}");
            response.Content = new StringContent(errorBody, Encoding.UTF8, "application/json");
        }

        return response;
    }
}
