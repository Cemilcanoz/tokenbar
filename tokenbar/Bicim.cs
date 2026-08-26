using System.Globalization;

namespace tokenbar;

/// <summary>Sayı biçimlendirme. Tek yerde, çünkü üç ayrı yerde gösteriliyor.</summary>
public static class Bicim
{
    public static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>Tam sayı: <c>210.405.990</c></summary>
    public static string Tam(long value) => value.ToString("N0", Tr);

    /// <summary>
    /// Kısa sayı: <c>210,4M</c>. Tepsi ikonuna 2-3 karakter sığıyor,
    /// tooltip'e 128 karakter — ikisinde de tam sayı gösterilemez.
    /// </summary>
    public static string Kisa(long value) => value switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000d).ToString("0.#", Tr) + "B",
        >= 1_000_000 => (value / 1_000_000d).ToString("0.#", Tr) + "M",
        >= 1_000 => (value / 1_000d).ToString("0.#", Tr) + "K",
        _ => value.ToString(Tr)
    };

    /// <summary>Tepsi ikonuna çizilecek metin — en fazla 4 karakter.</summary>
    public static string Ikon(long value) => value switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000d).ToString("0", Tr) + "B",
        >= 10_000_000 => (value / 1_000_000d).ToString("0", Tr) + "M",
        >= 1_000_000 => (value / 1_000_000d).ToString("0.#", Tr) + "M",
        >= 1_000 => (value / 1_000d).ToString("0", Tr) + "K",
        _ => value.ToString(Tr)
    };

    public static string Yuzde(double oran) => "%" + (oran * 100).ToString("0.#", Tr);

    /// <summary>"3 dakika önce", "2 gün önce" — bayat veriyi görünür kılar.</summary>
    public static string NeKadarOnce(DateTimeOffset an)
    {
        var fark = DateTimeOffset.Now - an;

        if (fark < TimeSpan.Zero) return "az önce";
        if (fark < TimeSpan.FromMinutes(1)) return "az önce";
        if (fark < TimeSpan.FromHours(1)) return $"{(int)fark.TotalMinutes} dk önce";
        if (fark < TimeSpan.FromDays(1)) return $"{(int)fark.TotalHours} saat önce";
        return $"{(int)fark.TotalDays} gün önce";
    }
}
