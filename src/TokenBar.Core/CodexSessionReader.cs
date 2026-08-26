using System.Text.Json;

namespace TokenBar.Core;

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

        QuotaInfo? quota = null;
        var lastActivity = DateTimeOffset.MinValue;

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

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // Bozuk satır tüm dosyayı çöpe atmasın; say ve devam et.
                malformed++;
                continue;
            }

            using (document)
            {
                var root = document.RootElement;

                if (!root.TryGetProperty("payload", out var payload)) continue;
                if (!payload.TryGetProperty("type", out var type) ||
                    type.GetString() != Marker) continue;

                var timestamp = ReadTimestamp(root);
                if (timestamp > lastActivity) lastActivity = timestamp;

                // Kota, token'dan BAĞIMSIZ okunur: info null olsa bile
                // rate_limits dolu olabilir — zaten çoğu zaman öyle.
                if (ReadQuota(payload, timestamp) is { } q) quota = q;

                // payload.info null olabilir — o satır sadece rate_limits taşır.
                if (ParseTotalUsage(payload) is not { } usage) continue;

                // Sayaç geriye gitti mi? (context_compacted sıfırlıyor mu sorusu)
                if (previousTotal >= 0 && usage.Total < previousTotal) reset = true;
                previousTotal = usage.Total;

                // ⭐ Kümülatif: topla DEĞİL, üzerine yaz. Son değer kazanır.
                last = usage;
                found = true;
            }
        }

        if (lastActivity == DateTimeOffset.MinValue)
            lastActivity = new DateTimeOffset(File.GetLastWriteTime(path));

        return new SessionResult(path, last, found, malformed, reset, quota, lastActivity);
    }

    private static DateTimeOffset ReadTimestamp(JsonElement root) =>
        root.TryGetProperty("timestamp", out var ts) &&
        ts.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(ts.GetString(), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    /// <summary>
    /// payload.info.total_token_usage. Satır token_count değilse veya
    /// info null ise null döner.
    /// </summary>
    private static TokenUsage? ParseTotalUsage(JsonElement payload)
    {
        if (!payload.TryGetProperty("info", out var info) ||
            info.ValueKind != JsonValueKind.Object)
            return null;

        if (!info.TryGetProperty("total_token_usage", out var total))
            return null;

        return new TokenUsage(
            ReadLong(total, "input_tokens"),
            ReadLong(total, "cached_input_tokens"),
            ReadLong(total, "cache_write_input_tokens"),
            ReadLong(total, "output_tokens"),
            ReadLong(total, "reasoning_output_tokens"));
    }

    /// <summary>
    /// rate_limits.primary — kotayı <b>ağa çıkmadan</b> veren alan.
    /// 2.165 satırın %98,1'inde dolu.
    /// </summary>
    private static QuotaInfo? ReadQuota(JsonElement payload, DateTimeOffset timestamp)
    {
        if (!payload.TryGetProperty("rate_limits", out var limits) ||
            limits.ValueKind != JsonValueKind.Object)
            return null;

        if (!limits.TryGetProperty("primary", out var primary) ||
            primary.ValueKind != JsonValueKind.Object)
            return null;

        if (!primary.TryGetProperty("used_percent", out var used) ||
            !used.TryGetDouble(out var percent))
            return null;

        DateTimeOffset? resets = null;
        if (primary.TryGetProperty("resets_at", out var r) && r.TryGetInt64(out var unix))
            resets = DateTimeOffset.FromUnixTimeSeconds(unix);   // ⚠️ saniye, milisaniye değil

        var plan = limits.TryGetProperty("plan_type", out var p) ? p.GetString() ?? "?" : "?";

        return new QuotaInfo(
            UsedPercent: percent,
            WindowMinutes: (int)ReadLong(primary, "window_minutes"),
            ResetsAt: resets,
            MeasuredAt: timestamp == DateTimeOffset.MinValue ? DateTimeOffset.Now : timestamp,
            PlanType: plan);
    }

    /// <summary>Alan yoksa 0. 210 milyonu int taşımaz — long şart.</summary>
    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0L;
}
