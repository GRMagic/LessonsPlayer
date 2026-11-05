#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LessonsPlayer.App.Diagnostics;

/// <summary>
/// Apenas em Debug: com a variável de ambiente <c>LESSONSPLAYER_DEBUG_SNAPSHOTS</c> apontando para uma
/// pasta, salva a janela renderizada em <c>latest.png</c> a cada segundo. Usa <see cref="RenderTargetBitmap"/>,
/// que funciona mesmo sem desktop visível (tela bloqueada, sessão remota minimizada), para testes de UI
/// automatizados. Não é compilado no build Release.
/// </summary>
internal static class DebugSnapshots
{
    public const string VariableName = "LESSONSPLAYER_DEBUG_SNAPSHOTS";

    public static void AttachIfRequested(Window window)
    {
        var directory = Environment.GetEnvironmentVariable(VariableName);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "latest.png");
        var timer = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Save(window, path), window.Dispatcher);
        window.Closed += (_, _) => timer.Stop();
    }

    private static void Save(Window window, string path)
    {
        if (window.Content is not FrameworkElement root || root.ActualWidth < 1 || root.ActualHeight < 1)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var width = root.ActualWidth;
        var height = root.ActualHeight;

        // Desenha o fundo da janela e o conteúdo por cima (o conteúdo sozinho teria fundo transparente).
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(
            (int)(width * dpi.DpiScaleX), (int)(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var temporaryPath = path + ".tmp";
        using (var stream = File.Create(temporaryPath))
        {
            encoder.Save(stream);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}
#endif
