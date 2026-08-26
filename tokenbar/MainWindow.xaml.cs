using System.Windows;

namespace tokenbar;

/// <summary>
/// Pencerenin kod tarafı. XAML'deki x:Name'ler burada birer alan gibi görünür —
/// ikisi "partial class" ile birleşiyor.
/// </summary>
/// <remarks>
/// ⚠️ Bu pencere <b>hiç dosya okumuyor.</b> Taramayı <see cref="App"/> yapıyor,
/// buraya yalnızca hazır bir <see cref="Gorunum"/> geliyor. Ayrım kasıtlı:
/// pencere kapalıyken de tepsi güncellenmeye devam ediyor.
/// </remarks>
public partial class MainWindow : Window
{
    private bool _genelToplamAcik;

    public event Action? YenileIstendi;
    public event Action<bool>? GenelToplamDegisti;

    public MainWindow() => InitializeComponent();

    private void YenileButton_Click(object sender, RoutedEventArgs e) => YenileIstendi?.Invoke();

    private void GenelToplamButton_Click(object sender, RoutedEventArgs e)
    {
        _genelToplamAcik = !_genelToplamAcik;
        GenelToplamUygula();
        GenelToplamDegisti?.Invoke(_genelToplamAcik);
    }

    public void MesgulGoster()
    {
        YenileButton.IsEnabled = false;
        AltBaslikText.Text = "okunuyor…";
        HataText.Visibility = Visibility.Collapsed;
    }

    public void Goster(Gorunum g)
    {
        YenileButton.IsEnabled = true;
        HataText.Visibility = Visibility.Collapsed;

        KartListesi.ItemsSource = g.Kartlar;
        PasifListesi.ItemsSource = g.Pasifler;
        OturumListesi.ItemsSource = g.Oturumlar;

        GenelToplamText.Text = g.GenelToplam;
        _genelToplamAcik = g.GenelToplamGoster;
        GenelToplamUygula();

        AltBaslikText.Text = g.SonEtkinlik == DateTimeOffset.MinValue
            ? "veri yok"
            : $"son ajan etkinliği {Bicim.NeKadarOnce(g.SonEtkinlik)}";

        DurumText.Text = g.Durum;
    }

    public void HataGoster(string mesaj)
    {
        YenileButton.IsEnabled = true;
        HataText.Text = mesaj;
        HataText.Visibility = Visibility.Visible;
        AltBaslikText.Text = "okunamadı";
    }

    private void GenelToplamUygula()
    {
        GenelToplamKutu.Visibility = _genelToplamAcik ? Visibility.Visible : Visibility.Collapsed;
        GenelToplamButton.Content = _genelToplamAcik ? "genel toplamı gizle" : "genel toplamı göster";
    }
}
