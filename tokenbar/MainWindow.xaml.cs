using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using TokenBar.Cli;

namespace tokenbar;

/// <summary>
/// Pencerenin kod tarafı. XAML'deki x:Name'ler burada birer alan gibi görünür —
/// ikisi "partial class" ile birleşiyor.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly CultureInfo Tr = new("tr-TR");

    public MainWindow()
    {
        InitializeComponent();

        // Pencere ekrana geldiği anda ilk taramayı başlat.
        // Yapıcı metodun (constructor) içinde okumaya kalkarsak pencere daha
        // çizilmeden dosya okumaya başlarız; kullanıcı boş ekrana bakar.
        Loaded += async (_, _) => await YenileAsync();
    }

    private async void YenileButton_Click(object sender, RoutedEventArgs e)
        => await YenileAsync();

    private async Task YenileAsync()
    {
        YenileButton.IsEnabled = false;
        DurumText.Text = "okunuyor…";
        HataText.Visibility = Visibility.Collapsed;

        try
        {
            // ⭐ Task.Run: dosya okuma arka plan thread'ine gidiyor.
            // Doğrudan Tara() çağırsaydık 46 dosya okunurken pencere donar,
            // Windows "yanıt vermiyor" derdi. await ile sonucu bekleyip
            // UI thread'ine geri dönüyoruz.
            var tarama = await Task.Run(Tara);
            Goster(tarama);
        }
        catch (Exception ex)
        {
            HataText.Text = ex.Message;
            HataText.Visibility = Visibility.Visible;
            DurumText.Text = "okunamadı";
        }
        finally
        {
            // finally: hata olsa da olmasa da buton geri açılsın.
            YenileButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Arka planda çalışır. ⚠️ Burada hiçbir arayüz elemanına dokunma —
    /// WPF kontrollerine sadece kendi thread'inden erişilebilir.
    /// </summary>
    private static Tarama Tara()
    {
        var klasor = CodexSessionReader.FindSessionsDirectory();

        if (!Directory.Exists(klasor))
            throw new DirectoryNotFoundException(
                $"Codex oturum klasörü bulunamadı:\n{klasor}");

        var kronometre = Stopwatch.StartNew();

        var sonuclar = new List<SessionResult>();
        var acilamayan = 0;

        foreach (var yol in CodexSessionReader.EnumerateSessionFiles(klasor))
        {
            try
            {
                sonuclar.Add(CodexSessionReader.ReadFile(yol));
            }
            catch (IOException)
            {
                // Tek bozuk dosya yüzünden tüm tarama çökmesin; say ve devam et.
                acilamayan++;
            }
            catch (UnauthorizedAccessException)
            {
                acilamayan++;
            }
        }

        kronometre.Stop();

        var dolu = sonuclar.Where(s => s.HasTokenCount).ToList();

        // ⭐ Her dosyanın SON değeri kümülatif. Dosyalar arası toplanır.
        var toplam = dolu.Aggregate(TokenUsage.Zero, (a, s) => a + s.Usage);

        var satirlar = dolu
            .OrderByDescending(s => s.Usage.Total)
            .Select(s => new OturumSatiri(
                File.GetLastWriteTime(s.Path).ToString("d MMMM yyyy  HH:mm", Tr),
                s.Usage.Total.ToString("N0", Tr)))
            .ToList();

        return new Tarama(
            DosyaSayisi: sonuclar.Count,
            OturumSayisi: dolu.Count,
            BozukSatir: sonuclar.Sum(s => s.MalformedLines),
            AcilamayanDosya: acilamayan,
            SureMs: kronometre.ElapsedMilliseconds,
            Toplam: toplam,
            Oturumlar: satirlar);
    }

    /// <summary>Sonucu ekrana yazar. UI thread'inde çalışır.</summary>
    private void Goster(Tarama t)
    {
        ToplamText.Text = t.Toplam.Total.ToString("N0", Tr);
        OzetText.Text = $"toplam token · {t.OturumSayisi} oturum";

        GirdiText.Text = t.Toplam.InputTokens.ToString("N0", Tr);
        OnbellekText.Text = $"{t.Toplam.CachedInputTokens.ToString("N0", Tr)}   {Yuzde(t.Toplam.CachedInputTokens, t.Toplam.InputTokens)}";

        CiktiText.Text = t.Toplam.OutputTokens.ToString("N0", Tr);
        ReasoningText.Text = $"{t.Toplam.ReasoningOutputTokens.ToString("N0", Tr)}   {Yuzde(t.Toplam.ReasoningOutputTokens, t.Toplam.OutputTokens)}";

        OturumListesi.ItemsSource = t.Oturumlar;

        var durum = $"{t.DosyaSayisi} dosya · {t.BozukSatir} bozuk satır · {t.SureMs} ms";
        if (t.AcilamayanDosya > 0)
            durum += $" · {t.AcilamayanDosya} dosya açılamadı";
        DurumText.Text = durum;
    }

    /// <summary>Türkçede yüzde işareti sayının önüne gelir: %94,3</summary>
    private static string Yuzde(long parca, long butun) =>
        butun == 0 ? "" : "%" + (100.0 * parca / butun).ToString("0.#", Tr);
}
