namespace TokenBar.Core;

/// <summary>
/// Sağlayıcıdan bağımsız, <b>normalize edilmiş</b> token toplamı.
/// </summary>
/// <remarks>
/// ⭐ Alanlar kaynak formata değil, <b>anlama</b> göre isimlendi. Codex ile
/// Claude Code aynı alan adını taban tabana zıt anlamda kullanıyor:
///
/// <code>
///                        Codex CLI                  Claude Code
/// input_tokens           girdinin TAMAMI            girdinin %0,12'si
/// önbellekten okunan     input'un ALT KÜMESİ        input'ten AYRIK
/// toplam                 input + output             input + read + write
/// </code>
///
/// Buradaki dört alan <b>ayrık</b> olacak şekilde doldurulur. Böylece
/// <see cref="Total"/> her sağlayıcı için aynı formülle hesaplanır —
/// normalizasyonun bütün amacı bu.
/// </remarks>
public sealed record TokenTotals(
    long InputFresh,
    long InputCached,
    long CacheWrite,
    long Output,
    long Reasoning)
{
    public static readonly TokenTotals Zero = new(0, 0, 0, 0, 0);

    /// <summary>Girdinin tamamı — üçü ayrık olduğu için doğrudan toplanır.</summary>
    public long Input => InputFresh + InputCached + CacheWrite;

    /// <summary>
    /// ⚠️ <see cref="Reasoning"/> buraya EKLENMEZ — o zaten Output'un alt kümesi,
    /// yalnızca bilgi amaçlı taşınıyor.
    /// </summary>
    public long Total => Input + Output;

    /// <summary>Girdinin yüzde kaçı önbellekten geldi (yani ucuzdu).</summary>
    public double CacheHitRatio => Input == 0 ? 0 : (double)InputCached / Input;

    public static TokenTotals operator +(TokenTotals a, TokenTotals b) => new(
        a.InputFresh + b.InputFresh,
        a.InputCached + b.InputCached,
        a.CacheWrite + b.CacheWrite,
        a.Output + b.Output,
        a.Reasoning + b.Reasoning);
}

/// <summary>
/// Sayı kesin mi, tahmin mi. Tahmini bir sayıyı kesin sayıyla aynı görünümde
/// göstermek kullanıcıyı kandırmaktır — arayüz bunu <c>≈</c> ile ayırır.
/// </summary>
public enum Accuracy
{
    Exact,
    Estimated
}

public enum ProviderStatus
{
    /// <summary>Okundu, sayılar güvenilir.</summary>
    Ok,

    /// <summary>Ajan bu makinede kurulu değil.</summary>
    NotInstalled,

    /// <summary>Veri var ama okunamıyor. <c>Note</c> sebebini söyler.</summary>
    Unsupported,

    /// <summary>Okuma sırasında beklenmedik hata. <c>Note</c> mesajı taşır.</summary>
    Failed
}

/// <summary>Tek bir oturum (Codex'te bir .jsonl, Claude Code'da bir .jsonl).</summary>
public sealed record SessionInfo(
    string Label,
    DateTimeOffset LastActivity,
    TokenTotals Totals,
    Accuracy Accuracy);

/// <summary>
/// Kota durumu. ⚠️ Kümülatif değil, <b>anlık durum</b> — ve ölçüldüğü an
/// geçmişte kalmış olabilir. Bu yüzden <see cref="MeasuredAt"/> zorunlu.
/// </summary>
public sealed record QuotaInfo(
    double UsedPercent,
    int WindowMinutes,
    DateTimeOffset? ResetsAt,
    DateTimeOffset MeasuredAt,
    string PlanType)
{
    /// <summary>
    /// Pencerenin adı <b>türetilir, varsayılmaz.</b> Başka planda haftalık ya da
    /// 5 saatlik olabilir.
    /// </summary>
    public string WindowLabel => WindowMinutes switch
    {
        <= 0 => "bilinmiyor",
        < 60 => $"{WindowMinutes} dk",
        < 1440 => $"{WindowMinutes / 60} saat",
        43200 => "aylık",
        10080 => "haftalık",
        _ => $"{WindowMinutes / 1440} gün"
    };

    public TimeSpan Age => DateTimeOffset.Now - MeasuredAt;
}

/// <summary>Taramanın kendisi hakkında sayılar — dürüstlük göstergesi.</summary>
public sealed record ScanStats(
    int Files,
    int FilesWithData,
    int MalformedLines,
    int UnreadableFiles,
    int DedupedLines,
    int CachedFiles);

/// <summary>Bir sağlayıcının tek bir tarama sonucu.</summary>
public sealed record ProviderResult(
    string AgentName,
    ProviderStatus Status,
    TokenTotals Totals,
    IReadOnlyList<SessionInfo> Sessions,
    ScanStats Stats,
    QuotaInfo? Quota = null,
    TokenTotals? SubagentTotals = null,
    string? Note = null)
{
    public static ProviderResult NotInstalled(string name) =>
        new(name, ProviderStatus.NotInstalled, TokenTotals.Zero, [],
            new ScanStats(0, 0, 0, 0, 0, 0));

    public static ProviderResult Unsupported(string name, string note, int files) =>
        new(name, ProviderStatus.Unsupported, TokenTotals.Zero, [],
            new ScanStats(files, 0, 0, 0, 0, 0), Note: note);

    public static ProviderResult Failed(string name, string note) =>
        new(name, ProviderStatus.Failed, TokenTotals.Zero, [],
            new ScanStats(0, 0, 0, 0, 0, 0), Note: note);
}

/// <summary>Bütün sağlayıcıların tek turluk sonucu.</summary>
public sealed record ScanResult(
    IReadOnlyList<ProviderResult> Providers,
    DateTimeOffset ScannedAt,
    long ElapsedMs)
{
    /// <summary>
    /// ⚠️ Bu toplam <b>karşılaştırma amaçlı değildir.</b> 1000 Codex token'ı ile
    /// 1000 Claude token'ı aynı şey değil: farklı tokenizer, farklı fiyat,
    /// farklı abonelik. Arayüz bu notu göstermek zorunda.
    /// </summary>
    public TokenTotals GrandTotal => Providers
        .Where(p => p.Status == ProviderStatus.Ok)
        .Aggregate(TokenTotals.Zero, (a, p) => a + p.Totals);

    public IEnumerable<ProviderResult> Active =>
        Providers.Where(p => p.Status == ProviderStatus.Ok);
}
