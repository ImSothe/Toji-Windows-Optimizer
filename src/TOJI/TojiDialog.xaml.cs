using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Toji
{
    // Dialogo modal con la estetica TOJI: sustituye a MessageBox y a los InputBox nativos
    public partial class TojiDialog : Window
    {
        private Func<string, string> _validate;
        public string Value { get; private set; }

        private TojiDialog(Window owner, string title, string message, string okText, string cancelText, bool danger)
        {
            InitializeComponent();
            if (owner != null && owner.IsVisible) Owner = owner;
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Title = title;
            TitleText.Text = title;
            MessageText.Text = message ?? "";
            BtnOk.Content = okText;
            if (cancelText == null) BtnCancel.Visibility = Visibility.Collapsed;
            else BtnCancel.Content = cancelText;
            if (danger)
            {
                BtnOk.Style = (Style)FindResource("BtnDangerSolid");
                AccentBar.Background = (Brush)FindResource("Extreme");
            }

            BtnOk.Click += (s, e) => Accept();
            BtnCancel.Click += (s, e) => DialogResult = false;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } };
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            Loaded += (s, e) =>
            {
                if (InputBox.Visibility == Visibility.Visible) { InputBox.Focus(); InputBox.SelectAll(); }
                else BtnOk.Focus();
            };
        }

        private void Accept()
        {
            if (InputBox.Visibility == Visibility.Visible)
            {
                var text = InputBox.Text ?? "";
                var error = _validate?.Invoke(text);
                if (error != null)
                {
                    ErrorText.Text = error;
                    ErrorText.Visibility = Visibility.Visible;
                    InputBox.Focus(); InputBox.SelectAll();
                    return;
                }
                Value = text;
            }
            DialogResult = true;
        }

        public static void Info(Window owner, string title, string message)
        {
            new TojiDialog(owner, title, message, "Entendido", null, false).ShowDialog();
        }

        public static bool Confirm(Window owner, string title, string message, string okText = "Continuar", bool danger = false)
        {
            return new TojiDialog(owner, title, message, okText, "Cancelar", danger).ShowDialog() == true;
        }

        // Devuelve el texto aceptado o null si se cancela. validate: devuelve un mensaje de error o null si es valido
        public static string Prompt(Window owner, string title, string message, string initial, string okText,
                                    string placeholder = "", bool code = false, Func<string, string> validate = null)
        {
            var d = new TojiDialog(owner, title, message, okText, "Cancelar", false) { _validate = validate };
            d.InputBox.Visibility = Visibility.Visible;
            d.InputBox.Text = initial ?? "";
            d.InputBox.Tag = placeholder;
            if (code)
            {
                d.InputBox.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                d.InputBox.FontSize = 13;
                d.InputBox.TextWrapping = TextWrapping.Wrap;
                d.InputBox.Height = 104;
                d.InputBox.VerticalContentAlignment = VerticalAlignment.Top;
                d.InputBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            d.InputBox.TextChanged += (s, e) => d.ErrorText.Visibility = Visibility.Collapsed;
            return d.ShowDialog() == true ? d.Value : null;
        }
    }
}
