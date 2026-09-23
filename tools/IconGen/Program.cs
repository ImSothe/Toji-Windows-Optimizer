using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

// Genera el icono de la app (icon.ico) a partir del logo vectorial SotheLogo de Themes/SotheTheme.xaml,
// sobre una loseta oscura redondeada para que se lea bien en barras de tareas claras y oscuras.
//   IconGen <SotheTheme.xaml> <salida.ico>
// Si cambia el logo, vuelve a ejecutarlo: dotnet run --project tools/IconGen -- src/SotheOptimizer/Themes/SotheTheme.xaml src/SotheOptimizer/Assets/icon.ico
internal static class Program
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Uso: IconGen <SotheTheme.xaml> <salida.ico>");
                return 2;
            }
            var logo = LoadLogo(args[0]);
            var frames = new List<(int size, byte[] data)>();
            foreach (int n in Sizes) frames.Add((n, n >= 256 ? Png(Render(logo, n)) : Dib(Render(logo, n))));
            File.WriteAllBytes(args[1], Ico(frames));
            Console.WriteLine($"{args[1]}: {Sizes.Length} tamaños ({string.Join(", ", Sizes)} px)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    // Solo se extraen los recursos del logo: el tema completo usa tipos de la app (local:) que aqui no existen
    private static ImageSource LoadLogo(string themePath)
    {
        string xaml = File.ReadAllText(themePath);
        var m = Regex.Match(xaml, "<StreamGeometry x:Key=\"LogoBladeGeo\">.*?</DrawingImage>", RegexOptions.Singleline);
        if (!m.Success) throw new InvalidDataException("No se encontro SotheLogo en " + themePath);
        var dict = (ResourceDictionary)XamlReader.Parse(
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + m.Value + "</ResourceDictionary>");
        return (ImageSource)dict["SotheLogo"];
    }

    private static BitmapSource Render(ImageSource logo, int n)
    {
        bool small = n <= 32;
        var tile = new Border
        {
            Width = n,
            Height = n,
            CornerRadius = new CornerRadius(n * 0.22),
            Background = new LinearGradientBrush(Color.FromRgb(0x1E, 0x13, 0x38), Color.FromRgb(0x07, 0x06, 0x0C), 45),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x35, 0x7A)),
            BorderThickness = new Thickness(Math.Max(1, n / 64.0)),
            Child = new Image
            {
                Source = logo,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(n * (small ? 0.07 : 0.12)),
                // El halo neon solo aporta a partir de 48 px; en tamanos pequenos emborrona
                Effect = small ? null : new DropShadowEffect { Color = Color.FromRgb(0xD9, 0x46, 0xEF), BlurRadius = n / 16.0, ShadowDepth = 0, Opacity = 0.8 }
            }
        };
        tile.Measure(new Size(n, n));
        tile.Arrange(new Rect(0, 0, n, n));
        tile.UpdateLayout();
        var rtb = new RenderTargetBitmap(n, n, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(tile);
        return new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
    }

    private static byte[] Png(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    // Entrada clasica de icono: BITMAPINFOHEADER + pixeles BGRA de abajo arriba + mascara AND (vacia: manda el alfa)
    private static byte[] Dib(BitmapSource bmp)
    {
        int n = bmp.PixelWidth, stride = n * 4, maskStride = ((n + 31) / 32) * 4;
        var px = new byte[stride * n];
        bmp.CopyPixels(px, stride, 0);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(stride * n + maskStride * n); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = n - 1; y >= 0; y--) w.Write(px, y * stride, stride);
        w.Write(new byte[maskStride * n]);
        return ms.ToArray();
    }

    private static byte[] Ico(List<(int size, byte[] data)> frames)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (size, data) in frames)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) w.Write(data);
        return ms.ToArray();
    }
}
