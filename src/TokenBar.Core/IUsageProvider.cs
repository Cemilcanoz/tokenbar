namespace TokenBar.Core;

/// <summary>
/// Bir AI kodlama ajanının token kullanımını okuyan kaynak.
/// </summary>
/// <remarks>
/// Yeni ajan eklemek = bu arayüzü uygulayan <b>tek bir yeni dosya</b>.
/// Arayüz kodu hiç değişmez.
///
/// ⚠️ Bu soyutlama Faz 1'de bilerek yazılmadı: tek sağlayıcı varken arayüz
/// soyutlama değil, süs olur. İkinci sağlayıcı gelince yazıldı.
/// </remarks>
public interface IUsageProvider
{
    /// <summary>Kullanıcıya görünen ad.</summary>
    string Name { get; }

    /// <summary>Bu ajan makinede kurulu mu (veri klasörü var mı).</summary>
    bool IsInstalled();

    /// <summary>
    /// Tek turluk tarama. <paramref name="cache"/> değişmemiş dosyaları
    /// yeniden okumamak için kullanılır.
    /// </summary>
    ProviderResult Scan(FileCache cache, CancellationToken ct);
}
