namespace TokenBar.Core;

/// <summary>
/// Codex CLI sağlayıcısı — <b>kümülatif</b> aritmetik.
/// </summary>
/// <remarks>
/// Her dosyanın SON <c>token_count</c> satırındaki <c>total_token_usage</c>
/// o oturumun toplamıdır. Satırları toplarsan 2-3 kat şişirirsin.
/// </remarks>
public sealed class CodexProvider : IUsageProvider
{
    public string Name => "Codex CLI";

    public bool IsInstalled() => Directory.Exists(CodexSessionReader.FindSessionsDirectory());

    public ProviderResult Scan(FileCache cache, CancellationToken ct)
    {
        var directory = CodexSessionReader.FindSessionsDirectory();
        if (!Directory.Exists(directory))
            return ProviderResult.NotInstalled(Name);

        var results = new List<SessionResult>();
        var unreadable = 0;
        var files = 0;

        foreach (var path in CodexSessionReader.EnumerateSessionFiles(directory))
        {
            ct.ThrowIfCancellationRequested();
            files++;

            try
            {
                results.Add(cache.GetOrRead(path, CodexSessionReader.ReadFile));
            }
            catch (IOException)
            {
                // Tek dosya yüzünden tarama çökmesin.
                unreadable++;
            }
            catch (UnauthorizedAccessException)
            {
                unreadable++;
            }
        }

        var withData = results.Where(r => r.HasTokenCount).ToList();

        var totals = withData.Aggregate(
            TokenTotals.Zero,
            (sum, r) => sum + r.Usage.ToTotals());

        var sessions = withData
            .OrderByDescending(r => r.Usage.Total)
            .Select(r => new SessionInfo(
                Label: r.LastActivity.ToLocalTime().ToString("d MMM yyyy HH:mm"),
                LastActivity: r.LastActivity,
                Totals: r.Usage.ToTotals(),
                Accuracy: Accuracy.Exact))
            .ToList();

        // Kota anlık durum: en SON ölçülen değer geçerli olan.
        var quota = results
            .Where(r => r.Quota is not null)
            .OrderByDescending(r => r.Quota!.MeasuredAt)
            .Select(r => r.Quota)
            .FirstOrDefault();

        var stats = new ScanStats(
            Files: files,
            FilesWithData: withData.Count,
            MalformedLines: results.Sum(r => r.MalformedLines),
            UnreadableFiles: unreadable,
            DedupedLines: 0,
            CachedFiles: 0);

        // ❓ Açık soruydu: context_compacted sayacı sıfırlıyor mu?
        // Hiçbir dosyada geriye gitmedi. Yine de izlemeye devam.
        var resets = results.Count(r => r.CounterReset);
        var note = resets == 0
            ? null
            : $"⚠️ {resets} oturumda sayaç geriye gitti — toplam eksik olabilir.";

        return new ProviderResult(
            Name, ProviderStatus.Ok, totals, sessions, stats,
            Quota: quota, Note: note);
    }
}
