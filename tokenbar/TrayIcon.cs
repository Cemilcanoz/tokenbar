using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace tokenbar;

/// <summary>
/// Sistem tepsisi ikonu.
/// </summary>
/// <remarks>
/// 📖 Windows'un <c>Shell_NotifyIcon</c> API'si sana yalnızca bir
/// <b>dikdörtgen ikon slotu</b> veriyor — yazı çizecek yer yok. macOS'un menu
/// bar'ından temel farkı bu. Çözüm: sayıyı her güncellemede ikonun üstüne
/// <b>çizmek</b> (Discord ve pil göstergesi de böyle yapıyor).
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private Icon? _current;
    private string _lastText = "";

    public event Action? GosterIstendi;
    public event Action? YenileIstendi;
    public event Action? CikisIstendi;
    public event Action<bool>? BaslangicDegisti;

    private readonly WinForms.ToolStripMenuItem _baslangicItem;

    public TrayIcon(bool baslangictaCalis)
    {
        _baslangicItem = new WinForms.ToolStripMenuItem("Windows açılışında başlat")
        {
            CheckOnClick = true,
            Checked = baslangictaCalis
        };
        _baslangicItem.CheckedChanged += (_, _) => BaslangicDegisti?.Invoke(_baslangicItem.Checked);

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => GosterIstendi?.Invoke());
        menu.Items.Add("Şimdi yenile", null, (_, _) => YenileIstendi?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_baslangicItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => CikisIstendi?.Invoke());

        _icon = new WinForms.NotifyIcon
        {
            Text = "TokenBar",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = SystemIcons.Application
        };

        _icon.DoubleClick += (_, _) => GosterIstendi?.Invoke();
        _icon.Click += (_, e) =>
        {
            if (e is WinForms.MouseEventArgs { Button: WinForms.MouseButtons.Left })
                GosterIstendi?.Invoke();
        };
    }

    public void BaslangicIsaretle(bool acik)
    {
        if (_baslangicItem.Checked != acik) _baslangicItem.Checked = acik;
    }

    public void Guncelle(string kisaMetin, string tooltip)
    {
        // 📖 szTip 128 karakter. Uzun metin sessizce kırpılır.
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        // Aynı metni yeniden çizmek boşuna GDI işi.
        if (kisaMetin == _lastText) return;
        _lastText = kisaMetin;

        var yeni = Ciz(kisaMetin);
        _icon.Icon = yeni;

        // Eskisini ANCAK yenisi atandıktan sonra bırak.
        _current?.Dispose();
        _current = yeni;
    }

    /// <summary>Sayıyı 32×32'lik bir bitmap'e çizip ikona çevirir.</summary>
    private static Icon Ciz(string metin)
    {
        const int boyut = 32;

        using var bitmap = new Bitmap(boyut, boyut);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var zemin = new SolidBrush(Color.FromArgb(255, 26, 29, 36));
            using var yol = YuvarlakDikdortgen(new Rectangle(0, 0, boyut, boyut), 7);
            g.FillPath(zemin, yol);

            // 2-3 karakter sığıyor; 4 olursa yazıyı küçült.
            var punto = metin.Length switch { <= 2 => 17f, 3 => 14f, _ => 11f };
            using var yaziTipi = new Font("Segoe UI", punto, FontStyle.Bold, GraphicsUnit.Pixel);
            using var firca = new SolidBrush(Color.FromArgb(255, 126, 224, 192));
            using var hizalama = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            g.DrawString(metin, yaziTipi, firca, new RectangleF(0, 0, boyut, boyut), hizalama);
        }

        // 🔴 DestroyIcon ŞART. Yoksa her güncellemede bir GDI handle sızar ve
        // saatler içinde uygulama hiçbir şey çizemez hâle gelir.
        var handle = bitmap.GetHicon();
        try
        {
            using var gecici = Icon.FromHandle(handle);
            return (Icon)gecici.Clone();     // handle'dan bağımsız kopya
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath YuvarlakDikdortgen(Rectangle r, int yaricap)
    {
        var yol = new GraphicsPath();
        var d = yaricap * 2;
        yol.AddArc(r.X, r.Y, d, d, 180, 90);
        yol.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        yol.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        yol.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        yol.CloseFigure();
        return yol;
    }

    public void Dispose()
    {
        // 🔴 Dispose edilmezse ikon tepside "hayalet" olarak kalır —
        // uygulama kapandığı hâlde ikon durur, tıklayınca hiçbir şey olmaz.
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }

    // Not: yeni [LibraryImport] kaynak üreteci `AllowUnsafeBlocks` istiyor.
    // Tek bir imza için projeyi unsafe'e açmaya değmez; klasik DllImport yeter.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
