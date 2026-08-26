using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenBar.Core;

/// <summary>
/// Kullanıcı ayarları. <c>%APPDATA%\TokenBar\settings.json</c>.
/// </summary>
/// <remarks>
/// ⭐ <see cref="Version"/> alanı boşuna değil: şemalar sessizce kayıyor
/// (Claude Code'un <c>sessionId</c> / <c>session_id</c> ikilisi bunun canlı
/// örneği). Sürüm numarası olmayan bir kalıcı dosya, ileride ya çökme ya da
/// sessiz yanlış davranış üretir.
/// </remarks>
public sealed class Settings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Windows açılışında kendiliğinden başlasın mı.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Ajanlar arası genel toplam gösterilsin mi. Varsayılan <b>kapalı</b> —
    /// 1000 Codex token'ı ile 1000 Claude token'ı aynı şey değil.
    /// </summary>
    public bool ShowGrandTotal { get; set; }

    /// <summary>Kapatınca tepside kalsın mı, yoksa gerçekten çıksın mı.</summary>
    public bool CloseToTray { get; set; } = true;

    [JsonIgnore]
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TokenBar",
        "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<Settings>(json);

            // Bilinmeyen (ileri) sürüm: dosyayı ezme, varsayılanla çalış.
            if (loaded is null || loaded.Version > CurrentVersion) return new Settings();

            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Ayar okunamadı diye uygulama açılmamazlık etmesin.
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(this, JsonOptions);

            // Önce geçici dosyaya yaz, sonra yerine koy: yazarken elektrik
            // giderse yarım bir settings.json kalmaz.
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ayar kaydedilemedi — sessizce geç, uygulama çalışmaya devam etsin.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
