using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.ChatCompletion;
using JanotAi.Configuration;

namespace JanotAi.Persistence;

/// <summary>
/// Persiste l'historique de conversation entre les sessions.
///
/// Sauvegarde automatiquement après chaque échange et recharge
/// au démarrage, permettant à l'agent de se souvenir des conversations précédentes.
/// </summary>
public class ConversationHistory
{
    private readonly PersistenceConfig _config;
    private readonly string _filePath;

    public ConversationHistory(PersistenceConfig config)
    {
        _config   = config;
        _filePath = Path.GetFullPath(config.FilePath);
    }

    /// <summary>Charge l'historique depuis le fichier JSON.</summary>
    public ChatHistory Load()
    {
        if (!_config.Enabled || !File.Exists(_filePath))
            return new ChatHistory();

        try
        {
            var json    = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<List<MessageRecord>>(json) ?? [];

            var history = new ChatHistory();
            foreach (var record in records.TakeLast(_config.MaxMessages))
            {
                if (record.Role == "user")
                    history.AddUserMessage(record.Content);
                else if (record.Role == "assistant")
                    history.AddAssistantMessage(record.Content);
            }

            return history;
        }
        catch
        {
            return new ChatHistory();  // en cas de fichier corrompu, on repart de zéro
        }
    }

    /// <summary>Sauvegarde l'historique dans le fichier JSON.</summary>
    public void Save(ChatHistory history)
    {
        if (!_config.Enabled) return;

        try
        {
            var records = history
                .Where(m => m.Role == AuthorRole.User || m.Role == AuthorRole.Assistant)
                .TakeLast(_config.MaxMessages)
                .Select(m => new MessageRecord(
                    m.Role == AuthorRole.User ? "user" : "assistant",
                    m.Content ?? ""))
                .ToList();

            var json = JsonSerializer.Serialize(records, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_filePath, json);
        }
        catch { /* ignore save errors silently */ }
    }

    /// <summary>Supprime l'historique sauvegardé.</summary>
    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    public bool HasSavedHistory => File.Exists(_filePath);

    public int SavedMessageCount
    {
        get
        {
            try
            {
                if (!File.Exists(_filePath)) return 0;
                var json = File.ReadAllText(_filePath);
                var records = JsonSerializer.Deserialize<List<MessageRecord>>(json);
                return records?.Count ?? 0;
            }
            catch { return 0; }
        }
    }

    private record MessageRecord(
        [property: JsonPropertyName("role")]    string Role,
        [property: JsonPropertyName("content")] string Content);
}
