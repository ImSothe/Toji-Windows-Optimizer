using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Toji
{
    public partial class MainWindow : Window
    {
        private readonly TojiEngine _eng = new TojiEngine();
        private List<Tweak> _tweaks;
        private List<Profile> _profiles;

        private readonly Dictionary<string, CheckBox> _boxes = new Dictionary<string, CheckBox>();
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Head> _heads = new List<Head>();
        private readonly Dictionary<string, Button> _riskBtns = new Dictionary<string, Button>();
        private string _fDanger = "todos";
        private string _fCat = "todas";

        private static readonly Dictionary<string, string> DangerColor = new Dictionary<string, string>
        { { "safe", "#3FB982" }, { "moderate", "#59C3D6" }, { "high", "#BD9140" }, { "extreme", "#CF4A4F" } };
        private static readonly Dictionary<string, string> ProfColor = new Dictionary<string, string>
        { { "competitivo", "#8B5CF6" }, { "ciber", "#59C3D6" }, { "limpio", "#8A83A0" } };

        private class Row { public Border Border; public string Text; public string Danger; public string Cat; }
        private class Head { public FrameworkElement Panel; public string Cat; }

        private static Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                _tweaks = _eng.LoadTweaks();
                _profiles = _eng.LoadProfiles();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudieron cargar los datos (tweaks.json). " + ex.Message, "TOJI");
                _tweaks = new List<Tweak>(); _profiles = new List<Profile>();
            }

            BuildList();
            BuildProfileBar();
            BuildRiskChips();
            BuildCatCombo();

            foreach (var id in _eng.GetApplied()) if (_boxes.ContainsKey(id)) _boxes[id].IsChecked = true;

            SearchBox.TextChanged += (s, e) => ApplyFilter();
            AdvancedChk.Checked += (s, e) =>
            {
                if (MessageBox.Show("El Modo Avanzado muestra ajustes PROHIBIDOS que pueden dejar Windows inservible. Continuar?",
                        "TOJI - PELIGRO", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                { AdvancedChk.IsChecked = false; return; }
                ApplyFilter();
            };
            AdvancedChk.Unchecked += (s, e) => ApplyFilter();

            BtnApply.Click += async (s, e) => await ApplyOrDry(false);
            BtnDry.Click += async (s, e) => await ApplyOrDry(true);
            BtnSaveProfile.Click += (s, e) => SaveProfile();
            BtnRevertAll.Click += async (s, e) => await RevertAll();
            BtnRestore.Click += (s, e) => { StatusText.Text = "Creando punto de restauracion..."; var ok = _eng.CreateRestorePoint(); MessageBox.Show(ok ? "Punto de restauracion creado." : "No se pudo crear.", "TOJI"); StatusText.Text = "Listo."; };
            BtnExport.Click += (s, e) => { var code = _eng.ExportCode(CheckedIds()); try { Clipboard.SetText(code); } catch { } MessageBox.Show("Codigo (copiado):\n\n" + code, "TOJI - exportar"); };
            BtnImport.Click += (s, e) => ImportCode();
            BtnClean.Click += (s, e) => { _eng.ClearTemp(); StatusText.Text = "Basura eliminada."; };

            // Reparaciones
            BtnClean2.Click += (s, e) => _eng.ClearTemp();
            BtnFlushDns.Click += (s, e) => _eng.RunPowerShellHidden("ipconfig /flushdns");
            BtnSFC.Click += (s, e) => _eng.RunPowerShellVisible("SFC", "sfc /scannow");
            BtnDISM.Click += (s, e) => _eng.RunPowerShellVisible("DISM", "DISM /Online /Cleanup-Image /RestoreHealth");
            BtnNet.Click += (s, e) => _eng.RunPowerShellVisible("Reset de red", "netsh winsock reset; netsh int ip reset; ipconfig /flushdns; Write-Host 'Reinicia el PC.' -ForegroundColor Yellow");
            BtnWU.Click += (s, e) => _eng.RunPowerShellVisible("Reparar Windows Update", "net stop wuauserv; net stop bits; Rename-Item \"$env:SystemRoot\\SoftwareDistribution\" SoftwareDistribution.old -ErrorAction SilentlyContinue; net start wuauserv; net start bits");

            ApplyFilter();
            UpdateCount();
        }

        private IEnumerable<string> CheckedIds() => _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key);
        private void UpdateCount() => CountText.Text = $"{_boxes.Count(kv => kv.Value.IsChecked == true)} seleccionados  |  {_eng.GetApplied().Count} aplicados";

        private static readonly string[] CatOrder =
        { "Rendimiento", "Input", "Energia", "Red", "GPU", "Interfaz", "Privacidad", "Almacenamiento", "Debloat", "Activacion", "Windows Update", "Seguridad", "PROHIBIDO" };

        private void BuildList()
        {
            var cats = CatOrder.Where(c => _tweaks.Any(t => t.Category == c)).ToList();
            foreach (var c in _tweaks.Select(t => t.Category).Distinct()) if (!cats.Contains(c)) cats.Add(c);

            foreach (var c in cats)
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 14, 0, 6) };
                head.Children.Add(new Border { Background = B("#8B5CF6"), Width = 3, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
                head.Children.Add(new TextBlock { Text = c.ToUpper(), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = B("#E9E6F0"), VerticalAlignment = VerticalAlignment.Center });
                RootList.Children.Add(head);
                _heads.Add(new Head { Panel = head, Cat = c });

                foreach (var t in _tweaks.Where(x => x.Category == c))
                {
                    var border = new Border { Background = B("#171122"), Margin = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 8, 10, 8) };
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var cb = new CheckBox { Margin = new Thickness(0, 0, 12, 0), Tag = t.Id };
                    cb.Checked += (s, e) => UpdateCount(); cb.Unchecked += (s, e) => UpdateCount();
                    Grid.SetColumn(cb, 0); _boxes[t.Id] = cb;

                    var sp = new StackPanel(); Grid.SetColumn(sp, 1);
                    sp.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.Bold, Foreground = B("#E9E6F0"), TextWrapping = TextWrapping.Wrap });
                    sp.Children.Add(new TextBlock { Text = t.Desc, Foreground = B("#8A83A0"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                    if (!string.IsNullOrEmpty(t.Warning))
                        sp.Children.Add(new TextBlock { Text = "! " + t.Warning, Foreground = B("#BD9140"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                    if (t.Anticheat)
                        sp.Children.Add(new TextBlock { Text = "* riesgo anti-cheat", Foreground = B("#CF4A4F"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });

                    var dp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 2, 0, 0) };
                    Grid.SetColumn(dp, 2);
                    var col = DangerColor.ContainsKey(t.Danger) ? DangerColor[t.Danger] : "#8A83A0";
                    dp.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = B(col), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
                    dp.Children.Add(new TextBlock { Text = t.Danger, Foreground = B(col), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });

                    grid.Children.Add(cb); grid.Children.Add(sp); grid.Children.Add(dp);
                    border.Child = grid; RootList.Children.Add(border);
                    _rows.Add(new Row { Border = border, Text = (t.Name + " " + t.Desc + " " + t.Id + " " + t.Category).ToLowerInvariant(), Danger = t.Danger, Cat = c });
                }
            }
        }

        private void BuildProfileBar()
        {
            ProfileBar.Children.Clear();
            _profiles = _eng.LoadProfiles();
            foreach (var p in _profiles)
            {
                var pb = new Button { Content = p.Name, Style = (Style)FindResource("Profile"), Tag = p };
                pb.Click += (s, e) => SelectProfile((Profile)((Button)s).Tag);
                ProfileBar.Children.Add(pb);
            }
        }

        private void SelectProfile(Profile p)
        {
            var ids = new HashSet<string>(p.Tweaks ?? new List<string>());
            foreach (var kv in _boxes) kv.Value.IsChecked = ids.Contains(kv.Key);
            var col = ProfColor.ContainsKey(p.Id) ? ProfColor[p.Id] : "#A78BFA";
            foreach (var child in ProfileBar.Children.OfType<Button>())
            {
                var pp = child.Tag as Profile;
                if (pp != null && pp.Id == p.Id) { child.Background = B(col); child.Foreground = B("#140B24"); }
                else { child.Background = B("#140F1E"); child.Foreground = B("#E9E6F0"); }
            }
            ProfileLabel.Text = $"Perfil: {p.Name}  -  {p.Desc}"; ProfileLabel.Foreground = B(col);
            UpdateCount();
        }

        private void BuildRiskChips()
        {
            foreach (var rk in new[] { "todos", "safe", "moderate", "high", "extreme" })
            {
                var col = rk == "todos" ? "#8A83A0" : DangerColor[rk];
                var chip = new Button { Content = rk, Style = (Style)FindResource("Base"), Tag = rk, FontSize = 11, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0), Foreground = B(col) };
                chip.Click += (s, e) => SetRiskFilter((string)((Button)s).Tag);
                _riskBtns[rk] = chip; RiskChips.Children.Add(chip);
            }
            _riskBtns["todos"].BorderBrush = B("#8A83A0");
        }

        private void SetRiskFilter(string rk)
        {
            _fDanger = rk;
            foreach (var kv in _riskBtns)
                kv.Value.BorderBrush = kv.Key == rk ? B(kv.Key == "todos" ? "#8A83A0" : DangerColor[kv.Key]) : B("#2E2440");
            ApplyFilter();
        }

        private void BuildCatCombo()
        {
            CatCombo.Items.Add("todas");
            foreach (var c in _rows.Select(r => r.Cat).Distinct()) CatCombo.Items.Add(c);
            CatCombo.SelectedIndex = 0;
            CatCombo.SelectionChanged += (s, e) => { _fCat = CatCombo.SelectedItem?.ToString() ?? "todas"; ApplyFilter(); };
        }

        private void ApplyFilter()
        {
            bool adv = AdvancedChk.IsChecked == true;
            string q = SearchBox.Text.ToLowerInvariant().Trim();
            foreach (var r in _rows)
            {
                bool hiddenByAdv = !adv && (r.Cat == "PROHIBIDO" || r.Danger == "extreme");
                bool okD = _fDanger == "todos" || r.Danger == _fDanger;
                bool okC = _fCat == "todas" || r.Cat == _fCat;
                bool okS = q == "" || r.Text.Contains(q);
                r.Border.Visibility = (!hiddenByAdv && okD && okC && okS) ? Visibility.Visible : Visibility.Collapsed;
            }
            foreach (var h in _heads)
                h.Panel.Visibility = _rows.Any(r => r.Cat == h.Cat && r.Border.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetBusy(bool busy)
        {
            foreach (var b in new[] { BtnApply, BtnDry, BtnRevertAll, BtnRestore, BtnExport, BtnImport, BtnClean, BtnSaveProfile })
                b.IsEnabled = !busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (!busy) Progress.Value = 0;
        }

        private async Task ApplyOrDry(bool dryRun)
        {
            var ids = CheckedIds().ToList();
            var applied = _eng.GetApplied();
            var toRevert = applied.Where(a => !ids.Contains(a)).ToList();
            var toApply = ids.Where(i => !applied.Contains(i)).ToList();

            if (dryRun)
            {
                MessageBox.Show($"SIMULACION (no se toca nada).\n\nSe APLICARIAN ({toApply.Count}):\n" +
                    string.Join("\n", toApply.Select(x => "  + " + x)) +
                    $"\n\nSe REVERTIRIAN ({toRevert.Count}):\n" + string.Join("\n", toRevert.Select(x => "  - " + x)), "TOJI - dry run");
                return;
            }

            var newExtreme = _tweaks.Where(t => toApply.Contains(t.Id) && t.Danger == "extreme").ToList();
            if (newExtreme.Count > 0)
            {
                var msg = "Vas a aplicar ajustes PROHIBIDOS:\n\n" + string.Join("\n", newExtreme.Select(t => "- " + t.Name)) + "\n\nContinuar?";
                if (MessageBox.Show(msg, "TOJI - PELIGRO", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }

            SetBusy(true);
            var errs = new List<string>(); bool reboot = false;
            var progress = new Progress<Tuple<int, string>>(p => { Progress.Value = p.Item1; StatusText.Text = p.Item2; });
            var rep = (IProgress<Tuple<int, string>>)progress;

            await Task.Run(() =>
            {
                int total = Math.Max(toRevert.Count + toApply.Count, 1); int i = 0;
                foreach (var id in toRevert) { i++; rep.Report(Tuple.Create(i * 100 / total, "Revirtiendo: " + id)); errs.AddRange(_eng.RevertTweak(id).Errors); }
                foreach (var id in toApply)
                {
                    i++; rep.Report(Tuple.Create(i * 100 / total, "Aplicando: " + id));
                    var t = _tweaks.First(x => x.Id == id); var r = _eng.ApplyTweak(t); errs.AddRange(r.Errors); if (t.Reboot) reboot = true;
                }
            });

            SetBusy(false); UpdateCount();
            var summary = $"Hecho. Aplicados: {toApply.Count} | Revertidos: {toRevert.Count}.";
            if (reboot) summary += "\nAlgunos cambios necesitan REINICIAR.";
            if (errs.Count > 0) summary += "\n\nAvisos:\n" + string.Join("\n", errs);
            StatusText.Text = "Listo.";
            MessageBox.Show(summary, "TOJI");
        }

        private async Task RevertAll()
        {
            if (MessageBox.Show("Revertir TODO lo aplicado por TOJI?", "TOJI", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var applied = _eng.GetApplied().ToList();
            SetBusy(true);
            var progress = new Progress<Tuple<int, string>>(p => { Progress.Value = p.Item1; StatusText.Text = p.Item2; });
            var rep = (IProgress<Tuple<int, string>>)progress;
            await Task.Run(() =>
            {
                int total = Math.Max(applied.Count, 1); int i = 0;
                foreach (var id in applied) { i++; rep.Report(Tuple.Create(i * 100 / total, "Revirtiendo: " + id)); _eng.RevertTweak(id); }
            });
            foreach (var kv in _boxes) kv.Value.IsChecked = false;
            foreach (var child in ProfileBar.Children.OfType<Button>()) { child.Background = B("#140F1E"); child.Foreground = B("#E9E6F0"); }
            ProfileLabel.Text = "Perfil: ninguno"; ProfileLabel.Foreground = B("#8A83A0");
            SetBusy(false); UpdateCount();
            MessageBox.Show("Todo revertido. Reinicia para que asiente.", "TOJI");
        }

        private void SaveProfile()
        {
            var ids = CheckedIds().ToList();
            if (ids.Count == 0) { MessageBox.Show("Marca primero los ajustes que quieras en el perfil.", "TOJI"); return; }
            var name = Prompt("Nombre del perfil:", "Mi setup");
            if (string.IsNullOrWhiteSpace(name)) return;
            _eng.SaveProfile(name, ids);
            BuildProfileBar();
            MessageBox.Show($"Perfil '{name}' guardado. Ya aparece arriba.", "TOJI");
        }

        private void ImportCode()
        {
            var code = Prompt("Pega el codigo TOJI:", "");
            if (string.IsNullOrWhiteSpace(code)) return;
            try
            {
                var ids = new HashSet<string>(_eng.ImportCode(code));
                foreach (var kv in _boxes) kv.Value.IsChecked = ids.Contains(kv.Key);
                ProfileLabel.Text = "Perfil: importado desde codigo";
                UpdateCount();
                MessageBox.Show($"{ids.Count} ajustes marcados. Pulsa APLICAR.", "TOJI");
            }
            catch { MessageBox.Show("Codigo invalido.", "TOJI"); }
        }

        // Prompt de texto minimalista (WPF no trae InputBox)
        private string Prompt(string question, string def)
        {
            var w = new Window { Title = "TOJI", Width = 440, Height = 175, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Background = B("#0A0810"), ResizeMode = ResizeMode.NoResize };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = question, Foreground = B("#E9E6F0"), Margin = new Thickness(0, 0, 0, 8) });
            var tb = new TextBox { Text = def, Background = B("#171122"), Foreground = B("#E9E6F0"), BorderBrush = B("#2E2440"), Padding = new Thickness(6) };
            sp.Children.Add(tb);
            var ok = new Button { Content = "OK", Width = 90, Height = 30, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Background = B("#8B5CF6"), Foreground = B("#140B24"), FontWeight = FontWeights.Bold, BorderBrush = B("#8B5CF6"), Cursor = System.Windows.Input.Cursors.Hand };
            string result = null;
            ok.Click += (s, e) => { result = tb.Text; w.DialogResult = true; w.Close(); };
            sp.Children.Add(ok);
            w.Content = sp;
            return w.ShowDialog() == true ? result : null;
        }
    }
}
