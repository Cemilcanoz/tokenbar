using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

// ⚠️ UseWindowsForms açık olduğu için Point ve MouseEventArgs iki ayrı ad
// alanından birden geliyor (System.Drawing / System.Windows.Forms ve WPF'inki).
// Tepsi ikonu WinForms'tan geliyor, bu pencere WPF — hangisini kastettiğimizi
// açıkça söylemek zorundayız.
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Size = System.Windows.Size;

namespace tokenbar;

/// <summary>
/// Yüzen rozet. Tıklayınca büyüyüp asıl paneli açıyor.
/// </summary>
/// <remarks>
/// ⚠️ Bu pencere <b>hiç dosya okumuyor.</b> Taramayı <see cref="App"/> yapıyor,
/// buraya hazır bir <see cref="Gorunum"/> geliyor. Ayrım kasıtlı: pencere
/// kapalıyken de tepsi güncellenmeye devam ediyor.
/// </remarks>
public partial class MainWindow : Window
{
    private const double KompaktGenislik = 286;
    private const double GenisGenislik = 470;
    private const double GenisYukseklik = 780;

    /// <summary>Sürükleme mi tıklama mı — bu eşiğin altındaki hareket tıklamadır.</summary>
    private const double SurukleEsigi = 4;

    private bool _genis;
    private bool _genelToplamAcik;

    private Point _basincKonum;
    private bool _basili;
    private bool _suruklendi;

    private double _kompaktYukseklik = 210;

    public event Action? YenileIstendi;
    public event Action<bool>? GenelToplamDegisti;
    public event Action? KonumDegisti;

    public MainWindow()
    {
        InitializeComponent();

        // Esc = küçült. Kenarlıksız pencerede kaçış yolu şart.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _genis) Kucult();
        };

        // Rozet hâlindeyken soluk dursun, üstüne gelince belirginleşsin.
        MouseEnter += (_, _) => { if (!_genis) Opacity = 1.0; };
        MouseLeave += (_, _) => { if (!_genis) Opacity = 0.82; };

        Opacity = 0.82;
    }

    public bool Genis => _genis;

    // ── Sürükleme ve tıklama ────────────────────────────────────────────
    //
    // 🔴 DragMove() çağırdığın an fare olayını yutuyor: MouseUp hiç gelmiyor.
    // Bu yüzden "tıkla → büyüsün" ile "sürükle → taşınsın" doğrudan çakışıyor.
    // Çözüm: basıldığı yeri sakla, fare 4 pikselden fazla kaydıysa sürükleme
    // say, kaymadıysa tıklama say.

    private void Kabuk_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _basincKonum = e.GetPosition(this);
        _basili = true;
        _suruklendi = false;
    }

    private void Kabuk_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_basili || e.LeftButton != MouseButtonState.Pressed) return;

        var simdi = e.GetPosition(this);
        if (Math.Abs(simdi.X - _basincKonum.X) < SurukleEsigi &&
            Math.Abs(simdi.Y - _basincKonum.Y) < SurukleEsigi) return;

        _suruklendi = true;
        _basili = false;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Düğme bu arada bırakılmışsa DragMove atar. Zararsız.
        }

        KonumDegisti?.Invoke();
    }

    private void Kabuk_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var tiklama = _basili && !_suruklendi;
        _basili = false;

        // Geniş hâlde gövdeye tıklamak paneli kapatmasın — sadece rozet açar.
        if (tiklama && !_genis) Genislet();
    }

    // ── Büyüme / küçülme ────────────────────────────────────────────────

    public void Genislet()
    {
        if (_genis) return;
        _genis = true;

        KompaktPanel.Visibility = Visibility.Collapsed;
        GenisPanel.Visibility = Visibility.Visible;

        Opacity = 1.0;
        Boyutla(GenisGenislik, GenisYukseklik);
    }

    public void Kucult()
    {
        if (!_genis) return;
        _genis = false;

        GenisPanel.Visibility = Visibility.Collapsed;
        KompaktPanel.Visibility = Visibility.Visible;

        // Ölçüm ancak panel görünür olduktan SONRA anlamlı — gizli bir
        // elemanın DesiredSize'ı sıfırdır.
        _kompaktYukseklik = KompaktYukseklikHesapla();

        Opacity = IsMouseOver ? 1.0 : 0.82;
        Boyutla(KompaktGenislik, _kompaktYukseklik);
    }

    /// <summary>
    /// Boyutu yumuşak geçişle değiştirir.
    /// </summary>
    /// <remarks>
    /// Animasyon bittiğinde <c>BeginAnimation(..., null)</c> ile animasyonu
    /// kaldırıyoruz. Yoksa animasyon değeri özelliği <b>kilitler</b> ve
    /// sonradan <c>Width = …</c> yazmak hiçbir şey yapmaz — WPF'in klasik
    /// tuzağı.
    /// </remarks>
    private void Boyutla(double genislik, double yukseklik)
    {
        var sure = TimeSpan.FromMilliseconds(160);
        var yumusatma = new CubicEase { EasingMode = EasingMode.EaseOut };

        var g = new DoubleAnimation(genislik, sure) { EasingFunction = yumusatma };
        var y = new DoubleAnimation(yukseklik, sure) { EasingFunction = yumusatma };

        g.Completed += (_, _) => { BeginAnimation(WidthProperty, null); Width = genislik; };
        y.Completed += (_, _) => { BeginAnimation(HeightProperty, null); Height = yukseklik; EkranaSigdir(); };

        BeginAnimation(WidthProperty, g);
        BeginAnimation(HeightProperty, y);
    }

    /// <summary>
    /// Rozetin içeriğinin gerçekte kaç piksel tuttuğunu WPF'e sordurur.
    /// </summary>
    /// <remarks>
    /// <c>Measure</c> "bu genişlikte, sınırsız yükseklikte kaç piksel yer
    /// kaplarsın?" demek; cevabı <c>DesiredSize</c>'a yazıyor. Sabit bir
    /// formül yazsaydık kota satırı gizlendiğinde ya da metin iki satıra
    /// sardığında tutmazdı.
    ///
    /// +26 dış kabuğun payı: <c>Margin="12"</c> (üst+alt 24) ve
    /// <c>BorderThickness="1"</c> (üst+alt 2).
    /// </remarks>
    private double KompaktYukseklikHesapla()
    {
        KompaktPanel.Measure(new Size(KompaktGenislik - 26, double.PositiveInfinity));
        return Math.Ceiling(KompaktPanel.DesiredSize.Height) + 26;
    }

    /// <summary>Büyüyünce ekranın dışına taşmasın.</summary>
    private void EkranaSigdir()
    {
        var alan = SystemParameters.WorkArea;

        if (Left + Width > alan.Right) Left = Math.Max(alan.Left, alan.Right - Width);
        if (Top + Height > alan.Bottom) Top = Math.Max(alan.Top, alan.Bottom - Height);
        if (Left < alan.Left) Left = alan.Left;
        if (Top < alan.Top) Top = alan.Top;

        KonumDegisti?.Invoke();
    }

    // ── Düğmeler ────────────────────────────────────────────────────────

    private void YenileButton_Click(object sender, RoutedEventArgs e) => YenileIstendi?.Invoke();

    private void DetaylarButton_Click(object sender, RoutedEventArgs e) => Genislet();

    private void KucultButton_Click(object sender, RoutedEventArgs e) => Kucult();

    private void GenelToplamButton_Click(object sender, RoutedEventArgs e)
    {
        _genelToplamAcik = !_genelToplamAcik;
        GenelToplamUygula();
        GenelToplamDegisti?.Invoke(_genelToplamAcik);
    }

    // ── Veri ────────────────────────────────────────────────────────────

    public void MesgulGoster()
    {
        YenileButton.IsEnabled = false;
        AltBaslikText.Text = "okunuyor…";
        KompaktDurumText.Text = "•••";
        HataText.Visibility = Visibility.Collapsed;
    }

    public void Goster(Gorunum g)
    {
        YenileButton.IsEnabled = true;
        HataText.Visibility = Visibility.Collapsed;

        KompaktListesi.ItemsSource = g.Kompakt;
        KartListesi.ItemsSource = g.Kartlar;
        PasifListesi.ItemsSource = g.Pasifler;
        OturumListesi.ItemsSource = g.Oturumlar;

        // Rozetin boyu içeriğe göre değişiyor: ajan sayısı, kota satırı var mı,
        // metin sarıyor mu… Sabit formül yerine WPF'e ölçtürüyoruz.
        if (!_genis)
        {
            _kompaktYukseklik = KompaktYukseklikHesapla();
            Height = _kompaktYukseklik;
        }

        GenelToplamText.Text = g.GenelToplam;
        _genelToplamAcik = g.GenelToplamGoster;
        GenelToplamUygula();

        AltBaslikText.Text = g.SonEtkinlik == DateTimeOffset.MinValue
            ? "veri yok"
            : $"son ajan etkinliği {Bicim.NeKadarOnce(g.SonEtkinlik)}";

        KompaktDurumText.Text = g.ScanSaati;
        DurumText.Text = g.Durum;
    }

    public void HataGoster(string mesaj)
    {
        YenileButton.IsEnabled = true;
        HataText.Text = mesaj;
        HataText.Visibility = Visibility.Visible;
        AltBaslikText.Text = "okunamadı";
        KompaktDurumText.Text = "hata";
    }

    private void GenelToplamUygula()
    {
        GenelToplamKutu.Visibility = _genelToplamAcik ? Visibility.Visible : Visibility.Collapsed;
        GenelToplamButton.Content = _genelToplamAcik ? "genel toplamı gizle" : "genel toplamı göster";
    }
}
