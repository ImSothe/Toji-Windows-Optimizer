using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Toji
{
    // Ventana sin marco nativo (WindowStyle=None + AllowsTransparency): esquinas redondeadas, arrastre desde la
    // barra propia y botones de minimizar / maximizar / cerrar. Al maximizar se ajusta al area de trabajo del
    // monitor (sin tapar la barra de tareas) y quita el redondeo y el borde.
    public static class BorderlessWindow
    {
        public const double Radius = 8;

        private static readonly Geometry MaximizeGlyph = Geometry.Parse("M 0.5,0.5 H 9.5 V 9.5 H 0.5 Z");
        private static readonly Geometry RestoreGlyph = Geometry.Parse("M 2.5,2.5 V 0.5 H 9.5 V 7.5 H 7.5 M 0.5,2.5 H 7.5 V 9.5 H 0.5 Z");

        // root: el Border exterior (CornerRadius/Background/BorderBrush). dragAreas: zonas desde las que se arrastra
        // (doble clic maximiza si la ventana es redimensionable). Los botones son opcionales.
        public static void Attach(Window window, Border root, Button minimize, Button maximize, Button close, params UIElement[] dragAreas)
        {
            bool resizable = window.ResizeMode == ResizeMode.CanResize || window.ResizeMode == ResizeMode.CanResizeWithGrip;

            void ApplyState()
            {
                bool maxed = window.WindowState == WindowState.Maximized;
                root.CornerRadius = new CornerRadius(maxed ? 0 : Radius);
                root.BorderThickness = new Thickness(maxed ? 0 : 1);
                UpdateClip(root, maxed);
                if (maximize?.Content is Path glyph) glyph.Data = maxed ? RestoreGlyph : MaximizeGlyph;
                if (maximize != null) maximize.ToolTip = maxed ? "Restaurar" : "Maximizar";
            }

            void ToggleMaximize() =>
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

            void OnDrag(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2 && resizable) { ToggleMaximize(); e.Handled = true; return; }
                if (e.ButtonState != MouseButtonState.Pressed) return;
                try { window.DragMove(); } catch (InvalidOperationException) { }
            }

            foreach (var area in dragAreas) area.MouseLeftButtonDown += OnDrag;
            if (minimize != null) minimize.Click += (_, __) => window.WindowState = WindowState.Minimized;
            if (maximize != null)
            {
                maximize.Visibility = resizable ? Visibility.Visible : Visibility.Collapsed;
                maximize.Click += (_, __) => ToggleMaximize();
            }
            if (close != null) close.Click += (_, __) => window.Close();

            if (root.Child is FrameworkElement content)
                content.SizeChanged += (_, __) => UpdateClip(root, window.WindowState == WindowState.Maximized);
            window.StateChanged += (_, __) => ApplyState();
            if (resizable)
            {
                // El manejador de SourceInitialized captura 'hook' y lo mantiene vivo mientras viva la ventana
                HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (msg == WM_GETMINMAXINFO) handled = FitToWorkArea(window, hwnd, lParam);
                    return IntPtr.Zero;
                };
                window.SourceInitialized += (_, __) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(hook);
            }
            ApplyState();
        }

        // El contenido se recorta a la forma redondeada para que nada (aura, hover de botones) asome por las esquinas
        private static void UpdateClip(Border root, bool maxed)
        {
            if (root.Child is not FrameworkElement c || c.ActualWidth <= 0 || c.ActualHeight <= 0) return;
            double r = maxed ? 0 : Math.Max(0, Radius - root.BorderThickness.Left);
            c.Clip = new RectangleGeometry(new Rect(0, 0, c.ActualWidth, c.ActualHeight), r, r);
        }

        // ---- Maximizado dentro del area de trabajo (una ventana sin marco taparia la barra de tareas) ----
        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        private static bool FitToWorkArea(Window window, IntPtr hwnd, IntPtr lParam)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref mi)) return false;

            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.MaxPosition.X = mi.Work.Left - mi.Monitor.Left;
            mmi.MaxPosition.Y = mi.Work.Top - mi.Monitor.Top;
            mmi.MaxSize.X = mi.Work.Right - mi.Work.Left;
            mmi.MaxSize.Y = mi.Work.Bottom - mi.Work.Top;
            // Al marcar el mensaje como resuelto WPF ya no aplica MinWidth/MinHeight: se hace aqui (en pixeles reales)
            var dpi = VisualTreeHelper.GetDpi(window);
            mmi.MinTrackSize.X = (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX);
            mmi.MinTrackSize.Y = (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY);
            Marshal.StructureToPtr(mmi, lParam, false);
            return true;
        }
    }
}
