namespace TokenBar.Core;

/// <summary>
/// Değişmemiş dosyayı ikinci kez okumamak için basit önbellek.
/// </summary>
/// <remarks>
/// Uygulama dakikada bir tarıyor ama 100 dosyanın belki biri değişmiş oluyor.
/// Anahtar: <b>uzunluk + son yazma zamanı</b>. İkisi birden aynıysa dosya
/// değişmemiş sayılır.
///
/// ⚠️ Bu bir tahmindir, kanıt değil. Aynı boyutta ve aynı saniyede yapılan bir
/// değişikliği kaçırabilir. Kabul edilebilir: bir sonraki turda dosya yine
/// değişirse yakalanır, hata kalıcı olmaz. Hash almak doğru olurdu ama 60 MB'ı
/// her dakika hash'lemek, okumaktan pahalı.
///
/// ⚠️ Süreç kapanınca kaybolur — diske yazılmıyor. Kalıcı önbellek şema
/// kayması riskini de beraberinde getirirdi.
/// </remarks>
public sealed class FileCache
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    private sealed record Entry(long Length, long TicksUtc, object Value);

    /// <summary>Son <see cref="ResetHits"/> çağrısından beri kaç dosya okunmadan geldi.</summary>
    public int Hits { get; private set; }

    public void ResetHits()
    {
        lock (_gate) Hits = 0;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Hits = 0;
        }
    }

    public T GetOrRead<T>(string path, Func<string, T> read) where T : class
    {
        var info = new FileInfo(path);
        var length = info.Length;
        var ticks = info.LastWriteTimeUtc.Ticks;

        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var entry) &&
                entry.Length == length &&
                entry.TicksUtc == ticks &&
                entry.Value is T hit)
            {
                Hits++;
                return hit;
            }
        }

        // ⚠️ Okuma kilidin DIŞINDA. 11 MB'lik dosyayı kilit tutarak okursak
        // diğer sağlayıcı boşuna bekler.
        var value = read(path);

        lock (_gate) _entries[path] = new Entry(length, ticks, value);
        return value;
    }
}
