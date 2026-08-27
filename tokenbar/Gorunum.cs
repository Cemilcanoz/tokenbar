using TokenBar.Core;

namespace tokenbar;

/// <summary>
/// Ekranda görünen tek bir ajan kartı. Bütün alanlar hazır <c>string</c> —
/// biçimlendirme arka planda bitiyor, XAML'de dönüştürücü gerekmiyor.
/// </summary>
public sealed record AjanKarti(
    string Ad,
    string Toplam,
    string YeniGirdi,
    string Onbellek,
    string OnbellekYuzde,
    string Cikti,
    string? AltAjan,
    string Istatistik,
    bool KotaVar,
    double KotaOran,
    string KotaBaslik,
    string KotaAlt,
    bool Uyari,
    string UyariMetni)
{
    public static AjanKarti Olustur(ProviderResult p)
    {
        var t = p.Totals;

        var kota = p.Quota;

        // ⚠️ Kota kümülatif değil, ANLIK DURUM. Ölçümün üstünden bir pencere
        // boyu geçtiyse o değer artık bir şey anlatmıyor — sayı yerine
        // uyarı gösteriyoruz. Bkz. QuotaInfo.IsStale.
        var kotaBaslik = kota switch
        {
            null => "",
            { IsStale: true } => "kota  ⚠️ bayat ölçüm",
            _ => $"kota  {Bicim.Yuzde(kota.UsedPercent / 100)} kullanılmış · " +
                 $"kalan {Bicim.Yuzde(kota.RemainingPercent / 100)}"
        };

        var kotaAlt = kota switch
        {
            null => "",
            { IsStale: true } =>
                $"son ölçüm {Bicim.NeKadarOnce(kota.MeasuredAt)}; {kota.WindowLabel} pencere " +
                "bu arada sıfırlandı. Ajanı bir kez çalıştır, değer tazelensin.",
            _ => $"{kota.WindowLabel} pencere · {kota.PlanType} · " +
                 $"ölçüm {Bicim.NeKadarOnce(kota.MeasuredAt)}" +
                 (kota.ResetsAt is { } r ? $" · {Bicim.NeKadarSonra(r)} yenilenir" : "")
        };

        var istatistik = $"{p.Stats.FilesWithData}/{p.Stats.Files} dosya";
        if (p.Stats.DedupedLines > 0) istatistik += $" · {Bicim.Tam(p.Stats.DedupedLines)} tekrar atıldı";
        if (p.Stats.MalformedLines > 0) istatistik += $" · {p.Stats.MalformedLines} bozuk satır";
        if (p.Stats.UnreadableFiles > 0) istatistik += $" · {p.Stats.UnreadableFiles} açılamadı";
        if (p.Stats.CachedFiles > 0) istatistik += $" · {p.Stats.CachedFiles} önbellekten";

        string? altAjan = null;
        if (p.SubagentTotals is { } sub && sub.Total > 0 && t.Total > 0)
            altAjan = $"alt ajanlar  {Bicim.Tam(sub.Total)}  ({Bicim.Yuzde((double)sub.Total / t.Total)})";

        return new AjanKarti(
            Ad: p.AgentName,
            Toplam: Bicim.Tam(t.Total),
            YeniGirdi: Bicim.Tam(t.InputFresh),
            Onbellek: Bicim.Tam(t.InputCached),
            OnbellekYuzde: Bicim.Yuzde(t.CacheHitRatio),
            Cikti: Bicim.Tam(t.Output),
            AltAjan: altAjan,
            Istatistik: istatistik,
            KotaVar: kota is not null,
            // Bayat ölçümde çubuk sıfır: dolu bir çubuk "durum bu" der, oysa bilmiyoruz.
            KotaOran: kota is { IsStale: false } ? Math.Clamp(kota.UsedPercent / 100.0, 0, 1) : 0,
            KotaBaslik: kotaBaslik,
            KotaAlt: kotaAlt,
            Uyari: p.Note is not null,
            UyariMetni: p.Note ?? "");
    }
}

/// <summary>Okunmayan ya da kurulu olmayan ajan — sebebiyle birlikte.</summary>
public sealed record PasifAjan(string Ad, string Sebep);

/// <summary>Oturum listesindeki tek satır.</summary>
public sealed record OturumSatiri(string Ajan, string Etiket, string Toplam);

/// <summary>
/// Küçük hâldeki rozette görünen tek satır — kısa sayı (<c>210,4M</c>) ve
/// varsa kalan kota.
/// </summary>
/// <remarks>
/// Ajanlar burada da <b>ayrı</b> satırlarda. Tek bir birleşik sayı
/// gösterilmiyor: 1000 Codex token'ı ile 1000 Claude token'ı aynı şey değil.
///
/// ⚠️ <b>"Kalan token" diye bir sayı yok.</b> Log'lar kota <b>yüzdesi</b>
/// veriyor, mutlak bir token limiti vermiyor. Elimizdeki en dürüst ifade
/// "kalan %41"; "kalan 84 milyon token" yazmak uydurma olurdu.
/// </remarks>
public sealed record KompaktSatir(
    string Ad,
    string Toplam,
    bool KalanVar,
    double KalanOran,
    string KalanMetni)
{
    public static KompaktSatir Olustur(ProviderResult p)
    {
        var kota = p.Quota;

        // Kota yoksa ya da bayatsa sayı GÖSTERİLMEZ, sebebi yazılır.
        var gecerli = kota is { IsStale: false };

        var metin = kota switch
        {
            null => "kotayı log'lar vermiyor",
            { IsStale: true } => $"kota {Bicim.NeKadarOnce(kota.MeasuredAt)} ölçülmüş — bayat",
            _ => $"kalan {Bicim.Yuzde(kota.RemainingPercent / 100)}" +
                 (kota.ResetsAt is { } r ? $" · {Bicim.NeKadarSonra(r)} yenilenir" : "")
        };

        return new KompaktSatir(
            Ad: p.AgentName,
            Toplam: Bicim.Kisa(p.Totals.Total),
            KalanVar: gecerli,
            KalanOran: gecerli ? kota!.RemainingPercent / 100.0 : 0,
            KalanMetni: metin);
    }
}

/// <summary>Bir taramanın ekrana hazır hâli.</summary>
public sealed record Gorunum(
    IReadOnlyList<AjanKarti> Kartlar,
    IReadOnlyList<KompaktSatir> Kompakt,
    IReadOnlyList<PasifAjan> Pasifler,
    IReadOnlyList<OturumSatiri> Oturumlar,
    string GenelToplam,
    bool GenelToplamGoster,
    string Durum,
    string ScanSaati,
    string IkonMetni,
    string Tooltip,
    DateTimeOffset SonEtkinlik)
{
    public static Gorunum Olustur(ScanResult scan, bool genelToplamGoster)
    {
        var aktif = scan.Active.ToList();

        var kartlar = aktif.Select(AjanKarti.Olustur).ToList();

        var pasifler = scan.Providers
            .Where(p => p.Status != ProviderStatus.Ok)
            .Select(p => new PasifAjan(p.AgentName, p.Status switch
            {
                ProviderStatus.NotInstalled => "bu makinede kurulu değil",
                ProviderStatus.Unsupported => p.Note ?? "okunmuyor",
                ProviderStatus.Failed => "hata: " + (p.Note ?? "bilinmiyor"),
                _ => ""
            }))
            .ToList();

        var oturumlar = aktif
            .SelectMany(p => p.Sessions.Select(s => (p.AgentName, s)))
            .OrderByDescending(x => x.s.Totals.Total)
            .Take(60)
            .Select(x => new OturumSatiri(
                x.AgentName,
                x.s.Label,
                Bicim.Tam(x.s.Totals.Total)))
            .ToList();

        var sonEtkinlik = aktif
            .SelectMany(p => p.Sessions)
            .Select(s => s.LastActivity)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

        var toplam = scan.GrandTotal.Total;

        var durum = $"{scan.ElapsedMs} ms · güncelleme {scan.ScannedAt:HH:mm}";

        // 📖 szTip 128 karakterle sınırlı — taşarsa Windows kırpar.
        var tooltip = "TokenBar\n" + string.Join("\n",
            kartlar.Select(k => $"{k.Ad}: {k.Toplam}"));
        if (tooltip.Length > 127) tooltip = tooltip[..127];

        var kompakt = aktif.Select(KompaktSatir.Olustur).ToList();

        return new Gorunum(
            Kartlar: kartlar,
            Kompakt: kompakt,
            Pasifler: pasifler,
            Oturumlar: oturumlar,
            GenelToplam: Bicim.Tam(toplam),
            GenelToplamGoster: genelToplamGoster,
            Durum: durum,
            ScanSaati: scan.ScannedAt.ToString("HH:mm"),
            IkonMetni: Bicim.Ikon(toplam),
            Tooltip: tooltip,
            SonEtkinlik: sonEtkinlik);
    }
}
