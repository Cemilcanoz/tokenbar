namespace TokenBar.Core;

/// <summary>
/// Codex'in bir oturumda o ana kadar harcadığı token'lar — <b>ham</b> hâli.
/// Kümülatif; dosyadaki son değer, o oturumun toplamıdır.
/// </summary>
/// <remarks>
/// ⭐ <c>cached_input_tokens</c> ⊂ <c>input_tokens</c>,
/// <c>reasoning_output_tokens</c> ⊂ <c>output_tokens</c>.
/// Bu yüzden ham toplam = Input + Output; diğerleri zaten içeride.
///
/// Bu tip Codex'e özel. Normalize edilmiş ortak tip
/// <see cref="TokenTotals"/> — dönüşümü <see cref="ToTotals"/> yapar.
/// </remarks>
public sealed record TokenUsage(
    long InputTokens,
    long CachedInputTokens,
    long CacheWriteInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens)
{
    public static readonly TokenUsage Zero = new(0, 0, 0, 0, 0);

    public long Total => InputTokens + OutputTokens;

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new(
        a.InputTokens + b.InputTokens,
        a.CachedInputTokens + b.CachedInputTokens,
        a.CacheWriteInputTokens + b.CacheWriteInputTokens,
        a.OutputTokens + b.OutputTokens,
        a.ReasoningOutputTokens + b.ReasoningOutputTokens);

    /// <summary>
    /// Codex aritmetiğini ortak modele çevirir.
    /// </summary>
    /// <remarks>
    /// 🔴 Buradaki <b>çıkarma</b> işlemi normalizasyonun kalbi. Codex'te
    /// önbellek alanları <c>input_tokens</c>'ın içinde; ortak modelde ise üç
    /// girdi alanı <b>ayrık</b> olmak zorunda. Çıkarmazsan aynı token'ı iki
    /// kez sayarsın.
    ///
    /// Aşağıdaki kırpma savunma amaçlı ve <b>toplamı korur</b>: üç girdi alanı
    /// her koşulda <c>input_tokens</c>'a eşit toplanır. Şema ileride kayar ve
    /// alt kümeler taşarsa uygulama yanlış bir toplam üretmek yerine kırılımı
    /// bozar — sessiz şişme, görünür tuhaflıktan daha tehlikelidir.
    /// </remarks>
    public TokenTotals ToTotals()
    {
        var bilinen = Math.Min(CachedInputTokens + CacheWriteInputTokens, InputTokens);
        var onbellek = Math.Min(CachedInputTokens, bilinen);

        return new TokenTotals(
            InputFresh: InputTokens - bilinen,
            InputCached: onbellek,
            CacheWrite: bilinen - onbellek,
            Output: OutputTokens,
            Reasoning: ReasoningOutputTokens);
    }
}

/// <summary>Tek bir .jsonl oturum dosyasının okunma sonucu.</summary>
public sealed record SessionResult(
    string Path,
    TokenUsage Usage,
    bool HasTokenCount,
    int MalformedLines,
    bool CounterReset,
    QuotaInfo? Quota,
    DateTimeOffset LastActivity);
