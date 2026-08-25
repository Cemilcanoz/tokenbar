namespace TokenBar.Cli;

/// <summary>
/// Codex'in bir oturumda o ana kadar harcadığı token'lar.
/// Kümülatif — dosyadaki son değer, o oturumun toplamıdır.
/// </summary>
/// <remarks>
/// ⭐ cached_input_tokens ⊂ input_tokens, reasoning_output_tokens ⊂ output_tokens.
/// Bu yüzden Total = Input + Output. Diğer ikisi zaten içeride.
/// </remarks>
public sealed record TokenUsage(
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens)
{
    public static readonly TokenUsage Zero = new(0, 0, 0, 0);

    public long Total => InputTokens + OutputTokens;

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new(
        a.InputTokens + b.InputTokens,
        a.CachedInputTokens + b.CachedInputTokens,
        a.OutputTokens + b.OutputTokens,
        a.ReasoningOutputTokens + b.ReasoningOutputTokens);
}

/// <summary>Tek bir .jsonl oturum dosyasının okunma sonucu.</summary>
public sealed record SessionResult(
    string Path,
    TokenUsage Usage,
    bool HasTokenCount,
    int MalformedLines,
    bool CounterReset);
