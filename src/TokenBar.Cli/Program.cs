using System.Diagnostics;
using System.Globalization;
using System.Text;
using TokenBar.Cli;

// Türkçe karakterler ve binlik ayracı için.
Console.OutputEncoding = Encoding.UTF8;
var tr = new CultureInfo("tr-TR");

// --detail: her oturumu ayrı ayrı yaz (SQLite kehanetiyle karşılaştırmak için)
var detail = args.Contains("--detail");

var sessionsDirectory = CodexSessionReader.FindSessionsDirectory();
Console.WriteLine($"Klasör: {sessionsDirectory}");

if (!Directory.Exists(sessionsDirectory))
{
    Console.WriteLine("Klasör bulunamadı. Codex CLI kurulu mu?");
    return 1;
}

var stopwatch = Stopwatch.StartNew();

var results = new List<SessionResult>();
var unreadable = new List<(string Path, string Error)>();

foreach (var path in CodexSessionReader.EnumerateSessionFiles(sessionsDirectory))
{
    try
    {
        results.Add(CodexSessionReader.ReadFile(path));
    }
    catch (IOException ex)
    {
        unreadable.Add((path, ex.Message));
    }
    catch (UnauthorizedAccessException ex)
    {
        unreadable.Add((path, ex.Message));
    }
}

stopwatch.Stop();

// ⭐ Her dosyanın SON değeri kümülatif. Dosyalar arası toplama yapılır.
var totals = results
    .Where(r => r.HasTokenCount)
    .Aggregate(TokenUsage.Zero, (sum, r) => sum + r.Usage);

var withCount = results.Count(r => r.HasTokenCount);
var malformed = results.Sum(r => r.MalformedLines);
var resets = results.Count(r => r.CounterReset);

if (detail)
{
    foreach (var r in results.Where(r => r.HasTokenCount).OrderBy(r => r.Path))
        Console.WriteLine($"{r.Usage.Total}	{r.Path}");
    return 0;
}

Console.WriteLine();
Console.WriteLine($"Dosya: {results.Count}    token_count bulunan: {withCount}    bulunmayan: {results.Count - withCount}");
Console.WriteLine($"Bozuk satır: {malformed}");
Console.WriteLine();
Console.WriteLine($"input:  {totals.InputTokens,15:N0}   (bunun {totals.CachedInputTokens:N0}'si önbellekten)".Replace("  ", "  "));
Console.WriteLine($"output: {totals.OutputTokens,15:N0}   (bunun {totals.ReasoningOutputTokens:N0}'i reasoning)");
Console.WriteLine($"TOPLAM: {totals.Total,15:N0}");
Console.WriteLine();
Console.WriteLine($"Süre: {stopwatch.ElapsedMilliseconds} ms");

if (unreadable.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"⚠️  Açılamayan dosya: {unreadable.Count}");
    foreach (var (path, error) in unreadable)
        Console.WriteLine($"    {Path.GetFileName(path)}: {error}");
}

// ❓ Açık soru: context_compacted sayacı sıfırlıyor mu?
Console.WriteLine();
Console.WriteLine(resets == 0
    ? "✅ Hiçbir dosyada sayaç geriye gitmedi — 'son değeri al' güvenli."
    : $"🔴 {resets} dosyada sayaç geriye gitti — 'son değeri al' o dosyalarda EKSİK sayıyor.");

// Doğrulama: bilinen referans sayılarla karşılaştır.
const long expectedInput = 209_443_513;
const long expectedCached = 197_488_512;
const long expectedOutput = 962_477;
const long expectedReasoning = 200_381;

Console.WriteLine();
Console.WriteLine("Referans karşılaştırması");
Check("input_tokens", totals.InputTokens, expectedInput);
Check("cached_input_tokens", totals.CachedInputTokens, expectedCached);
Check("output_tokens", totals.OutputTokens, expectedOutput);
Check("reasoning_output_tokens", totals.ReasoningOutputTokens, expectedReasoning);
Check("TOPLAM", totals.Total, expectedInput + expectedOutput);

return 0;

void Check(string name, long actual, long expected)
{
    var mark = actual == expected ? "✅" : "❌";
    var diff = actual - expected;
    var note = diff == 0 ? "" : $"  (fark: {diff:+#,##0;-#,##0})";
    Console.WriteLine($"  {mark} {name,-24} {actual,15:N0}{note}");
}
