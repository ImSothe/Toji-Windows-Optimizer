using System.Windows;

namespace Sothe
{
    // Propiedad adjunta para que un unico ControlTemplate de boton sirva con distintos radios de esquina
    public static class Ui
    {
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
            "CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(8)));

        public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
        public static void SetCornerRadius(DependencyObject o, CornerRadius value) => o.SetValue(CornerRadiusProperty, value);
    }
}
