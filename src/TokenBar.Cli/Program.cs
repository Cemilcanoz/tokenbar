using System.Globalization;
using System.Text;
using TokenBar.Core;

// Türkçe karakterler ve binlik ayracı için.
Console.OutputEncoding = Encoding.UTF8;
var tr = new CultureInfo("tr-TR");

// --detail: her oturumu ayrı ayrı yaz (SQLite kehanetiyle karşılaştırmak için)
var detail = args.Contains("--detail");

var scanner = UsageScanner.Default();
var scan = scanner.Scan();

foreach (var provider in scan.Providers)
{
    Console.WriteLine();
    Console.WriteLine($"── {provider.AgentName} ──");

    switch (provider.Status)
    {
        case ProviderStatus.NotInstalled:
            Console.WriteLine("   kurulu değil");
            continue;

        case ProviderStatus.Unsupported:
            Console.WriteLine($"   okunmuyor — {provider.Note}");
            Console.WriteLine($"   ({provider.Stats.Files} dosya bulundu)");
            continue;

        case ProviderStatus.Failed:
            Console.WriteLine($"   HATA — {provider.Note}");
            continue;
    }

    var t = provider.Totals;
    var s = provider.Stats;

    Console.WriteLine($"   dosya {s.Files}  ·  veri olan {s.FilesWithData}  ·  bozuk satır {s.MalformedLines}"
                      + (s.DedupedLines > 0 ? $"  ·  tekrar {s.DedupedLines}" : "")
                      + (s.UnreadableFiles > 0 ? $"  ·  açılamayan {s.UnreadableFiles}" : ""));
    Console.WriteLine();
    Console.WriteLine($"   yeni girdi    {t.InputFresh,15:N0}");
    Console.WriteLine($"   önbellekten   {t.InputCached,15:N0}   (%{t.CacheHitRatio * 100:0.#})");
    Console.WriteLine($"   önbelleğe     {t.CacheWrite,15:N0}");
    Console.WriteLine($"   çıktı         {t.Output,15:N0}"
                      + (t.Reasoning > 0 ? $"   (bunun {t.Reasoning:N0}'i reasoning)" : ""));
    Console.WriteLine($"   TOPLAM        {t.Total,15:N0}");

    if (provider.SubagentTotals is { } sub && sub.Total > 0)
        Console.WriteLine($"   ↳ alt ajan    {sub.Total,15:N0}   (%{100.0 * sub.Total / t.Total:0.#})");

    if (provider.Quota is { } q)
    {
        Console.WriteLine();
        Console.WriteLine($"   kota  %{q.UsedPercent:0.#} kullanılmış  ({q.WindowLabel} pencere, plan: {q.PlanType})");
        Console.WriteLine($"         ölçüm {q.MeasuredAt.ToLocalTime():d MMM yyyy HH:mm} — {(int)q.Age.TotalDays} gün önce");
    }

    if (provider.Note is not null)
        Console.WriteLine($"   {provider.Note}");

    if (detail)
    {
        Console.WriteLine();
        foreach (var session in provider.Sessions)
            Console.WriteLine($"   {session.Totals.Total,15:N0}  {session.Label}");
    }
}

Console.WriteLine();
Console.WriteLine($"Süre: {scan.ElapsedMs} ms   ·   önbellekten gelen dosya: "
                  + string.Join(", ", scan.Active.Select(p => $"{p.AgentName} {p.Stats.CachedFiles}")));

// ── Doğrulama ───────────────────────────────────────────────────────────
// Referans sayılar PowerShell ile bağımsız olarak hesaplandı.
Console.WriteLine();
Console.WriteLine("Referans karşılaştırması");

var codex = scan.Providers.FirstOrDefault(p => p.AgentName == "Codex CLI");
if (codex?.Status == ProviderStatus.Ok)
{
    Console.WriteLine("  Codex CLI");
    Check("önbellekten", codex.Totals.InputCached, 197_488_512);
    Check("çıktı", codex.Totals.Output, 962_477);
    Check("reasoning", codex.Totals.Reasoning, 200_381);
    Check("TOPLAM", codex.Totals.Total, 210_405_990);
}

// ⚠️ Claude Code ŞU AN çalışıyor. Sabit bir referans sayı yazarsan dakikalar
// içinde eskir ve doğru parser'ı ❌ diye işaretler. Bu yüzden kontrol sadece
// TOKENBAR_CLAUDE_HOME dondurulmuş bir kopyaya bakıyorken anlamlı.
var claude = scan.Providers.FirstOrDefault(p => p.AgentName == "Claude Code");
if (claude?.Status == ProviderStatus.Ok)
{
    var frozen = Environment.GetEnvironmentVariable("TOKENBAR_CLAUDE_HOME");
    if (frozen is null)
    {
        Console.WriteLine("  Claude Code — atlandı");
        Console.WriteLine("    Ajan canlı; sayı her dakika değişiyor, sabit referansla");
        Console.WriteLine("    karşılaştırılamaz. Dondurulmuş kopyaya karşı çalıştır:");
        Console.WriteLine("      $env:TOKENBAR_CLAUDE_HOME = \"<kopya>\"; dotnet run");
    }
    else
    {
        Console.WriteLine($"  Claude Code — dondurulmuş kopya");
        Check("yeni girdi", claude.Totals.InputFresh, 45_742);
        Check("önbellekten", claude.Totals.InputCached, 214_524_132);
        Check("önbelleğe", claude.Totals.CacheWrite, 8_319_463);
        Check("çıktı", claude.Totals.Output, 1_787_389);
        Check("TOPLAM", claude.Totals.Total, 224_676_726);
    }
}

return 0;

void Check(string name, long actual, long expected)
{
    var mark = actual == expected ? "✅" : "❌";
    var diff = actual - expected;
    var note = diff == 0 ? "" : $"  (fark: {diff:+#,##0;-#,##0})";
    Console.WriteLine($"    {mark} {name,-14} {actual,15:N0}{note}");
}
