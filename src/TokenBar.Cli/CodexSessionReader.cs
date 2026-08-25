using System.Text.Json;

namespace TokenBar.Cli;

/// <summary>
/// Codex CLI'ın oturum kayıtlarını (.jsonl) okur ve token sayılarını çıkarır.
/// </summary>
public static class CodexSessionReader
{
    /// <summary>Satırda bu metin yoksa JSON parse etmeye hiç kalkışma. (ucuz ön eleme)</summary>
    private const string Marker = "token_count";

    /// <summary>
    /// Oturum dosyalarının bulunduğu klasör.
    /// CODEX_HOME tanımlıysa ona, değilse %USERPROFILE%\.codex'e bakar.
    /// </summary>
    public static string FindSessionsDirectory()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");

        if (string.IsNullOrWhiteSpace(codexHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            codexHome = Path.Combine(home, ".codex");
        }

        return Path.Combine(codexHome, "sessions");
    }

    /// <summary>Klasör altındaki tüm .jsonl dosyaları, özyinelemeli.</summary>
    public static IEnumerable<string> EnumerateSessionFiles(string sessionsDirectory)
    {
        if (!Directory.Exists(sessionsDirectory))
            return [];

        return Directory.EnumerateFiles(sessionsDirectory, "*.jsonl", SearchOption.AllDirectories);
    }

    /// <summary>
    /// Tek bir oturum dosyasını okur.
    /// </summary>
    /// <remarks>
    /// ⚠️ File.ReadLines / File.ReadAllLines KULLANMA. İkisi de dosyayı
    /// FileShare.Read ile açar; Codex o sırada yazıyorsa IOException fırlatır.
    /// Aşağıdaki FileShare.ReadWrite | FileShare.Delete bunun tek çözümü.
    /// </remarks>
    public static SessionResult ReadFile(string path)
    {
        var last = TokenUsage.Zero;
        var found = false;
        var malformed = 0;
        var reset = false;
        long previousTotal = -1;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            // Ucuz ön eleme: on binlerce satırın ezici çoğunluğu buradan döner.
            if (!line.Contains(Marker, StringComparison.Ordinal))
                continue;

            TokenUsage? usage;
            try
            {
                usage = ParseTotalUsage(line);
            }
            catch (JsonException)
            {
                // Bozuk satır tüm dosyayı çöpe atmasın; say ve devam et.
                malformed++;
                continue;
            }

            // payload.info null olabilir — o satır sadece rate_limits taşır.
            if (usage is null)
                continue;

            // Sayaç geriye gitti mi? (context_compacted sıfırlıyor mu sorusu)
            if (previousTotal >= 0 && usage.Total < previousTotal)
                reset = true;

            previousTotal = usage.Total;

            // ⭐ Kümülatif: topla DEĞİL, üzerine yaz. Son değer kazanır.
            last = usage;
            found = true;
        }

        return new SessionResult(path, last, found, malformed, reset);
    }

    /// <summary>
    /// Bir JSONL satırından payload.info.total_token_usage'ı çıkarır.
    /// Satır token_count değilse veya info null ise null döner.
    /// </summary>
    private static TokenUsage? ParseTotalUsage(string line)
    {
        using var document = JsonDocument.Parse(line);

        if (!document.RootElement.TryGetProperty("payload", out var payload))
            return null;

        // "token_count" metni satırda başka bir yerde de geçmiş olabilir.
        if (!payload.TryGetProperty("type", out var type) || type.GetString() != Marker)
            return null;

        if (!payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
            return null;

        if (!info.TryGetProperty("total_token_usage", out var total))
            return null;

        return new TokenUsage(
            ReadLong(total, "input_tokens"),
            ReadLong(total, "cached_input_tokens"),
            ReadLong(total, "output_tokens"),
            ReadLong(total, "reasoning_output_tokens"));
    }

    /// <summary>Alan yoksa 0. 210 milyonu int taşımaz — long şart.</summary>
    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0L;
}
