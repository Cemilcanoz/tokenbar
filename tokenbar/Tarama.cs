using TokenBar.Cli;

namespace tokenbar;

/// <summary>
/// Bir taramanın tüm sonucu. Arayüz sadece bunu görür.
/// </summary>
/// <remarks>
/// Neden ayrı bir tip? Çünkü tarama arka planda çalışıyor, ekrana yazma ise
/// UI thread'inde. Aradan tek bir nesne geçsin ki hangi verinin nereden
/// geldiği belli olsun. Ayrıca <c>record</c> değiştirilemez — arka plan işi
/// bitirdikten sonra kimse içeriğini bozamaz.
/// </remarks>
public sealed record Tarama(
    int DosyaSayisi,
    int OturumSayisi,
    int BozukSatir,
    int AcilamayanDosya,
    long SureMs,
    TokenUsage Toplam,
    IReadOnlyList<OturumSatiri> Oturumlar);

/// <summary>
/// Listede görünen tek satır. İkisi de <c>string</c> — biçimlendirme
/// arka planda bitiyor, XAML'de dönüştürücü (converter) gerekmiyor.
/// </summary>
public sealed record OturumSatiri(string Tarih, string Toplam);
