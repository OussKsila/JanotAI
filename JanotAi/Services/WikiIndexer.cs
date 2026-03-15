using System.Text;
using System.Text.RegularExpressions;

namespace JanotAi.Services;

/// <summary>
/// Charge et indexe les documents du dossier wiki/ dans un SimpleVectorMemory.
/// Formats supportés : .md (Markdown) et .txt (texte brut).
/// Chaque fichier est découpé en chunks (~600 chars) avant vectorisation.
/// </summary>
public static class WikiIndexer
{
    private const int MaxChunkSize = 600;
    private const int ChunkOverlap = 80;

    /// <summary>
    /// Indexe tous les documents du dossier.
    /// Si <paramref name="cacheFile"/> est fourni et à jour, charge le cache disque
    /// sans appeler Ollama. Retourne le nb de chunks.
    /// </summary>
    public static async Task<int> IndexAsync(
        SimpleVectorMemory memory,
        string wikiFolder,
        string? cacheFile = null)
    {
        if (!Directory.Exists(wikiFolder)) return 0;

        var files = Directory.GetFiles(wikiFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".md",  StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToArray();

        // ── Essayer de charger le cache ──────────────────────────────────────
        if (cacheFile is not null && IsCacheValid(cacheFile, files))
        {
            int cached = await memory.LoadFromFileAsync(cacheFile);
            if (cached > 0)
            {
                Console.WriteLine($"[RAG] Cache charge depuis {Path.GetFileName(cacheFile)} ({cached} chunks)");
                return cached;
            }
        }

        // ── Re-indexer depuis les fichiers ───────────────────────────────────
        int total = 0;
        foreach (var file in files)
        {
            foreach (var chunk in GetChunks(file))
            {
                await memory.SaveAsync(
                    id:          chunk.Id,
                    text:        chunk.Content,
                    description: $"[{chunk.Source}] {chunk.Section}".TrimEnd());
                total++;
            }
        }

        // ── Sauvegarder le cache ─────────────────────────────────────────────
        if (cacheFile is not null && total > 0)
        {
            await memory.SaveToFileAsync(cacheFile);
            Console.WriteLine($"[RAG] Cache sauvegarde ({total} chunks) -> {Path.GetFileName(cacheFile)}");
        }

        return total;
    }

    /// <summary>
    /// Le cache est valide si le fichier existe ET est plus récent
    /// que tous les fichiers wiki.
    /// </summary>
    private static bool IsCacheValid(string cacheFile, string[] wikiFiles)
    {
        if (!File.Exists(cacheFile)) return false;
        if (wikiFiles.Length == 0) return false;
        var cacheTime = File.GetLastWriteTimeUtc(cacheFile);
        return wikiFiles.All(f => File.GetLastWriteTimeUtc(f) <= cacheTime);
    }

    // ── Découpage ────────────────────────────────────────────────────────────

    private static IEnumerable<WikiChunk> GetChunks(string filePath)
    {
        string content;
        try { content = File.ReadAllText(filePath, Encoding.UTF8); }
        catch { yield break; }

        var source = Path.GetFileNameWithoutExtension(filePath);

        if (filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            foreach (var c in ChunkMarkdown(content, source)) yield return c;
        else
            foreach (var c in ChunkPlainText(content, source)) yield return c;
    }

    private static IEnumerable<WikiChunk> ChunkMarkdown(string content, string source)
    {
        var sections = Regex.Split(content, @"(?=^#{1,3} .+)", RegexOptions.Multiline);
        int idx = 0;

        foreach (var section in sections)
        {
            var trimmed = section.Trim();
            if (trimmed.Length < 40) continue;

            var lines  = trimmed.Split('\n');
            var header = lines[0].TrimStart('#', ' ').Trim();
            var body   = string.Join('\n', lines.Skip(1)).Trim();
            if (body.Length < 30) body = trimmed;

            foreach (var sub in SplitBySize(body, MaxChunkSize, ChunkOverlap))
                yield return new WikiChunk($"{source}_{idx++}", source, header, sub.Trim());
        }
    }

    private static IEnumerable<WikiChunk> ChunkPlainText(string content, string source)
    {
        int idx = 0;
        var paragraphs = Regex.Split(content, @"\n\s*\n")
            .Select(p => p.Trim())
            .Where(p => p.Length > 30);

        var buffer = new StringBuilder();
        foreach (var para in paragraphs)
        {
            buffer.Append(para).Append('\n');
            if (buffer.Length >= MaxChunkSize)
            {
                yield return new WikiChunk($"{source}_{idx++}", source, "", buffer.ToString().Trim());
                var tail = buffer.Length > ChunkOverlap ? buffer.ToString()[^ChunkOverlap..] : "";
                buffer.Clear();
                buffer.Append(tail);
            }
        }
        if (buffer.Length > 30)
            yield return new WikiChunk($"{source}_{idx}", source, "", buffer.ToString().Trim());
    }

    private static IEnumerable<string> SplitBySize(string text, int maxSize, int overlap)
    {
        if (text.Length <= maxSize) { yield return text; yield break; }
        for (int i = 0; i < text.Length;)
        {
            int end = Math.Min(i + maxSize, text.Length);
            yield return text[i..end];
            if (end == text.Length) break;
            i += maxSize - overlap;
        }
    }
}

public record WikiChunk(string Id, string Source, string Section, string Content);
