using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sothe
{
    // Pestanas "Apps (Winget)" y "Caracteristicas de Windows" (portadas de la version PowerShell).
    // Estas pestanas no tienen restricciones:
    // el muro de pago solo va en APLICAR optimizaciones y Guardar perfil (MainWindow.xaml.cs).
    public partial class MainWindow
    {
        private const double AppTileMinWidth = 290;
        private static readonly string[] AppCatOrder = { "Browsers", "Communication", "Gaming", "Development", "Utilities", "Multimedia", "Cybersecurity" };

        private List<AppItem> _apps = new List<AppItem>();
        private readonly Dictionary<string, CheckBox> _appBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Border> _appInstalled = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly List<(AppItem app, Border card, string text)> _appCards = new List<(AppItem, Border, string)>();
        private readonly List<(FrameworkElement head, WrapPanel panel, string cat)> _appSections = new List<(FrameworkElement, WrapPanel, string)>();

        private List<FeatureItem> _features = new List<FeatureItem>();
        private readonly Dictionary<string, (Border pill, TextBlock text, Button action)> _featRows = new Dictionary<string, (Border, TextBlock, Button)>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, FeatureState> _featStates = new Dictionary<string, FeatureState>(StringComparer.OrdinalIgnoreCase);
        private bool _appsLoaded, _featuresLoaded, _featBusy;

        private void InitTabs()
        {
            try { _apps = _eng.LoadApps().Where(a => Winget.IsValidId(a.Id)).ToList(); }
            catch (Exception ex) { _eng.Log("apps.json: " + ex.Message); }
            try { _features = _eng.LoadFeatures().Where(f => FeatureCommands.IsValidName(f.Feature)).ToList(); }
            catch (Exception ex) { _eng.Log("features.json: " + ex.Message); }

            BuildAppList();
            BuildFeatureList();
            UpdateAppCount();

            AppSearch.TextChanged += (s, e) => FilterApps();
            AppScroll.ScrollChanged += (s, e) => { if (e.ViewportWidthChange != 0) FitAppColumns(); };
            BtnInstall.Click += (s, e) => InstallApps();
            BtnUninstall.Click += (s, e) => UninstallApps();
            BtnUpgrade.Click += (s, e) => RunWingetConsole(Loc.T("console.update"), Winget.UpgradeAllScript, Loc.T("apps.updating"));
            BtnGpu.Click += async (s, e) => await OpenGpuDriversAsync();
            BtnAppsRefresh.Click += async (s, e) => await DetectAppsAsync();
            BtnFeatRefresh.Click += async (s, e) => await RefreshFeaturesAsync();

            // Carga perezosa: winget y el estado de las caracteristicas solo se consultan al abrir su pestana
            Tabs.SelectionChanged += async (s, e) =>
            {
                if (e.Source != Tabs) return;
                if (Tabs.SelectedItem == TabApps && !_appsLoaded) { _appsLoaded = true; await DetectAppsAsync(); }
                else if (Tabs.SelectedItem == TabFeatures && !_featuresLoaded) { _featuresLoaded = true; await RefreshFeaturesAsync(); }
            };
        }

        // ---------------- Apps (Winget) ----------------
        private void BuildAppList()
        {
            var cats = AppCatOrder.Where(c => _apps.Any(a => a.Category == c)).ToList();
            foreach (var c in _apps.Select(a => a.Category).Distinct()) if (!cats.Contains(c)) cats.Add(c);

            foreach (var c in cats)
            {
                var group = _apps.Where(a => a.Category == c).ToList();
                var head = BuildSectionHeader(c, group.Count);
                var panel = new WrapPanel();
                AppList.Children.Add(head);
                AppList.Children.Add(panel);
                _appSections.Add((head, panel, c));
                foreach (var a in group) panel.Children.Add(BuildAppCard(a));
            }
            if (_apps.Count == 0)
                AppList.Children.Add(new TextBlock { Text = Loc.T("apps.none"), Foreground = R("TextSub"), Margin = new Thickness(4, 18, 0, 0) });
        }

        private Border BuildAppCard(AppItem a)
        {
            var cb = new CheckBox { Tag = a.Id, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
            cb.Checked += (s, e) => UpdateAppCount(); cb.Unchecked += (s, e) => UpdateAppCount();
            _appBoxes[a.Id] = cb;

            var card = new Border
            {
                Style = (Style)FindResource("TweakCard"), Tag = cb, Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(12)
            };
            card.MouseLeftButtonDown += (s, e) => _pressedCard = card;
            card.MouseLeftButtonUp += (s, e) => { if (_pressedCard == card) cb.IsChecked = cb.IsChecked != true; _pressedCard = null; };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cb, 0);

            var sp = new StackPanel(); Grid.SetColumn(sp, 1);
            var title = new DockPanel();
            var installed = Badge(Loc.T("badge.installed"), R("Safe"));
            installed.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(installed, Dock.Right);
            _appInstalled[a.Id] = installed;
            title.Children.Add(installed);
            title.Children.Add(new TextBlock { Text = a.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(title);
            sp.Children.Add(new TextBlock { Text = a.DisplayDesc, FontSize = 13.5, Foreground = R("TextSub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), LineHeight = 20 });
            sp.Children.Add(new TextBlock { Text = a.Id, FontSize = 12, Foreground = R("TextDim"), FontFamily = new FontFamily("Cascadia Mono, Consolas"), Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

            g.Children.Add(cb); g.Children.Add(sp);
            card.Child = g;
            _appCards.Add((a, card, Fold(a.Name + " " + a.Id + " " + a.Desc + " " + (a.Es?.Desc ?? "") + " " + a.Category)));
            return card;
        }

        private void FitAppColumns()
        {
            double w = AppScroll.ViewportWidth - AppList.Margin.Left - AppList.Margin.Right;
            if (double.IsNaN(w) || w <= 0) return;
            double itemW = Math.Floor(w / Math.Max(1, (int)(w / AppTileMinWidth)));
            foreach (var s in _appSections) s.panel.ItemWidth = itemW;
        }

        private void FilterApps()
        {
            var terms = Fold(AppSearch.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var (_, card, text) in _appCards)
                card.Visibility = terms.All(t => text.Contains(t)) ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (head, panel, cat) in _appSections)
            {
                var vis = _appCards.Any(c => c.app.Category == cat && c.card.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
                head.Visibility = vis; panel.Visibility = vis;
            }
        }

        private List<string> CheckedAppIds() => _appBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        private string AppName(string id) => _apps.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;

        private void UpdateAppCount()
        {
            int n = _appBoxes.Count(kv => kv.Value.IsChecked == true);
            AppCountText.Text = n == 1 ? Loc.T("apps.selected.one") : Loc.T("apps.selected.n", n);
            BtnInstall.Content = n > 0 ? Loc.T("btn.install.n", n) : Loc.T("btn.install");
            BtnUninstall.Content = n > 0 ? Loc.T("btn.uninstall.n", n) : Loc.T("btn.uninstall");
        }

        private void SetWingetPill(string text, Brush color)
        {
            WingetText.Text = text; WingetText.Foreground = color;
            WingetDot.Fill = color;
            WingetPill.BorderBrush = Tint(color, 0x66);
        }

        private async Task DetectAppsAsync()
        {
            BtnAppsRefresh.IsEnabled = false;
            SetWingetPill(Loc.T("winget.checking"), R("TextSub"));
            AppStatus.Foreground = R("TextSub");
            try
            {
                var version = await WindowsToolsNative.WingetVersionAsync();
                bool ok = version != null;
                BtnInstall.IsEnabled = BtnUninstall.IsEnabled = BtnUpgrade.IsEnabled = ok;
                if (!ok)
                {
                    SetWingetPill(Loc.T("winget.unavailable"), R("Extreme"));
                    AppStatus.Foreground = R("Extreme");
                    AppStatus.Text = Loc.T("winget.missing");
                    return;
                }
                SetWingetPill("winget " + version, R("Safe"));
                AppStatus.Text = Loc.T("apps.detecting");
                var installed = await WindowsToolsNative.WingetInstalledAsync();
                int n = 0;
                foreach (var kv in _appInstalled)
                {
                    bool on = installed.Contains(kv.Key);
                    kv.Value.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                    if (on) n++;
                }
                AppStatus.Text = Loc.T("apps.installed.fmt", n, _apps.Count);
            }
            catch (Exception ex) { AppStatus.Text = Loc.T("winget.queryfail", ex.Message); }
            finally { BtnAppsRefresh.IsEnabled = true; }
        }

        private void InstallApps()
        {
            var ids = CheckedAppIds();
            if (ids.Count == 0) { ShowToast(Loc.T("apps.pickone"), ToastKind.Warn); return; }
            RunWingetConsole(Loc.T("console.install"), Winget.InstallScript(ids), Loc.T("apps.installing", ids.Count));
            _eng.Log("WINGET install: " + string.Join(", ", ids));
        }

        private void UninstallApps()
        {
            var ids = CheckedAppIds();
            if (ids.Count == 0) { ShowToast(Loc.T("apps.pickone"), ToastKind.Warn); return; }
            if (!SotheDialog.Confirm(this, Loc.T("apps.uninstall.title"),
                    Loc.T("apps.uninstall.body", ids.Count) + string.Join("\n", ids.Select(id => "   •  " + AppName(id))),
                    Loc.T("apps.uninstall.ok"), danger: true)) return;
            RunWingetConsole(Loc.T("console.uninstall"), Winget.UninstallScript(ids), Loc.T("apps.uninstalling"));
            _eng.Log("WINGET uninstall: " + string.Join(", ", ids));
        }

        private void RunWingetConsole(string title, string script, string status)
        {
            try
            {
                _eng.RunPowerShellVisible(title, script);
                AppStatus.Foreground = R("TextSub");
                AppStatus.Text = status;
            }
            catch (Exception ex) { ShowToast(Loc.T("console.fail", ex.Message), ToastKind.Error); }
        }

        private async Task OpenGpuDriversAsync()
        {
            BtnGpu.IsEnabled = false;
            try
            {
                var gpu = GpuDrivers.PickGpu(await Task.Run(() => WindowsToolsNative.GpuNames()));
                var (vendor, url) = GpuDrivers.DriverPage(gpu);
                _eng.Log("GPU detected: " + (gpu ?? "none"));
                if (url == null)
                {
                    SotheDialog.Info(this, Loc.T("gpu.title"), gpu == null ? Loc.T("gpu.none") : Loc.T("gpu.unknown", gpu));
                    return;
                }
                if (SotheDialog.Confirm(this, Loc.T("gpu.title"), Loc.T("gpu.confirm", gpu, vendor), Loc.T("gpu.open", vendor)))
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) { ShowToast(Loc.T("gpu.fail", ex.Message), ToastKind.Error); }
            finally { BtnGpu.IsEnabled = true; }
        }

        // ---------------- Caracteristicas de Windows ----------------
        private void BuildFeatureList()
        {
            foreach (var f in _features)
            {
                var card = new Border
                {
                    Background = R("Surface"), BorderBrush = R("Stroke"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 0, 10)
                };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = f.DisplayName, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                sp.Children.Add(new TextBlock { Text = f.DisplayDesc, FontSize = 13.5, Foreground = R("TextSub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), LineHeight = 20 });
                sp.Children.Add(new TextBlock { Text = f.Feature, FontSize = 12, Foreground = R("TextDim"), FontFamily = new FontFamily("Cascadia Mono, Consolas"), Margin = new Thickness(0, 6, 0, 0) });

                var text = new TextBlock { Text = Loc.T("feat.checking"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = R("TextSub") };
                var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(16, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Child = text, Background = Tint(R("TextSub"), 0x1E) };
                Grid.SetColumn(pill, 1);
                var btn = new Button { Style = (Style)FindResource("Btn"), Content = Loc.T("feat.enable"), MinWidth = 124, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = false };
                Grid.SetColumn(btn, 2);
                var feature = f;
                btn.Click += async (s, e) => await ToggleFeatureAsync(feature);

                g.Children.Add(sp); g.Children.Add(pill); g.Children.Add(btn);
                card.Child = g;
                FeatureList.Children.Add(card);
                _featRows[f.Feature] = (pill, text, btn);
            }
            if (_features.Count == 0)
                FeatureList.Children.Add(new TextBlock { Text = Loc.T("feat.none"), Foreground = R("TextSub") });
        }

        private void SetFeatureRow(string feature, FeatureState st, bool busy = false)
        {
            if (!_featRows.TryGetValue(feature, out var row)) return;
            string label; Brush color;
            if (busy) { label = Loc.T("feat.applying"); color = R("AccentHi"); }
            else
            {
                switch (st)
                {
                    case FeatureState.Enabled: label = Loc.T("feat.enabled"); color = R("Safe"); break;
                    case FeatureState.Disabled: label = Loc.T("feat.disabled"); color = R("TextSub"); break;
                    case FeatureState.Unavailable: label = Loc.T("feat.unavailable"); color = R("High"); break;
                    default: label = Loc.T("feat.unknown"); color = R("TextDim"); break;
                }
            }
            row.text.Text = label; row.text.Foreground = color;
            row.pill.Background = Tint(color, 0x22);
            row.action.Content = st == FeatureState.Enabled ? Loc.T("feat.disable") : Loc.T("feat.enable");
            row.action.Style = (Style)FindResource(st == FeatureState.Enabled ? "BtnDanger" : "Btn");
            row.action.Margin = new Thickness(0);
            row.action.IsEnabled = !busy && !_featBusy && (st == FeatureState.Enabled || st == FeatureState.Disabled);
        }

        private async Task RefreshFeaturesAsync()
        {
            BtnFeatRefresh.IsEnabled = false;
            FeatStatus.Text = Loc.T("feat.querying");
            try
            {
                var names = _features.Select(f => f.Feature).ToList();
                _featStates = await Task.Run(() => WindowsToolsNative.QueryFeatures(names));
                foreach (var f in _features) SetFeatureRow(f.Feature, _featStates.TryGetValue(f.Feature, out var st) ? st : FeatureState.Unknown);
                FeatStatus.Text = Loc.T("feat.count", _featStates.Values.Count(v => v == FeatureState.Enabled), _features.Count);
            }
            catch (Exception ex)
            {
                foreach (var f in _features) SetFeatureRow(f.Feature, FeatureState.Unknown);
                FeatStatus.Text = Loc.T("feat.queryfail", ex.Message);
            }
            finally { BtnFeatRefresh.IsEnabled = true; }
        }

        private async Task ToggleFeatureAsync(FeatureItem f)
        {
            if (_featBusy) return;
            var before = _featStates.TryGetValue(f.Feature, out var s0) ? s0 : FeatureState.Unknown;
            bool enable = before != FeatureState.Enabled;
            if (!SotheDialog.Confirm(this, Loc.T(enable ? "feat.confirm.title.on" : "feat.confirm.title.off"),
                    Loc.T(enable ? "feat.confirm.on" : "feat.confirm.off", f.DisplayName, f.DisplayDesc),
                    Loc.T(enable ? "feat.enable" : "feat.disable"), danger: !enable)) return;

            _featBusy = true;
            foreach (var other in _features) SetFeatureRow(other.Feature, _featStates.TryGetValue(other.Feature, out var st) ? st : FeatureState.Unknown, busy: other == f);
            BtnFeatRefresh.IsEnabled = false;
            FeatStatus.Text = Loc.T(enable ? "feat.working.on" : "feat.working.off", f.DisplayName);
            try
            {
                bool restart = await Task.Run(() => WindowsToolsNative.SetFeature(f.Feature, enable));
                _eng.Log($"FEATURE {f.Feature} -> {(enable ? "ON" : "OFF")}{(restart ? " (restart pending)" : "")}");
                _featBusy = false;
                await RefreshFeaturesAsync();
                var msg = Loc.T(enable ? "feat.done.on" : "feat.done.off", f.DisplayName) + (restart ? Loc.T("feat.restart") : "");
                FeatStatus.Text = msg;
                ShowToast(msg);
            }
            catch (Exception ex)
            {
                _featBusy = false;
                _eng.Log($"FEATURE {f.Feature} error: {ex.Message}");
                foreach (var other in _features) SetFeatureRow(other.Feature, _featStates.TryGetValue(other.Feature, out var st) ? st : FeatureState.Unknown);
                BtnFeatRefresh.IsEnabled = true;
                FeatStatus.Text = Loc.T("feat.failshort");
                SotheDialog.Info(this, Loc.T("feat.failtitle"), Loc.T("feat.failbody", f.DisplayName, ex.Message));
            }
        }
    }
}
