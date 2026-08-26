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

        // Kapatma çıkış değil, gizleme. Uygulama tepside yaşamaya devam eder.
        _pencere.Closing += (_, args) =>
        {
            if (!_ayarlar.CloseToTray) return;
            args.Cancel = true;
            _pencere.Hide();
        };

        PencereyiGoster();
        _ = DonguAsync();
    }

    private void PencereyiGoster()
    {
        if (_pencere is null) return;

        _pencereAcildi = DateTimeOffset.Now;
        _pencere.Show();
        if (_pencere.WindowState == WindowState.Minimized)
            _pencere.WindowState = WindowState.Normal;
        _pencere.Activate();
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
