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
        var kotaBaslik = kota is null
            ? ""
            : $"kota  {Bicim.Yuzde(kota.UsedPercent / 100)} kullanılmış";

        // ⚠️ Kota kümülatif değil, anlık durum. Ölçüm tarihini göstermezsek
        // 26 gün önceki değeri "şu an" sanır.
        var kotaAlt = kota is null
            ? ""
            : $"{kota.WindowLabel} pencere · {kota.PlanType} · ölçüm {Bicim.NeKadarOnce(kota.MeasuredAt)}";

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
            KotaOran: Math.Clamp((kota?.UsedPercent ?? 0) / 100.0, 0, 1),
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

/// <summary>Bir taramanın ekrana hazır hâli.</summary>
public sealed record Gorunum(
    IReadOnlyList<AjanKarti> Kartlar,
    IReadOnlyList<PasifAjan> Pasifler,
    IReadOnlyList<OturumSatiri> Oturumlar,
    string GenelToplam,
    bool GenelToplamGoster,
    string Durum,
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

        return new Gorunum(
            Kartlar: kartlar,
            Pasifler: pasifler,
            Oturumlar: oturumlar,
            GenelToplam: Bicim.Tam(toplam),
            GenelToplamGoster: genelToplamGoster,
            Durum: durum,
            IkonMetni: Bicim.Ikon(toplam),
            Tooltip: tooltip,
            SonEtkinlik: sonEtkinlik);
    }
}
