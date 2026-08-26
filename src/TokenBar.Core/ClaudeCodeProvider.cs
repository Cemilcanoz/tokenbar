using System.Text.Json;

namespace TokenBar.Core;

/// <summary>Dosya içinde tekilleştirilmiş tek bir asistan mesajı.</summary>
public sealed record ClaudeMessage(string Id, TokenTotals Totals, bool IsSidechain);

/// <summary>Bir .jsonl dosyasının okunma sonucu.</summary>
public sealed record ClaudeFileResult(
    string Path,
    string ProjectLabel,
    IReadOnlyList<ClaudeMessage> Messages,
    int MalformedLines,
    int DuplicateLines,
    DateTimeOffset LastActivity);

/// <summary>
/// Claude Code sağlayıcısı — <b>artımlı</b> aritmetik, Codex'in tam tersi.
/// </summary>
/// <remarks>
/// İki kritik fark:
///
/// 1. 🔴 <c>input_tokens</c> burada girdinin yalnızca <b>%0,12'si</b>.
///    Gerçek girdi = <c>input + cache_read + cache_creation</c> (üçü ayrık).
///    Sadece <c>input_tokens</c>'a bakarsan girdinin %99,88'ini kaybedersin.
///
/// 2. ⭐ Aynı mesaj dosyaya <b>birden çok kez</b> yazılıyor — sayılar artmıyor,
///    birebir kopya. <c>uuid</c> ile tekilleştirirsen 3,2 kat şişer; doğru
///    anahtar <c>message.id</c>.
/// </remarks>
public sealed class ClaudeCodeProvider : IUsageProvider
{
    private const string Marker = "usage";

    public string Name => "Claude Code";

    /// <summary>
    /// Oturum dosyalarının klasörü.
    /// </summary>
    /// <remarks>
    /// <c>TOKENBAR_CLAUDE_HOME</c> tanımlıysa ona bakar. Codex'teki
    /// <c>CODEX_HOME</c>'un karşılığı — ve asıl faydası test: canlı bir ajanın
    /// klasörü sürekli değiştiği için referans sayı tutturulamaz. Dondurulmuş
    /// bir kopyaya yönlendirebilmek şart.
    /// </remarks>
    public static string FindProjectsDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("TOKENBAR_CLAUDE_HOME");
        if (!string.IsNullOrWhiteSpace(overridden))
            return Path.Combine(overridden, "projects");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", "projects");
    }

    public bool IsInstalled() => Directory.Exists(FindProjectsDirectory());

    public ProviderResult Scan(FileCache cache, CancellationToken ct)
    {
        var directory = FindProjectsDirectory();
        if (!Directory.Exists(directory))
            return ProviderResult.NotInstalled(Name);

        var files = new List<ClaudeFileResult>();
        var unreadable = 0;
        var fileCount = 0;

        foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            fileCount++;

            try
            {
                files.Add(cache.GetOrRead(path, ReadFile));
            }
            catch (IOException) { unreadable++; }
            catch (UnauthorizedAccessException) { unreadable++; }
        }

        // ⭐ Tekilleştirme dosya sınırını AŞAR. Bir oturum devam ettirilirse
        // aynı message.id iki dosyada birden görünebilir; dosya başına
        // tekilleştirip sonra toplarsak o mesajı iki kez sayarız.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var totals = TokenTotals.Zero;
        var subagent = TokenTotals.Zero;
        var sessions = new List<SessionInfo>();
        var crossFileDuplicates = 0;

        foreach (var file in files.OrderBy(f => f.LastActivity))
        {
            var fileTotals = TokenTotals.Zero;

            foreach (var message in file.Messages)
            {
                if (!seen.Add(message.Id))
                {
                    crossFileDuplicates++;
                    continue;
                }

                fileTotals += message.Totals;
                if (message.IsSidechain) subagent += message.Totals;
            }

            totals += fileTotals;

            if (fileTotals.Total > 0)
            {
                sessions.Add(new SessionInfo(
                    Label: $"{file.ProjectLabel} · {file.LastActivity.ToLocalTime():d MMM HH:mm}",
                    LastActivity: file.LastActivity,
                    Totals: fileTotals,
                    Accuracy: Accuracy.Exact));
            }
        }

        var stats = new ScanStats(
            Files: fileCount,
            FilesWithData: sessions.Count,
            MalformedLines: files.Sum(f => f.MalformedLines),
            UnreadableFiles: unreadable,
            DedupedLines: files.Sum(f => f.DuplicateLines) + crossFileDuplicates,
            CachedFiles: 0);

        return new ProviderResult(
            Name,
            ProviderStatus.Ok,
            totals,
            sessions.OrderByDescending(s => s.Totals.Total).ToList(),
            stats,
            SubagentTotals: subagent);
    }

    private static ClaudeFileResult ReadFile(string path)
    {
        var messages = new List<ClaudeMessage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var malformed = 0;
        var duplicates = 0;
        var lastActivity = DateTimeOffset.MinValue;

        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            // Ucuz ön eleme: satırların yarısından fazlası buradan döner.
            if (!line.Contains(Marker, StringComparison.Ordinal)) continue;

            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { malformed++; continue; }

            using (document)
            {
                var root = document.RootElement;

                // usage YALNIZCA assistant satırlarında var.
                if (!root.TryGetProperty("type", out var type) ||
                    type.GetString() != "assistant") continue;

                if (!root.TryGetProperty("message", out var message) ||
                    message.ValueKind != JsonValueKind.Object) continue;

                if (!message.TryGetProperty("usage", out var usage) ||
                    usage.ValueKind != JsonValueKind.Object) continue;

                var timestamp = ReadTimestamp(root);
                if (timestamp > lastActivity) lastActivity = timestamp;

                // message.id yoksa uuid'ye düş — o zaman tekilleştirme
                // zayıflar ama satırı büsbütün kaybetmekten iyidir.
                var id = ReadString(message, "id") ?? ReadString(root, "uuid");
                if (id is null) continue;

                if (!seen.Add(id)) { duplicates++; continue; }

                var sidechain = root.TryGetProperty("isSidechain", out var sc) &&
                                sc.ValueKind == JsonValueKind.True;

                // ⭐ Üç girdi alanı AYRIK — Codex'in aksine toplanır.
                messages.Add(new ClaudeMessage(
                    id,
                    new TokenTotals(
                        InputFresh: ReadLong(usage, "input_tokens"),
                        InputCached: ReadLong(usage, "cache_read_input_tokens"),
                        CacheWrite: ReadLong(usage, "cache_creation_input_tokens"),
                        Output: ReadLong(usage, "output_tokens"),
                        Reasoning: 0),          // Claude Code reasoning'i ayrı vermiyor
                    sidechain));
            }
        }

        if (lastActivity == DateTimeOffset.MinValue)
            lastActivity = new DateTimeOffset(File.GetLastWriteTime(path));

        return new ClaudeFileResult(
            path, ProjectLabel(path), messages, malformed, duplicates, lastActivity);
    }

    /// <summary>
    /// Klasör adı proje yolunun kodlanmış hâli (<c>C--Users-cemil-source-repos-x</c>).
    /// Son parça genelde proje adı — etiket olarak yeter, kesin değil.
    /// </summary>
    private static string ProjectLabel(string path)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? "?";
        var parts = folder.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : folder;
    }

    private static DateTimeOffset ReadTimestamp(JsonElement root) =>
        root.TryGetProperty("timestamp", out var ts) &&
        ts.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(ts.GetString(), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0L;
}
