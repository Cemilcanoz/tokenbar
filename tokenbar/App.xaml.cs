using System.Windows;
using TokenBar.Core;
using WinForms = System.Windows.Forms;

// ⚠️ Application, MessageBox gibi adlar hem WPF'te hem WinForms'ta var ve
// UseWindowsForms açıkken ikisi de örtük using ile geliyor. Takma ad koymazsak
// derleyici "belirsiz başvuru" diyor.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace tokenbar;

/// <summary>
/// Uygulamanın çekirdeği. Pencere değil, <b>tepsi ikonu</b> asıl uygulama —
/// pencere yalnızca ona bakmanın bir yolu.
/// </summary>
public partial class App : Application
{
    private const string MutexAdi = "TokenBar.TekOrnek.v1";

    private Mutex? _mutex;
    private TrayIcon? _tray;
    private MainWindow? _pencere;
    private Settings _ayarlar = new();

    private readonly UsageScanner _tarayici = UsageScanner.Default();
    private readonly CancellationTokenSource _iptal = new();

    private DateTimeOffset _pencereAcildi = DateTimeOffset.MinValue;
    private DateTimeOffset _sonEtkinlik = DateTimeOffset.MinValue;
    private int _taramaSuruyor;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 🔴 Tek örnek. Yoksa uygulama iki kez açılır ve tepside iki ikon olur.
        _mutex = new Mutex(initiallyOwned: true, MutexAdi, out var ilkOrnek);
        if (!ilkOrnek)
        {
            MessageBox.Show("TokenBar zaten çalışıyor. Tepsi ikonuna bak.",
                "TokenBar", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 🔴 Bu satır olmadan penceresiz uygulama AÇILIR AÇILMAZ kapanır.
        // Varsayılan OnLastWindowClose, açık pencere olmadığı an çıkış yapar.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _ayarlar = Settings.Load();

        _tray = new TrayIcon(Baslangic.Acik());
        _tray.GosterIstendi += PencereyiGoster;
        _tray.YenileIstendi += async () => await TaraAsync(zorla: true);
        _tray.CikisIstendi += Cik;
        _tray.BaslangicDegisti += BaslangiciAyarla;

        _pencere = new MainWindow();
        _pencere.YenileIstendi += async () => await TaraAsync(zorla: true);
        _pencere.GenelToplamDegisti += acik =>
        {
            _ayarlar.ShowGrandTotal = acik;
            _ayarlar.Save();
        };
        _pencere.KonumDegisti += KonumuKaydet;

        // Kapatma çıkış değil, gizleme. Uygulama tepside yaşamaya devam eder.
        _pencere.Closing += (_, args) =>
        {
            if (!_ayarlar.CloseToTray) return;
            args.Cancel = true;
            _pencere.Hide();
        };

        KonumuGeriYukle();
        _pencere.Show();
        if (_ayarlar.StartExpanded) _pencere.Genislet();

        _ = DonguAsync();
    }

    private void PencereyiGoster()
    {
        if (_pencere is null) return;

        _pencereAcildi = DateTimeOffset.Now;
        _pencere.Show();
        if (_pencere.WindowState == WindowState.Minimized)
            _pencere.WindowState = WindowState.Normal;

        _pencere.Genislet();
        _pencere.Activate();
    }

    /// <summary>
    /// Rozetin son konumu. İlk açılışta sağ alt köşe — orası tepsiye en yakın
    /// ve genelde en az meşgul olan yer.
    /// </summary>
    private void KonumuGeriYukle()
    {
        if (_pencere is null) return;

        var alan = SystemParameters.WorkArea;

        if (_ayarlar.Left is { } x && _ayarlar.Top is { } y)
        {
            // ⚠️ Monitör sökülmüş olabilir: kayıtlı konum artık ekran dışında
            // kalırsa pencere görünmez olur ve kullanıcı onu bulamaz.
            _pencere.Left = Math.Clamp(x, alan.Left, Math.Max(alan.Left, alan.Right - _pencere.Width));
            _pencere.Top = Math.Clamp(y, alan.Top, Math.Max(alan.Top, alan.Bottom - _pencere.Height));
            return;
        }

        _pencere.Left = alan.Right - _pencere.Width - 16;
        _pencere.Top = alan.Bottom - _pencere.Height - 16;
    }

    private void KonumuKaydet()
    {
        if (_pencere is null) return;

        _ayarlar.Left = _pencere.Left;
        _ayarlar.Top = _pencere.Top;
        _ayarlar.StartExpanded = _pencere.Genis;
        _ayarlar.Save();
    }

    /// <summary>
    /// Yenileme döngüsü.
    /// </summary>
    /// <remarks>
    /// <see cref="PeriodicTimer"/> seçildi çünkü turlar <b>üst üste binmiyor</b>:
    /// bir tarama uzun sürerse bir sonraki tik beklenir. <c>Period</c> her turda
    /// yeniden hesaplanıyor — kullanıcı pencereye bakıyorsa sık, günlerdir
    /// bakmıyorsa seyrek.
    ///
    /// ⚠️ <c>FileSystemWatcher</c> bilerek kullanılmadı: olay tamponu taşınca
    /// olaylar sessizce kaybolur, ağ/senkron klasörlerinde hiç çalışmayabilir.
    /// Polling yavaş ama <b>yanlış değil</b>.
    /// </remarks>
    private async Task DonguAsync()
    {
        await TaraAsync(zorla: true);

        using var zamanlayici = new PeriodicTimer(RefreshPolicy.Minimum);
        try
        {
            while (await zamanlayici.WaitForNextTickAsync(_iptal.Token))
            {
                await TaraAsync(zorla: false);

                zamanlayici.Period = RefreshPolicy.Next(
                    sincePopupOpened: DateTimeOffset.Now - _pencereAcildi,
                    sinceAgentActivity: DateTimeOffset.Now - _sonEtkinlik,
                    powerSaver: GucTasarrufunda());
            }
        }
        catch (OperationCanceledException)
        {
            // Çıkış — normal.
        }
    }

    private async Task TaraAsync(bool zorla)
    {
        // Aynı anda iki tarama olmasın.
        if (Interlocked.Exchange(ref _taramaSuruyor, 1) == 1) return;

        try
        {
            if (zorla) _pencere?.MesgulGoster();

            var sonuc = await Task.Run(() => _tarayici.Scan(_iptal.Token), _iptal.Token);
            var gorunum = Gorunum.Olustur(sonuc, _ayarlar.ShowGrandTotal);

            _sonEtkinlik = gorunum.SonEtkinlik;
            _tray?.Guncelle(gorunum.IkonMetni, gorunum.Tooltip);
            _pencere?.Goster(gorunum);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _pencere?.HataGoster(ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _taramaSuruyor, 0);
        }
    }

    private void BaslangiciAyarla(bool acik)
    {
        var oldu = Baslangic.Ayarla(acik);
        _ayarlar.StartWithWindows = oldu && acik;
        _ayarlar.Save();

        // Kayıt defterine yazamadıysak menüdeki işareti geri al —
        // kullanıcı açık sansın da çalışmasın olmaz.
        if (!oldu) _tray?.BaslangicIsaretle(Baslangic.Acik());
    }

    /// <summary>Pilde ve şarj düşükse tarama seyrekleşsin.</summary>
    private static bool GucTasarrufunda()
    {
        var guc = WinForms.SystemInformation.PowerStatus;
        return guc.PowerLineStatus == WinForms.PowerLineStatus.Offline &&
               guc.BatteryLifePercent is > 0 and < 0.30f;
    }

    private void Cik()
    {
        _iptal.Cancel();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _iptal.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
