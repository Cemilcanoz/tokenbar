using System.Diagnostics;

namespace TokenBar.Core;

/// <summary>
/// Bütün sağlayıcıları sırayla tarar. Uygulamanın tek giriş noktası.
/// </summary>
public sealed class UsageScanner
{
    private readonly IUsageProvider[] _providers;
    private readonly FileCache _cache = new();

    public UsageScanner(params IUsageProvider[] providers) => _providers = providers;

    /// <summary>Makinede aranacak ajanlar — sıra arayüzdeki sırayı belirler.</summary>
    public static UsageScanner Default() => new(
        new CodexProvider(),
        new ClaudeCodeProvider(),
        new AntigravityProvider());

    /// <summary>Önbelleği boşaltır — kullanıcı "her şeyi yeniden oku" derse.</summary>
    public void Invalidate() => _cache.Clear();

    /// <summary>
    /// ⚠️ Arka plan thread'inde çağrılır. Hiçbir arayüz elemanına dokunmaz.
    /// </summary>
    public ScanResult Scan(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<ProviderResult>(_providers.Length);

        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();
            _cache.ResetHits();

            try
            {
                var result = provider.Scan(_cache, ct);

                // Önbellek isabetini sağlayıcı bilmiyor; sayacı burada geçiriyoruz.
                results.Add(result with { Stats = result.Stats with { CachedFiles = _cache.Hits } });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 🔴 Bir sağlayıcının çökmesi diğerlerini götürmesin. Kullanıcı
                // "Claude okunamadı" görsün, boş pencere değil.
                results.Add(ProviderResult.Failed(provider.Name, ex.Message));
            }
        }

        stopwatch.Stop();
        return new ScanResult(results, DateTimeOffset.Now, stopwatch.ElapsedMilliseconds);
    }
}
