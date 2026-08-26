namespace TokenBar.Core;

/// <summary>
/// Antigravity — <b>bilerek okunmuyor.</b>
/// </summary>
/// <remarks>
/// Veri var: <c>%USERPROFILE%\.gemini\antigravity\conversations\*.db</c> ve
/// yanlarında <c>*.pb</c> dosyaları. Ama:
///
/// <list type="bullet">
/// <item>Format SQLite + <b>protobuf BLOB</b> — şema dokümante değil.</item>
/// <item>🌐 Bilinen tek araç ham token bulamadığı için <b>tur sayısından
/// tahmin</b> ediyor. Bu iddia bu makinede doğrulanmadı.</item>
/// </list>
///
/// 🔴 Tahmini bir sayıyı kesin sayıyla aynı görünümde göstermek kullanıcıyı
/// kandırmak olur. Doğrulanmamış bir parser yazıp "Antigravity: 12.3M" demek,
/// hiç göstermemekten <b>daha kötü</b> — çünkü yanlış olduğu belli olmaz.
///
/// Bu yüzden sağlayıcı yalnızca <b>varlığı tespit ediyor</b> ve sebebini
/// söylüyor. Şema ölçüldüğünde burası dolacak.
/// </remarks>
public sealed class AntigravityProvider : IUsageProvider
{
    public string Name => "Antigravity";

    private static IEnumerable<string> CandidateDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".gemini", "antigravity", "conversations");
        yield return Path.Combine(home, ".gemini", "antigravity-ide", "conversations");
        yield return Path.Combine(home, ".gemini", "antigravity-cli", "conversations");
    }

    public bool IsInstalled() => CandidateDirectories().Any(Directory.Exists);

    public ProviderResult Scan(FileCache cache, CancellationToken ct)
    {
        var files = 0;

        foreach (var directory in CandidateDirectories().Where(Directory.Exists))
        {
            ct.ThrowIfCancellationRequested();
            files += Directory.EnumerateFiles(directory, "*.db", SearchOption.TopDirectoryOnly).Count();
        }

        if (files == 0)
            return ProviderResult.NotInstalled(Name);

        return ProviderResult.Unsupported(
            Name,
            "SQLite + protobuf; ham token sayısı yok. Tahmin yerine hiç göstermiyoruz.",
            files);
    }
}
