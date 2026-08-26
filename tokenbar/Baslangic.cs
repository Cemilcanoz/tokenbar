using System.IO;
using Microsoft.Win32;

namespace tokenbar;

/// <summary>
/// "Windows açılışında başlat" — HKCU Run anahtarı.
/// </summary>
/// <remarks>
/// HKCU tercih edildi: yönetici hakkı gerektirmiyor ve yalnızca bu kullanıcıyı
/// etkiliyor. HKLM yazmak için UAC şart olurdu.
/// </remarks>
public static class Baslangic
{
    private const string Anahtar = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Ad = "TokenBar";

    public static bool Acik()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Anahtar);
            return key?.GetValue(Ad) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <returns>İşlem başarılı olduysa true.</returns>
    public static bool Ayarla(bool acik)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Anahtar, writable: true);
            if (key is null) return false;

            if (!acik)
            {
                key.DeleteValue(Ad, throwOnMissingValue: false);
                return true;
            }

            var yol = Environment.ProcessPath;
            if (string.IsNullOrEmpty(yol)) return false;

            // Tırnak şart: yolda boşluk varsa Windows ilk boşluktan böler.
            key.SetValue(Ad, $"\"{yol}\"");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
