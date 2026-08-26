namespace TokenBar.Core;

/// <summary>
/// Bir sonraki taramaya ne kadar beklenecek. <b>Saf fonksiyon</b> — aynı
/// girdiye her zaman aynı çıktıyı verir, dışarıdan hiçbir şey okumaz.
/// </summary>
/// <remarks>
/// Saf olması tesadüf değil, tercih: test etmesi en kolay şey saf
/// fonksiyondur. Zamanı <c>DateTime.Now</c> ile içeriden okusaydı test
/// edilemezdi.
///
/// ⚠️ <b>Kota ve hata sinyalleri bilerek dışarıda bırakıldı.</b> "Kota doldu →
/// daha sık kontrol et → daha çok iş → ..." bir geri besleme döngüsü yaratır.
/// Bu tür döngüler sistemleri kilitler.
/// </remarks>
public static class RefreshPolicy
{
    public static readonly TimeSpan Minimum = TimeSpan.FromMinutes(2);

    public static TimeSpan Next(
        TimeSpan sincePopupOpened,
        TimeSpan sinceAgentActivity,
        bool powerSaver)
    {
        if (powerSaver) return TimeSpan.FromMinutes(30);

        // Kullanıcı pencereye az önce baktıysa taze veri bekliyordur.
        if (sincePopupOpened <= TimeSpan.FromMinutes(5)) return TimeSpan.FromMinutes(2);

        // Ajan az önce yazmışsa sayı değişiyor demektir.
        if (sinceAgentActivity <= TimeSpan.FromMinutes(5)) return TimeSpan.FromMinutes(5);

        if (sincePopupOpened <= TimeSpan.FromHours(1)) return TimeSpan.FromMinutes(5);
        if (sincePopupOpened <= TimeSpan.FromHours(4)) return TimeSpan.FromMinutes(15);

        return TimeSpan.FromMinutes(30);
    }
}
