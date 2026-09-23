using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toji.Licensing;

namespace Toji
{
    // Muro de pago "mirar pero no tocar": solo aparece al pulsar APLICAR (con cambios nuevos) o Guardar perfil sin licencia
    public partial class PaywallDialog : Window
    {
        private readonly LicenseService _lic;
        private bool _busy;

        private PaywallDialog(Window owner, LicenseService lic, string action)
        {
            InitializeComponent();
            _lic = lic;
            if (owner != null && owner.IsVisible) Owner = owner;
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var s = lic.Status;
            ReasonText.Text = Reason(s, action);
            if (s.State == LicenseState.TrialExpired) TitleText.Text = "Tu prueba ha terminado";
            if (s.State == LicenseState.Revoked) TitleText.Text = "Licencia desactivada";
            if (s.State == LicenseState.ValidationRequired) TitleText.Text = "Valida tu licencia";

            bool validate = s.State == LicenseState.ValidationRequired && lic.HasKey;
            BtnRetry.Visibility = validate ? Visibility.Visible : Visibility.Collapsed;
            BtnTrial.Visibility = !validate && s.TrialAvailable ? Visibility.Visible : Visibility.Collapsed;
            if (BtnTrial.Visibility != Visibility.Visible && !validate) Grid.SetColumnSpan(BtnBuy, 2);
            if (lic.ProviderName == "Demo")
                FootText.Text = "MODO DEMO (Debug): prueba con la clave TOJI-DEMO-PRO o TOJI-DEMO-REVOKED. Revertir cambios es siempre gratis.";
            if (!string.IsNullOrEmpty(LicenseConfig.SupportEmail))
                FootText.Text += "  ·  Soporte: " + LicenseConfig.SupportEmail;

            BtnActivate.Click += async (_, __) => await Run(() => _lic.ActivateAsync(KeyBox.Text), "Licencia activada");
            BtnTrial.Click += async (_, __) => await Run(() => _lic.StartTrialAsync(), $"Prueba de {LicenseConfig.TrialDays} días activada");
            BtnRetry.Click += async (_, __) => await Run(async () =>
            {
                await _lic.RefreshOnlineAsync();
                return (_lic.Status.CanModify, _lic.Status.CanModify ? null : (_lic.Status.Message ?? "No se pudo validar. Revisa tu conexión."));
            }, "Licencia validada");
            BtnBuy.Click += (_, __) => OpenCheckout();
            BtnClose.Click += (_, __) => Close();
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !_busy) { e.Handled = true; Close(); } };
            MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            Loaded += (_, __) => KeyBox.Focus();
            KeyBox.TextChanged += (_, __) => ErrorText.Visibility = Visibility.Collapsed;
        }

        public string SuccessMessage { get; private set; }

        // Devuelve true si (ya o tras el dialogo) se puede modificar el sistema
        public static async Task<bool> EnsureCanModifyAsync(Window owner, LicenseService lic, string action, Action<string> onUnlocked = null)
        {
            if (lic.Status.CanModify) return true;
            // Si solo falta validar, se intenta en silencio antes de molestar
            if (lic.Status.State == LicenseState.ValidationRequired && lic.HasKey)
            {
                await lic.RefreshOnlineAsync();
                if (lic.Status.CanModify) return true;
            }
            var d = new PaywallDialog(owner, lic, action);
            d.ShowDialog();
            if (lic.Status.CanModify && d.SuccessMessage != null) onUnlocked?.Invoke(d.SuccessMessage);
            return lic.Status.CanModify;
        }

        public static void Show(Window owner, LicenseService lic) => new PaywallDialog(owner, lic, "aplicar cambios").ShowDialog();

        private static string Reason(LicenseStatus s, string action)
        {
            var extra = string.IsNullOrEmpty(s.Message) ? "" : s.Message + " ";
            return s.State switch
            {
                LicenseState.TrialExpired => $"Hazte con TOJI PRO para seguir pudiendo {action}. Todo lo que ya aplicaste se puede revertir gratis.",
                LicenseState.Revoked => extra + "Si crees que es un error, contacta con soporte.",
                LicenseState.ValidationRequired => extra + "Conéctate a Internet y pulsa «Validar ahora».",
                _ => extra + $"Para {action} necesitas una licencia de TOJI. Puedes seguir explorando todos los ajustes sin límites."
            };
        }

        private async Task Run(Func<Task<(bool ok, string message)>> action, string success)
        {
            if (_busy) return;
            SetBusy(true);
            try
            {
                var (ok, message) = await action();
                if (ok) { SuccessMessage = success; Close(); return; }
                ShowError(message);
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { SetBusy(false); }
        }

        private void OpenCheckout()
        {
            if (string.IsNullOrEmpty(LicenseConfig.CheckoutUrl)) { ShowError("La tienda aún no está disponible."); return; }
            try { Process.Start(new ProcessStartInfo(LicenseConfig.CheckoutUrl) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError("No se pudo abrir el navegador: " + ex.Message); }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            BtnActivate.IsEnabled = BtnTrial.IsEnabled = BtnRetry.IsEnabled = BtnBuy.IsEnabled = KeyBox.IsEnabled = !busy;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
