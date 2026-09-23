using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Toji.Licensing;

namespace Toji
{
    public partial class MainWindow : Window
    {
        private readonly TojiEngine _eng = new TojiEngine();
        private readonly LicenseService _license;
        private List<Tweak> _tweaks;
        private List<Profile> _profiles;
        private string _loadError;

        private readonly Dictionary<string, CheckBox> _boxes = new Dictionary<string, CheckBox>();
        private readonly List<Item> _items = new List<Item>();
        private readonly List<Section> _sections = new List<Section>();
        private readonly Dictionary<string, Button> _riskBtns = new Dictionary<string, Button>();
        private string _fDanger = "todos";
        private string _fCat = "todas";
        private ViewMode _view = ViewMode.List;
        private int _appliedCount;
        private Border _pressedCard;

        private enum ViewMode { List, Grid }
        private enum ToastKind { Ok, Warn, Error }
        private const double GridTileMinWidth = 330;

        // Nivel de peligro -> clave de color del tema y etiqueta visible
        private static readonly Dictionary<string, string> DangerKey = new Dictionary<string, string>
        { { "safe", "Safe" }, { "moderate", "Moderate" }, { "high", "High" }, { "extreme", "Extreme" } };
        private static readonly Dictionary<string, string> DangerLabel = new Dictionary<string, string>
        { { "todos", "Todos" }, { "safe", "Seguro" }, { "moderate", "Moderado" }, { "high", "Alto" }, { "extreme", "Prohibido" } };
        private static readonly Dictionary<string, string> ProfColor = new Dictionary<string, string>
        { { "competitivo", "#8B5CF6" }, { "ciber", "#38BDF8" }, { "limpio", "#A7A0BD" } };
        private static readonly Dictionary<string, string> CatDisplay = new Dictionary<string, string>
        { { "Energia", "Energía" }, { "Activacion", "Activación" }, { "Comunicacion", "Comunicación" } };

        private class Item { public Tweak Tweak; public CheckBox Box; public Border Card; public string Text; public string Danger; public string Cat; }
        private class Section { public FrameworkElement Header; public WrapPanel Panel; public string Cat; }

        private static Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        private Brush R(string key) => (Brush)FindResource(key);
        private Brush DangerBrush(string danger) => danger != null && DangerKey.TryGetValue(danger, out var k) ? R(k) : R("TextSub");
        private static string CatName(string cat) => CatDisplay.TryGetValue(cat, out var d) ? d : cat;

        private static Brush Tint(Brush b, byte alpha)
        {
            var c = (b as SolidColorBrush)?.Color ?? Colors.White;
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        public MainWindow()
        {
            InitializeComponent();
            NativeTheme.ApplyDarkTitleBar(this);
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (ver != null) VersionText.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            try
            {
                _tweaks = _eng.LoadTweaks();
                _profiles = _eng.LoadProfiles();
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                _tweaks = new List<Tweak>(); _profiles = new List<Profile>();
            }

            _license = LicenseFactory.CreateDefault(_eng.Root);
            _license.StatusChanged += st => Dispatcher.Invoke(() => UpdateLicenseBadge(st));
            UpdateLicenseBadge(_license.Status);
            LicenseBadge.Click += (s, e) => ShowLicenseInfo();

            BuildList();
            BuildProfileBar();
            BuildRiskChips();
            BuildCatCombo();
            InitTabs();

            foreach (var id in _eng.GetApplied()) if (_boxes.ContainsKey(id)) _boxes[id].IsChecked = true;
            RefreshApplied();

            SearchBox.TextChanged += (s, e) => ApplyFilter();
            AdvancedChk.Checked += (s, e) =>
            {
                if (!TojiDialog.Confirm(this, "Modo avanzado",
                        "El Modo avanzado muestra los ajustes PROHIBIDOS: desactivan protecciones de Windows y pueden dejar el sistema inestable o inservible.\n\n" +
                        "Úsalo solo si sabes exactamente lo que haces y con un punto de restauración creado.",
                        "Mostrar PROHIBIDO", danger: true))
                { AdvancedChk.IsChecked = false; return; }
                ApplyFilter();
            };
            AdvancedChk.Unchecked += (s, e) => ApplyFilter();

            ViewList.Checked += (s, e) => SetView(ViewMode.List);
            ViewGrid.Checked += (s, e) => SetView(ViewMode.Grid);
            ListScroll.ScrollChanged += (s, e) => { if (e.ViewportWidthChange != 0) UpdateItemWidth(); };

            BtnApply.Click += async (s, e) => await ApplyOrDry(false);
            BtnDry.Click += async (s, e) => await ApplyOrDry(true);
            BtnSaveProfile.Click += async (s, e) => await SaveProfile();
            BtnRevertAll.Click += async (s, e) => await RevertAll();
            BtnRestore.Click += async (s, e) => await CreateRestorePoint();
            BtnExport.Click += (s, e) => ExportCode();
            BtnImport.Click += (s, e) => ImportCode();
            BtnClean.Click += async (s, e) => await CleanTemp();

            // Reparaciones
            BtnClean2.Click += async (s, e) => await CleanTemp();
            BtnFlushDns.Click += async (s, e) => await RunQuiet("ipconfig /flushdns", "Caché DNS vaciada");
            BtnSFC.Click += (s, e) => _eng.RunPowerShellVisible("SFC", "sfc /scannow");
            BtnDISM.Click += (s, e) => _eng.RunPowerShellVisible("DISM", "DISM /Online /Cleanup-Image /RestoreHealth");
            BtnNet.Click += (s, e) =>
            {
                if (!TojiDialog.Confirm(this, "Reset de red", "Se reiniciarán Winsock, TCP/IP y DNS y se renovará la IP. Perderás la conexión unos segundos y tendrás que reiniciar el PC.", "Resetear red")) return;
                _eng.RunPowerShellVisible("Reset de red", "netsh winsock reset; netsh int ip reset; ipconfig /flushdns; ipconfig /release; ipconfig /renew; Write-Host 'Reinicia el PC para completar el reset.' -ForegroundColor Yellow");
            };
            BtnWU.Click += (s, e) => _eng.RunPowerShellVisible("Reparar Windows Update", "net stop wuauserv; net stop bits; net stop cryptsvc; Rename-Item \"$env:SystemRoot\\SoftwareDistribution\" SoftwareDistribution.old -ErrorAction SilentlyContinue; Rename-Item \"$env:SystemRoot\\System32\\catroot2\" catroot2.old -ErrorAction SilentlyContinue; net start wuauserv; net start bits; net start cryptsvc; Write-Host 'Cache de Windows Update reiniciada.' -ForegroundColor Green");
            BtnChkdsk.Click += (s, e) => _eng.RunPowerShellVisible("CHKDSK", "chkdsk $env:SystemDrive /scan");

            Loaded += async (s, e) =>
            {
                UpdateItemWidth();
                if (_loadError != null)
                    TojiDialog.Info(this, "Error al cargar datos", "No se pudieron cargar los datos (tweaks.json).\n\n" + _loadError);
                try { await _license.RefreshOnlineAsync(); } catch (Exception ex) { _eng.Log("Licencia: " + ex.Message); }
                await CheckOtaAsync();
            };

            ApplyFilter();
            UpdateCount();
        }

        private IEnumerable<string> CheckedIds() => _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key);
        private void RefreshApplied() => _appliedCount = _eng.GetApplied().Count;
        private void UpdateCount() => CountText.Text = $"{_boxes.Count(kv => kv.Value.IsChecked == true)} seleccionados  ·  {_appliedCount} aplicados";
        private string TweakName(string id) => _tweaks.FirstOrDefault(t => t.Id == id)?.Name ?? id;

        private static readonly string[] CatOrder =
        { "Rendimiento", "Input", "Energia", "Red", "GPU", "Interfaz", "Privacidad", "Almacenamiento", "Debloat", "Activacion", "Windows Update", "Seguridad", "PROHIBIDO" };

        private static string Fold(string s) => TextUtil.Fold(s);

        // ---------------- Lista de ajustes (vista Lista / Cuadricula) ----------------
        private void BuildList()
        {
            var cats = CatOrder.Where(c => _tweaks.Any(t => t.Category == c)).ToList();
            foreach (var c in _tweaks.Select(t => t.Category).Distinct()) if (!cats.Contains(c)) cats.Add(c);

            foreach (var c in cats)
            {
                var tweaks = _tweaks.Where(x => x.Category == c).ToList();
                var head = BuildSectionHeader(c, tweaks.Count);
                var panel = new WrapPanel();
                RootList.Children.Add(head);
                RootList.Children.Add(panel);
                _sections.Add(new Section { Header = head, Panel = panel, Cat = c });

                foreach (var t in tweaks)
                {
                    var cb = new CheckBox { Tag = t.Id, VerticalAlignment = VerticalAlignment.Center };
                    cb.Checked += (s, e) => UpdateCount(); cb.Unchecked += (s, e) => UpdateCount();
                    _boxes[t.Id] = cb;

                    // Toda la tarjeta es clicable (la casilla gestiona su propio clic)
                    var card = new Border { Style = (Style)FindResource("TweakCard"), Tag = cb };
                    card.MouseLeftButtonDown += (s, e) => _pressedCard = card;
                    card.MouseLeftButtonUp += (s, e) => { if (_pressedCard == card) cb.IsChecked = cb.IsChecked != true; _pressedCard = null; };

                    var item = new Item { Tweak = t, Box = cb, Card = card, Danger = t.Danger, Cat = c, Text = Fold(t.Name + " " + t.Desc + " " + t.Id + " " + t.Category) };
                    RenderCard(item);
                    panel.Children.Add(card);
                    _items.Add(item);
                }
            }
        }

        private FrameworkElement BuildSectionHeader(string cat, int count)
        {
            bool forbidden = cat == "PROHIBIDO";
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 20, 0, 12) };
            head.Children.Add(new Border { Background = forbidden ? R("Extreme") : R("AccentGradient"), Width = 4, Height = 18, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) });
            head.Children.Add(new TextBlock { Text = CatName(cat).ToUpper(), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = forbidden ? R("Extreme") : R("Text"), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(new Border
            {
                Background = R("Surface2"), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2),
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = count.ToString(), FontSize = 12, Foreground = R("TextSub") }
            });
            return head;
        }

        // Rehace el contenido de la tarjeta segun la vista. La CheckBox se reutiliza: no se pierde lo marcado
        private void RenderCard(Item it)
        {
            if (it.Box.Parent is Panel old) old.Children.Remove(it.Box);
            var t = it.Tweak;
            var risk = DangerBrush(t.Danger);

            if (_view == ViewMode.List)
            {
                it.Card.Padding = new Thickness(14, 11, 14, 11);
                it.Card.Margin = new Thickness(0, 0, 0, 6);
                it.Card.CornerRadius = new CornerRadius(10);

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                it.Box.Margin = new Thickness(0, 0, 14, 0);
                it.Box.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(it.Box, 0);
                var dot = RiskDot(risk); Grid.SetColumn(dot, 1);
                var name = new TextBlock { Text = t.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 440 };
                Grid.SetColumn(name, 2);
                var desc = new TextBlock { Text = t.Desc, FontSize = 13.5, Foreground = R("TextSub"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(18, 0, 12, 0) };
                Grid.SetColumn(desc, 3);
                var tags = BuildBadges(t, risk, compact: true); Grid.SetColumn(tags, 4);

                g.Children.Add(it.Box); g.Children.Add(dot); g.Children.Add(name); g.Children.Add(desc); g.Children.Add(tags);
                it.Card.Child = g;
                it.Card.ToolTip = BuildTooltip(t);
            }
            else
            {
                it.Card.Padding = new Thickness(16, 14, 16, 14);
                it.Card.Margin = new Thickness(0, 0, 12, 12);
                it.Card.CornerRadius = new CornerRadius(12);

                var dp = new DockPanel();
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                it.Box.Margin = new Thickness(0, 1, 12, 0);
                it.Box.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(it.Box, 0);
                var name = new TextBlock { Text = t.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(name, 1);
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(10), Background = Tint(risk, 0x26), Padding = new Thickness(9, 2, 9, 3),
                    Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = DangerLabel.TryGetValue(t.Danger ?? "", out var l) ? l : t.Danger, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = risk }
                };
                Grid.SetColumn(pill, 2);
                header.Children.Add(it.Box); header.Children.Add(name); header.Children.Add(pill);
                DockPanel.SetDock(header, Dock.Top);
                dp.Children.Add(header);

                var body = new StackPanel { Margin = new Thickness(32, 8, 0, 0) };
                body.Children.Add(new TextBlock { Text = t.Desc, FontSize = 13.5, Foreground = R("TextSub"), TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
                if (!string.IsNullOrEmpty(t.Warning))
                    body.Children.Add(new TextBlock { Text = "⚠ " + t.Warning, FontSize = 12.5, Foreground = R("High"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
                var badges = BuildBadges(t, risk, compact: false);
                if (badges.Children.Count > 0) { badges.Margin = new Thickness(-6, 10, 0, 0); body.Children.Add(badges); }
                dp.Children.Add(body);

                it.Card.Child = dp;
                it.Card.ToolTip = null;
            }
        }

        private static Ellipse RiskDot(Brush risk)
        {
            var c = (risk as SolidColorBrush)?.Color ?? Colors.White;
            return new Ellipse
            {
                Width = 9, Height = 9, Fill = risk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                Effect = new DropShadowEffect { Color = c, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8 }
            };
        }

        private WrapPanel BuildBadges(Tweak t, Brush risk, bool compact)
        {
            var wp = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            if (compact && !string.IsNullOrEmpty(t.Warning)) wp.Children.Add(Badge("AVISO", R("High")));
            if (t.Anticheat) wp.Children.Add(Badge("ANTI-CHEAT", R("Extreme")));
            if (t.Reboot) wp.Children.Add(Badge("REINICIO", R("TextSub")));
            if (compact)
                wp.Children.Add(new TextBlock
                {
                    Text = DangerLabel.TryGetValue(t.Danger ?? "", out var l) ? l : t.Danger, Foreground = risk, FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold, MinWidth = 78, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center
                });
            return wp;
        }

        private static Border Badge(string text, Brush fg) => new Border
        {
            CornerRadius = new CornerRadius(6), BorderBrush = fg, BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 2),
            Margin = new Thickness(6, 0, 0, 0), Opacity = 0.9, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = fg }
        };

        private object BuildTooltip(Tweak t)
        {
            var sp = new StackPanel { MaxWidth = 390 };
            sp.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = t.Desc, Foreground = R("TextSub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            if (!string.IsNullOrEmpty(t.Warning))
                sp.Children.Add(new TextBlock { Text = "⚠ " + t.Warning, Foreground = R("High"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            if (t.Anticheat)
                sp.Children.Add(new TextBlock { Text = "Puede afectar a anti-cheats (Vanguard, FACEIT…).", Foreground = R("Extreme"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            if (t.Reboot)
                sp.Children.Add(new TextBlock { Text = "Requiere reiniciar.", Foreground = R("TextDim"), Margin = new Thickness(0, 6, 0, 0) });
            return sp;
        }

        private void SetView(ViewMode mode)
        {
            if (_view == mode) return;
            _view = mode;
            foreach (var it in _items) RenderCard(it);
            UpdateItemWidth();
        }

        // Lista: una tarjeta por fila. Cuadricula: tantas columnas como quepan (min. GridTileMinWidth)
        private void UpdateItemWidth()
        {
            double w = ListScroll.ViewportWidth - RootList.Margin.Left - RootList.Margin.Right;
            if (double.IsNaN(w) || w <= 0) return;
            double itemW = _view == ViewMode.List ? Math.Floor(w) : Math.Floor(w / Math.Max(1, (int)(w / GridTileMinWidth)));
            foreach (var s in _sections) s.Panel.ItemWidth = itemW;
        }

        // ---------------- Perfiles ----------------
        private void BuildProfileBar()
        {
            ProfileBar.Children.Clear();
            try { _profiles = _eng.LoadProfiles(); } catch { }
            foreach (var p in _profiles)
            {
                var pb = new Button { Content = p.Name, Style = (Style)FindResource("Pill"), Tag = p, ToolTip = p.Desc };
                pb.Click += (s, e) => SelectProfile((Profile)((Button)s).Tag);
                ProfileBar.Children.Add(pb);
            }
        }

        private void SelectProfile(Profile p)
        {
            var ids = new HashSet<string>(p.Tweaks ?? new List<string>());
            foreach (var kv in _boxes) kv.Value.IsChecked = ids.Contains(kv.Key);
            var col = p.Id != null && ProfColor.TryGetValue(p.Id, out var hex) ? hex : "#A78BFA";
            foreach (var child in ProfileBar.Children.OfType<Button>())
            {
                if (child.Tag is Profile pp && pp.Id == p.Id)
                {
                    child.Background = B(col); child.BorderBrush = B(col); child.Foreground = B("#0B0614");
                    child.Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString(col), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 };
                }
                else ResetStyled(child);
            }
            ProfileLabel.Text = string.IsNullOrEmpty(p.Desc) ? p.Name : $"{p.Name} — {p.Desc}";
            ProfileLabel.Foreground = B(col);
            UpdateCount();
        }

        private void ClearProfileSelection(string label)
        {
            foreach (var child in ProfileBar.Children.OfType<Button>()) ResetStyled(child);
            ProfileLabel.Text = label;
            ProfileLabel.Foreground = R("TextSub");
        }

        // Quita los valores locales para que vuelvan a mandar el estilo y sus efectos hover
        private static void ResetStyled(Control c)
        {
            c.ClearValue(BackgroundProperty); c.ClearValue(BorderBrushProperty);
            c.ClearValue(ForegroundProperty); c.ClearValue(EffectProperty);
        }

        // ---------------- Filtros ----------------
        private void BuildRiskChips()
        {
            foreach (var rk in new[] { "todos", "safe", "moderate", "high", "extreme" })
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                if (rk != "todos") content.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = DangerBrush(rk), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new TextBlock { Text = DangerLabel[rk] });
                var chip = new Button { Content = content, Style = (Style)FindResource("Pill"), Tag = rk, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 13.5 };
                chip.Click += (s, e) => SetRiskFilter((string)((Button)s).Tag);
                _riskBtns[rk] = chip;
                RiskChips.Children.Add(chip);
            }
            HighlightRisk("todos");
        }

        private void SetRiskFilter(string rk)
        {
            _fDanger = rk;
            HighlightRisk(rk);
            ApplyFilter();
        }

        private void HighlightRisk(string rk)
        {
            foreach (var kv in _riskBtns)
            {
                if (kv.Key == rk)
                {
                    var b = kv.Key == "todos" ? R("AccentHi") : DangerBrush(kv.Key);
                    kv.Value.BorderBrush = b; kv.Value.Background = Tint(b, 0x2E);
                }
                else ResetStyled(kv.Value);
            }
        }

        private void BuildCatCombo()
        {
            CatCombo.Items.Add(new ComboBoxItem { Content = "Todas las categorías", Tag = "todas" });
            foreach (var s in _sections) CatCombo.Items.Add(new ComboBoxItem { Content = CatName(s.Cat), Tag = s.Cat });
            CatCombo.SelectedIndex = 0;
            CatCombo.SelectionChanged += (s, e) => { _fCat = (CatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "todas"; ApplyFilter(); };
        }

        private void ApplyFilter()
        {
            bool adv = AdvancedChk.IsChecked == true;
            var terms = Fold(SearchBox.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int visible = 0;
            foreach (var it in _items)
            {
                bool hiddenByAdv = !adv && (it.Cat == "PROHIBIDO" || it.Danger == "extreme");
                bool ok = !hiddenByAdv
                    && (_fDanger == "todos" || it.Danger == _fDanger)
                    && (_fCat == "todas" || it.Cat == _fCat)
                    && terms.All(q => it.Text.Contains(q));
                it.Card.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
                if (ok) visible++;
            }
            foreach (var s in _sections)
            {
                var vis = _items.Any(i => i.Cat == s.Cat && i.Card.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
                s.Header.Visibility = vis;
                s.Panel.Visibility = vis;
            }
            EmptyState.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------- Notificaciones ----------------
        private void ShowToast(string message, ToastKind kind = ToastKind.Ok)
        {
            var brush = kind == ToastKind.Ok ? R("Accent") : kind == ToastKind.Warn ? R("High") : R("Extreme");
            ToastText.Text = message;
            Toast.BorderBrush = brush;
            ToastDot.Fill = brush;

            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3000))));
            Toast.BeginAnimation(OpacityProperty, fade);

            var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ToastShift.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        // ---------------- Acciones ----------------
        private void SetBusy(bool busy)
        {
            foreach (var b in new[] { BtnApply, BtnDry, BtnRevertAll, BtnRestore, BtnExport, BtnImport, BtnClean, BtnSaveProfile, BtnClean2, BtnFlushDns })
                b.IsEnabled = !busy;
            ProfileBar.IsEnabled = !busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Progress.Value = 0;
            if (!busy) Progress.IsIndeterminate = false;
        }

        private async Task ApplyOrDry(bool dryRun)
        {
            var ids = CheckedIds().ToList();
            var applied = _eng.GetApplied();
            var toRevert = applied.Where(a => !ids.Contains(a)).ToList();
            var toApply = ids.Where(i => !applied.Contains(i)).ToList();

            if (toApply.Count == 0 && toRevert.Count == 0)
            {
                ShowToast("Nada que hacer: el sistema ya coincide con lo marcado.");
                return;
            }

            if (dryRun)
            {
                var sb = new StringBuilder("No se ha tocado nada. Esto es lo que haría APLICAR:\n\n");
                sb.AppendLine($"Se aplicarían ({toApply.Count}):");
                foreach (var id in toApply) sb.AppendLine("   +  " + TweakName(id));
                sb.AppendLine();
                sb.AppendLine($"Se revertirían ({toRevert.Count}):");
                foreach (var id in toRevert) sb.AppendLine("   −  " + TweakName(id));
                TojiDialog.Info(this, "Simulación", sb.ToString().TrimEnd());
                return;
            }

            if (toApply.Count > 0 && !await PaywallDialog.EnsureCanModifyAsync(this, _license, "aplicar cambios", msg => ShowToast(msg)))
                return;

            var newExtreme = _tweaks.Where(t => toApply.Contains(t.Id) && t.Danger == "extreme").ToList();
            if (newExtreme.Count > 0)
            {
                var msg = "Vas a aplicar ajustes PROHIBIDOS:\n\n" + string.Join("\n", newExtreme.Select(t => "   •  " + t.Name)) +
                          "\n\nPueden desactivar protecciones de Windows. ¿Continuar?";
                if (!TojiDialog.Confirm(this, "Peligro", msg, "Aplicar igualmente", danger: true)) return;
            }

            SetBusy(true);
            var errs = new List<string>(); bool reboot = false;
            var progress = new Progress<Tuple<int, string>>(p => { Progress.Value = p.Item1; StatusText.Text = p.Item2; });
            var rep = (IProgress<Tuple<int, string>>)progress;

            try
            {
                await Task.Run(() =>
                {
                    int total = Math.Max(toRevert.Count + toApply.Count, 1); int i = 0;
                    foreach (var id in toRevert)
                    {
                        i++; rep.Report(Tuple.Create(i * 100 / total, "Revirtiendo: " + TweakName(id)));
                        errs.AddRange(_eng.RevertTweak(id).Errors.Select(e => TweakName(id) + " → " + e));
                    }
                    foreach (var id in toApply)
                    {
                        i++; rep.Report(Tuple.Create(i * 100 / total, "Aplicando: " + TweakName(id)));
                        var t = _tweaks.First(x => x.Id == id); var r = _eng.ApplyTweak(t);
                        errs.AddRange(r.Errors.Select(e => t.Name + " → " + e));
                        if (t.Reboot) reboot = true;
                    }
                });
            }
            catch (Exception ex) { errs.Add(ex.Message); }
            finally { SetBusy(false); }

            RefreshApplied(); UpdateCount();
            StatusText.Text = "Listo.";
            var summary = $"Aplicados: {toApply.Count}   ·   Revertidos: {toRevert.Count}";
            if (reboot) summary += "\n\nAlgunos cambios necesitan REINICIAR para surtir efecto.";
            if (errs.Count > 0) summary += $"\n\n{errs.Count} operaciones fallaron (detalle también en %ProgramData%\\TOJI\\toji.log):\n" + string.Join("\n", errs.Select(x => "   •  " + x));
            TojiDialog.Info(this, errs.Count > 0 ? "Aplicado con errores" : "Cambios aplicados", summary);
        }

        private async Task RevertAll()
        {
            if (!TojiDialog.Confirm(this, "Revertir TODO", "Se deshará todo lo aplicado por Sothe y Windows volverá a los valores que tenía antes.", "Revertir TODO", danger: true)) return;
            var applied = _eng.GetApplied().ToList();
            var errs = new List<string>();
            SetBusy(true);
            var progress = new Progress<Tuple<int, string>>(p => { Progress.Value = p.Item1; StatusText.Text = p.Item2; });
            var rep = (IProgress<Tuple<int, string>>)progress;
            try
            {
                await Task.Run(() =>
                {
                    int total = Math.Max(applied.Count, 1); int i = 0;
                    foreach (var id in applied)
                    {
                        i++; rep.Report(Tuple.Create(i * 100 / total, "Revirtiendo: " + TweakName(id)));
                        errs.AddRange(_eng.RevertTweak(id).Errors.Select(e => TweakName(id) + " → " + e));
                    }
                });
            }
            catch (Exception ex) { errs.Add(ex.Message); }
            finally { SetBusy(false); }

            foreach (var kv in _boxes) kv.Value.IsChecked = false;
            ClearProfileSelection("Selección manual");
            RefreshApplied(); UpdateCount();
            StatusText.Text = "Listo.";
            TojiDialog.Info(this, "Revertir TODO", errs.Count == 0
                ? "Todo revertido. Reinicia el PC para que se asiente."
                : "Revertido con avisos (detalle también en toji.log):\n" + string.Join("\n", errs.Select(x => "   •  " + x)));
        }

        private async Task CreateRestorePoint()
        {
            SetBusy(true);
            Progress.IsIndeterminate = true;
            StatusText.Text = "Creando punto de restauración… puede tardar un minuto.";
            bool ok;
            try { ok = await Task.Run(() => _eng.CreateRestorePoint()); }
            catch { ok = false; }
            finally { SetBusy(false); }
            StatusText.Text = "Listo.";
            if (ok) ShowToast("Punto de restauración creado");
            else ShowToast("No se pudo crear el punto de restauración", ToastKind.Error);
        }

        private async Task CleanTemp()
        {
            SetBusy(true);
            Progress.IsIndeterminate = true;
            StatusText.Text = "Limpiando temporales y papelera…";
            try { await Task.Run(() => _eng.ClearTemp()); ShowToast("Basura eliminada"); }
            catch (Exception ex) { ShowToast("No se pudo limpiar: " + ex.Message, ToastKind.Error); }
            finally { SetBusy(false); StatusText.Text = "Listo."; }
        }

        private async Task RunQuiet(string command, string done)
        {
            try { await Task.Run(() => _eng.RunPowerShellHidden(command)); ShowToast(done); }
            catch (Exception ex) { ShowToast(ex.Message, ToastKind.Error); }
        }

        // La OTA descarga en segundo plano; los datos nuevos se cargan en el siguiente arranque
        private async Task CheckOtaAsync()
        {
            var r = await new DataUpdater(_eng).CheckAndUpdateAsync();
            if (r.Status == OtaStatus.Updated)
                ShowToast($"Base de ajustes actualizada (v{r.Version}). Reinicia Sothe para verla.");
        }

        private async Task SaveProfile()
        {
            var ids = CheckedIds().ToList();
            if (ids.Count == 0) { ShowToast("Marca primero los ajustes que quieras en el perfil.", ToastKind.Warn); return; }
            if (!await PaywallDialog.EnsureCanModifyAsync(this, _license, "guardar perfiles propios", msg => ShowToast(msg))) return;
            var name = TojiDialog.Prompt(this, "Guardar perfil",
                $"Guarda los {ids.Count} ajustes marcados como un perfil propio. Aparecerá junto a los perfiles de Sothe.",
                "Mi setup", "Guardar", "Nombre del perfil",
                validate: s =>
                {
                    if (string.IsNullOrWhiteSpace(s)) return "Escribe un nombre para el perfil.";
                    if (_eng.IsReservedProfileName(s, out var suggestion))
                        return $"«{s.Trim()}» es un perfil oficial de Sothe y no se puede sobrescribir. Elige otro nombre, por ejemplo «{suggestion}».";
                    return null;
                });
            if (name == null) return;
            name = name.Trim();
            try { _eng.SaveProfile(name, ids); }
            catch (Exception ex) { TojiDialog.Info(this, "No se pudo guardar", "No se pudo guardar el perfil.\n\n" + ex.Message); return; }
            BuildProfileBar();
            ShowToast($"Perfil «{name}» guardado");
        }

        // ---------------- Licencia ----------------
        private void UpdateLicenseBadge(LicenseStatus st)
        {
            ResetStyled(LicenseBadge);
            string text; Brush accent = null;
            switch (st.State)
            {
                case LicenseState.Licensed:
                    text = "PRO";
                    LicenseBadge.Background = R("AccentGradient"); LicenseBadge.BorderBrush = R("AccentHi");
                    LicenseBadge.Foreground = Brushes.White; LicenseBadge.Effect = (Effect)FindResource("GlowAccent");
                    break;
                case LicenseState.Trial: text = $"PRUEBA · {st.DaysLeft} {(st.DaysLeft == 1 ? "día" : "días")}"; accent = R("High"); break;
                case LicenseState.TrialExpired: text = "PRUEBA TERMINADA"; accent = R("High"); break;
                case LicenseState.Revoked: text = "LICENCIA DESACTIVADA"; accent = R("Extreme"); break;
                case LicenseState.ValidationRequired: text = "VALIDAR LICENCIA"; accent = R("High"); break;
                default: text = "SIN LICENCIA"; LicenseBadge.Foreground = R("TextSub"); break;
            }
            if (accent != null) { LicenseBadge.Foreground = accent; LicenseBadge.BorderBrush = accent; LicenseBadge.Background = Tint(accent, 0x1E); }
            if (_license?.ProviderName == "Demo") text += " · DEMO";
            LicenseBadge.Content = text;
        }

        private void ShowLicenseInfo()
        {
            var st = _license.Status;
            if (!st.CanModify) { PaywallDialog.Show(this, _license); return; }
            var detail = st.State == LicenseState.Trial
                ? $"Prueba gratuita activa: te quedan {st.DaysLeft} días (hasta el {st.ExpiresAtUtc?.ToLocalTime():d})."
                : "Sothe PRO activado en este equipo.";
            TojiDialog.Info(this, "Tu licencia", $"{detail}\n\nClave: {st.MaskedKey}\nEquipo: {_license.Fingerprint.Substring(0, Math.Min(12, _license.Fingerprint.Length))}…");
        }

        // Un clic: copia el codigo al portapapeles y avisa con un toast
        private void ExportCode()
        {
            var ids = CheckedIds().ToList();
            if (ids.Count == 0) { ShowToast("Marca al menos un ajuste para exportar.", ToastKind.Warn); return; }
            try
            {
                Clipboard.SetText(_eng.ExportCode(ids));
                ShowToast("Configuración copiada al portapapeles");
            }
            catch { ShowToast("No se pudo acceder al portapapeles. Inténtalo de nuevo.", ToastKind.Error); }
        }

        private void ImportCode()
        {
            string prefill = "";
            try
            {
                if (Clipboard.ContainsText())
                {
                    var clip = Clipboard.GetText().Trim();
                    if (clip.StartsWith("TOJI-", StringComparison.OrdinalIgnoreCase)) prefill = clip;
                }
            }
            catch { }

            List<string> ids = null;
            var code = TojiDialog.Prompt(this, "Importar configuración",
                "Pega el código de Sothe que te han compartido. Se marcarán sus ajustes; nada se aplica hasta que pulses APLICAR.",
                prefill, "Importar", "TOJI-1-…", code: true,
                validate: text =>
                {
                    if (string.IsNullOrWhiteSpace(text)) return "Pega un código de Sothe.";
                    try { ids = _eng.ImportCode(text); }
                    catch { return "Código no válido. Comprueba que lo has copiado completo."; }
                    if (!ids.Any(id => id != null && _boxes.ContainsKey(id))) return "El código no contiene ajustes compatibles con esta versión.";
                    return null;
                });
            if (code == null) return;

            var set = new HashSet<string>(ids.Where(id => id != null));
            foreach (var kv in _boxes) kv.Value.IsChecked = set.Contains(kv.Key);
            ClearProfileSelection("Importado desde código");
            UpdateCount();
            ShowToast($"{set.Count(id => _boxes.ContainsKey(id))} ajustes marcados. Revisa y pulsa APLICAR.");
        }
    }
}
