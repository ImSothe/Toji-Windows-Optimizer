using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Sothe
{
    public partial class MainWindow : Window
    {
        private readonly SotheEngine _eng = new SotheEngine();
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
        private bool _busy;                // hay una operacion (aplicar/revertir/limpiar) en curso
        private bool _sessionDirty;        // el usuario ya ha aplicado o revertido algo en esta sesion

        private enum ViewMode { List, Grid }
        private enum ToastKind { Ok, Warn, Error }
        private const double GridTileMinWidth = 330;

        // Nivel de peligro -> clave de color del tema y etiqueta visible
        private static readonly Dictionary<string, string> DangerKey = new Dictionary<string, string>
        { { "safe", "Safe" }, { "moderate", "Moderate" }, { "high", "High" }, { "extreme", "Extreme" } };
        private static string DangerLabelOf(string k) => Loc.T("lbl." + (k == "todos" ? "all" : k ?? ""));
        private static readonly Dictionary<string, string> ProfColor = new Dictionary<string, string>
        { { "competitivo", "#8B5CF6" }, { "ciber", "#38BDF8" }, { "limpio", "#A7A0BD" } };

        private class Item { public Tweak Tweak; public CheckBox Box; public Border Card; public string Text; public string Danger; public string Cat; }
        private class Section { public FrameworkElement Header; public WrapPanel Panel; public string Cat; }

        private static Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        private Brush R(string key) => (Brush)FindResource(key);
        private Brush DangerBrush(string danger) => danger != null && DangerKey.TryGetValue(danger, out var k) ? R(k) : R("TextSub");
        private static string CatName(string cat) { var k = "cat." + cat; var t = Loc.T(k); return t == k ? cat : t; }

        private static Brush Tint(Brush b, byte alpha)
        {
            var c = (b as SolidColorBrush)?.Color ?? Colors.White;
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        public MainWindow() : this(null) { }

        // Con "state" (cambio de idioma) la ventana nueva recupera lo marcado, la pestana, el modo y el tamano
        internal MainWindow(UiState state)
        {
            InitializeComponent();
            LangBtn.Content = Loc.T("lang.button");
            LangBtn.ToolTip = Loc.T("lang.tooltip");
            LangBtn.Click += (s, e) => SwitchLanguage();
            BorderlessWindow.Attach(this, RootBorder, BtnMinimize, BtnMaximize, BtnCloseWindow, TitleBar, HeaderBar);
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
                if (_restoring) { ApplyFilter(); return; }
                if (!SotheDialog.Confirm(this, Loc.T("adv.title"), Loc.T("adv.body"), Loc.T("adv.ok"), danger: true))
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
            BtnFlushDns.Click += async (s, e) => await RunQuiet("ipconfig /flushdns", Loc.T("dns.done"));
            BtnSFC.Click += (s, e) => _eng.RunPowerShellVisible("SFC", "sfc /scannow");
            BtnDISM.Click += (s, e) => _eng.RunPowerShellVisible("DISM", "DISM /Online /Cleanup-Image /RestoreHealth");
            BtnNet.Click += (s, e) =>
            {
                if (!SotheDialog.Confirm(this, Loc.T("net.title"), Loc.T("net.body"), Loc.T("net.ok"))) return;
                _eng.RunPowerShellVisible(Loc.T("net.title"), "netsh winsock reset; netsh int ip reset; ipconfig /flushdns; ipconfig /release; ipconfig /renew; Write-Host '" + Loc.T("net.restart") + "' -ForegroundColor Yellow");
            };
            BtnWU.Click += (s, e) => _eng.RunPowerShellVisible(Loc.T("wu.title"), "net stop wuauserv; net stop bits; net stop cryptsvc; Rename-Item \"$env:SystemRoot\\SoftwareDistribution\" SoftwareDistribution.old -ErrorAction SilentlyContinue; Rename-Item \"$env:SystemRoot\\System32\\catroot2\" catroot2.old -ErrorAction SilentlyContinue; net start wuauserv; net start bits; net start cryptsvc; Write-Host '" + Loc.T("wu.done") + "' -ForegroundColor Green");
            BtnChkdsk.Click += (s, e) => _eng.RunPowerShellVisible("CHKDSK", "chkdsk $env:SystemDrive /scan");

            Loaded += async (s, e) =>
            {
                UpdateItemWidth();
                if (_loadError != null)
                    SotheDialog.Info(this, Loc.T("load.err.title"), Loc.T("load.err.body") + _loadError);
                await CheckAppUpdateAsync();   // puede reiniciar la app; si lo hace, no sigue
                await CheckOtaAsync();
            };

            ApplyFilter();
            UpdateCount();
            if (state != null) RestoreState(state);
        }

        // Todo lo que el usuario ha tocado y no esta en el sistema: se conserva al cambiar de idioma
        internal sealed class UiState
        {
            public HashSet<string> Tweaks; public List<string> Apps; public int Tab; public bool Advanced; public bool Grid;
            public string TweakSearch, AppSearch; public double Left, Top, Width, Height; public WindowState State;
            public bool RestorePointDone, SessionDirty;
        }

        private bool _restoring;

        private UiState CaptureState() => new UiState
        {
            Tweaks = new HashSet<string>(CheckedIds()), Apps = CheckedAppIds(), Tab = Tabs.SelectedIndex, Advanced = AdvancedChk.IsChecked == true,
            Grid = _view == ViewMode.Grid, TweakSearch = SearchBox.Text, AppSearch = AppSearch.Text,
            Left = Left, Top = Top, Width = Width, Height = Height, State = WindowState,
            RestorePointDone = _restorePointDone, SessionDirty = _sessionDirty
        };

        private void RestoreState(UiState st)
        {
            _restoring = true;
            try
            {
                foreach (var kv in _boxes) kv.Value.IsChecked = st.Tweaks.Contains(kv.Key);
                foreach (var kv in _appBoxes) kv.Value.IsChecked = st.Apps.Contains(kv.Key);
                AdvancedChk.IsChecked = st.Advanced;
                if (st.Grid) ViewGrid.IsChecked = true;
                SearchBox.Text = st.TweakSearch ?? ""; AppSearch.Text = st.AppSearch ?? "";
                Tabs.SelectedIndex = st.Tab;
                _restorePointDone = st.RestorePointDone; _sessionDirty = st.SessionDirty;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = st.Left; Top = st.Top; Width = st.Width; Height = st.Height;
                if (st.State == WindowState.Maximized) WindowState = WindowState.Maximized;
            }
            finally { _restoring = false; }
            ApplyFilter(); UpdateCount(); UpdateAppCount();
        }

        // Cambia el idioma: se guarda, y se abre una ventana nueva (los textos construidos en codigo se rehacen) con el mismo estado
        private void SwitchLanguage()
        {
            if (_busy) { ShowToast(Loc.T("lang.busy"), ToastKind.Warn); return; }
            var state = CaptureState();
            Loc.SetLanguage(Loc.IsSpanish ? Loc.English : Loc.Spanish);
            Loc.Save(_eng.Root);
            var next = new MainWindow(state);
            var old = this;
            Application.Current.MainWindow = next;   // asi cerrar la vieja no termina la aplicacion
            next.Show();
            old.Close();
        }

        private IEnumerable<string> CheckedIds() => _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key);
        private void RefreshApplied() => _appliedCount = _eng.GetApplied().Count;
        private void UpdateCount() => CountText.Text = Loc.T("count.fmt", _boxes.Count(kv => kv.Value.IsChecked == true), _appliedCount);
        private string TweakName(string id) => _tweaks.FirstOrDefault(t => t.Id == id)?.DisplayName ?? id;

        private static readonly string[] CatOrder =
        { "Performance", "Input", "Power", "Network", "GPU", "Interface", "Privacy", "Storage", "Debloat", "Activation", "Windows Update", "Security", "FORBIDDEN" };

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

                    var item = new Item { Tweak = t, Box = cb, Card = card, Danger = t.Danger, Cat = c, Text = Fold(t.Name + " " + t.Desc + " " + (t.Es?.Name ?? "") + " " + (t.Es?.Desc ?? "") + " " + t.Id + " " + t.Category) };
                    RenderCard(item);
                    panel.Children.Add(card);
                    _items.Add(item);
                }
            }
        }

        private FrameworkElement BuildSectionHeader(string cat, int count)
        {
            bool forbidden = cat == "FORBIDDEN";
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
                var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(new TextBlock { Text = t.DisplayName, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 440 });
                Grid.SetColumn(name, 2);
                var desc = new TextBlock { Text = t.DisplayDesc, FontSize = 13.5, Foreground = R("TextSub"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(18, 0, 12, 0) };
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
                var name = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                name.Inlines.Add(new Run(t.DisplayName));
                Grid.SetColumn(name, 1);
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(10), Background = Tint(risk, 0x26), Padding = new Thickness(9, 2, 9, 3),
                    Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = DangerLabelOf(t.Danger), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = risk }
                };
                Grid.SetColumn(pill, 2);
                header.Children.Add(it.Box); header.Children.Add(name); header.Children.Add(pill);
                DockPanel.SetDock(header, Dock.Top);
                dp.Children.Add(header);

                var body = new StackPanel { Margin = new Thickness(32, 8, 0, 0) };
                body.Children.Add(new TextBlock { Text = t.DisplayDesc, FontSize = 13.5, Foreground = R("TextSub"), TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
                if (!string.IsNullOrEmpty(t.DisplayWarning))
                    body.Children.Add(new TextBlock { Text = "⚠ " + t.DisplayWarning, FontSize = 12.5, Foreground = R("High"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
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
            if (compact && !string.IsNullOrEmpty(t.DisplayWarning)) wp.Children.Add(Badge(Loc.T("badge.warn"), R("High")));
            if (t.Anticheat) wp.Children.Add(Badge(Loc.T("badge.anticheat"), R("Extreme")));
            if (t.Reboot) wp.Children.Add(Badge(Loc.T("badge.reboot"), R("TextSub")));
            if (compact)
                wp.Children.Add(new TextBlock
                {
                    Text = DangerLabelOf(t.Danger), Foreground = risk, FontSize = 12.5,
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
            sp.Children.Add(new TextBlock { Text = t.DisplayName, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = t.DisplayDesc, Foreground = R("TextSub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            if (!string.IsNullOrEmpty(t.DisplayWarning))
                sp.Children.Add(new TextBlock { Text = "⚠ " + t.DisplayWarning, Foreground = R("High"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            if (t.Anticheat)
                sp.Children.Add(new TextBlock { Text = Loc.T("tt.anticheat"), Foreground = R("Extreme"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            if (t.Reboot)
                sp.Children.Add(new TextBlock { Text = Loc.T("tt.reboot"), Foreground = R("TextDim"), Margin = new Thickness(0, 6, 0, 0) });
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
                var pb = new Button { Content = p.DisplayName, Style = (Style)FindResource("Pill"), Tag = p, ToolTip = p.DisplayDesc };
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
            ProfileLabel.Text = string.IsNullOrEmpty(p.DisplayDesc) ? p.DisplayName : $"{p.DisplayName} — {p.DisplayDesc}";
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
                content.Children.Add(new TextBlock { Text = DangerLabelOf(rk) });
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
            CatCombo.Items.Add(new ComboBoxItem { Content = Loc.T("cat.all"), Tag = "todas" });
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
                bool hiddenByAdv = !adv && (it.Cat == "FORBIDDEN" || it.Danger == "extreme");
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
            _busy = busy;
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
            // LIFO: lo ultimo que se aplico es lo primero que se deshace (ver SotheEngine.OrderForRevert)
            var toRevert = _eng.OrderForRevert(applied.Where(a => !ids.Contains(a)));
            var toApply = ids.Where(i => !applied.Contains(i)).ToList();

            if (toApply.Count == 0 && toRevert.Count == 0)
            {
                ShowToast(Loc.T("idle"));
                return;
            }

            if (dryRun)
            {
                var sb = new StringBuilder(Loc.T("dry.header"));
                sb.AppendLine(Loc.T("dry.apply", toApply.Count));
                foreach (var id in toApply) sb.AppendLine("   +  " + TweakName(id));
                sb.AppendLine();
                sb.AppendLine(Loc.T("dry.revert", toRevert.Count));
                foreach (var id in toRevert) sb.AppendLine("   −  " + TweakName(id));
                SotheDialog.Info(this, Loc.T("dry.title"), sb.ToString().TrimEnd());
                return;
            }

            var newExtreme = _tweaks.Where(t => toApply.Contains(t.Id) && t.Danger == "extreme").ToList();
            if (newExtreme.Count > 0)
            {
                var msg = Loc.T("danger.head") + string.Join("\n", newExtreme.Select(t => "   •  " + t.DisplayName)) + Loc.T("danger.tail");
                if (!SotheDialog.Confirm(this, Loc.T("danger.title"), msg, Loc.T("danger.ok"), danger: true)) return;
            }

            _sessionDirty = true;   // no auto-reiniciar por una actualizacion mientras se toca el sistema
            if (toApply.Count > 0 && !await EnsureRestorePointAsync()) return;
            SetBusy(true);
            var errs = new List<string>(); bool reboot = false;
            int okApplied = 0, okReverted = 0;
            var progress = new Progress<Tuple<int, string>>(p => { Progress.Value = p.Item1; StatusText.Text = p.Item2; });
            var rep = (IProgress<Tuple<int, string>>)progress;

            try
            {
                await Task.Run(() =>
                {
                    int total = Math.Max(toRevert.Count + toApply.Count, 1); int i = 0;
                    foreach (var id in toRevert)
                    {
                        i++; rep.Report(Tuple.Create(i * 100 / total, Loc.T("prog.revert", TweakName(id))));
                        var rv = _eng.RevertTweak(id);
                        if (rv.Ok) okReverted++;
                        errs.AddRange(rv.Errors.Select(e => TweakName(id) + " → " + e));
                    }
                    foreach (var id in toApply)
                    {
                        i++; rep.Report(Tuple.Create(i * 100 / total, Loc.T("prog.apply", TweakName(id))));
                        var t = _tweaks.First(x => x.Id == id); var r = _eng.ApplyTweak(t);
                        errs.AddRange(r.Errors.Select(e => t.DisplayName + " → " + e));
                        if (r.Ok && !r.Skipped) okApplied++;
                        if (t.Reboot) reboot = true;
                    }
                });
            }
            catch (Exception ex) { errs.Add(ex.Message); }
            finally { SetBusy(false); }

            RefreshApplied(); UpdateCount();
            StatusText.Text = Loc.T("done");
            var summary = Loc.T("summary", okApplied, toApply.Count, okReverted, toRevert.Count);
            if (reboot) summary += Loc.T("summary.reboot");
            if (errs.Count > 0) summary += Loc.T("summary.err", errs.Count) + string.Join("\n", errs.Select(x => "   •  " + x));
            SotheDialog.Info(this, errs.Count > 0 ? Loc.T("summary.title.err") : Loc.T("summary.title.ok"), summary);
        }

        private async Task RevertAll()
        {
            if (!SotheDialog.Confirm(this, Loc.T("revall.title"), Loc.T("revall.body"), Loc.T("revall.title"), danger: true)) return;
            var applied = _eng.AppliedNewestFirst();   // LIFO
            var errs = new List<string>();
            _sessionDirty = true;
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
                        i++; rep.Report(Tuple.Create(i * 100 / total, Loc.T("prog.revert", TweakName(id))));
                        errs.AddRange(_eng.RevertTweak(id).Errors.Select(e => TweakName(id) + " → " + e));
                    }
                });
            }
            catch (Exception ex) { errs.Add(ex.Message); }
            finally { SetBusy(false); }

            foreach (var kv in _boxes) kv.Value.IsChecked = false;
            ClearProfileSelection(Loc.T("profiles.manual"));
            RefreshApplied(); UpdateCount();
            StatusText.Text = Loc.T("done");
            SotheDialog.Info(this, Loc.T("revall.title"), errs.Count == 0
                ? Loc.T("revall.done")
                : Loc.T("revall.warn") + string.Join("\n", errs.Select(x => "   •  " + x)));
        }

        private bool _restorePointDone;   // una vez por sesion: Windows limita los puntos de restauracion

        // Antes del primer lote de cambios de la sesion. Si falla, el usuario decide si sigue sin red de seguridad.
        private async Task<bool> EnsureRestorePointAsync()
        {
            if (_restorePointDone || AutoRestoreChk.IsChecked != true) return true;
            SetBusy(true);
            Progress.IsIndeterminate = true;
            StatusText.Text = Loc.T("rp.creating.apply");
            bool ok;
            try { ok = await Task.Run(() => _eng.CreateRestorePoint()); }
            catch { ok = false; }
            finally { SetBusy(false); }
            StatusText.Text = Loc.T("done");
            if (ok) { _restorePointDone = true; ShowToast(Loc.T("rp.created")); return true; }
            return SotheDialog.Confirm(this, Loc.T("rp.none.title"), Loc.T("rp.none.body"), Loc.T("rp.none.ok"), danger: true);
        }

        private async Task CreateRestorePoint()
        {
            SetBusy(true);
            Progress.IsIndeterminate = true;
            StatusText.Text = Loc.T("rp.creating");
            bool ok;
            try { ok = await Task.Run(() => _eng.CreateRestorePoint()); }
            catch { ok = false; }
            finally { SetBusy(false); }
            StatusText.Text = Loc.T("done");
            if (ok) ShowToast(Loc.T("rp.created"));
            else ShowToast(Loc.T("rp.fail"), ToastKind.Error);
        }

        private async Task CleanTemp()
        {
            SetBusy(true);
            Progress.IsIndeterminate = true;
            StatusText.Text = Loc.T("clean.progress");
            try { await Task.Run(() => _eng.ClearTemp()); ShowToast(Loc.T("clean.done")); }
            catch (Exception ex) { ShowToast(Loc.T("clean.fail", ex.Message), ToastKind.Error); }
            finally { SetBusy(false); StatusText.Text = Loc.T("done"); }
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
                ShowToast(Loc.T("ota.updated", r.Version));
        }

        // Comprobacion silenciosa de nueva version de la app. Solo descarga si la firma del autor es valida y el
        // SHA-256 coincide; se instala al reiniciar (nunca a mitad de sesion). Sin GitHubRepo configurado, no hace nada.
        private async Task CheckAppUpdateAsync()
        {
            try
            {
                var updater = new AppUpdater(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                if (!updater.IsConfigured) return;
                // Solo se prepara una actualizacion si la carpeta de datos es de confianza (ver SecureStorage)
                if (!OperatingSystem.IsWindows() || !SecureStorage.RootTrusted) { _eng.Log("Auto-update skipped: the data folder is not trusted."); return; }
                var check = await updater.CheckAsync();
                if (check.Status != UpdateStatus.Available)
                {
                    if (check.Status == UpdateStatus.Failed) _eng.Log("Auto-update: " + check.Message);
                    return;
                }
                var stageDir = System.IO.Path.Combine(_eng.Root, "update");
                var staged = await updater.DownloadAndStageAsync(check, stageDir);
                if (staged.Status != UpdateStatus.Staged)
                {
                    if (staged.Status == UpdateStatus.Failed) _eng.Log("Auto-update: " + staged.Message);
                    return;
                }

                // 100% automatico: si el usuario aun no ha tocado el sistema y no hay nada en curso, se instala y
                // reinicia sola ahora mismo (un solo reinicio, sin clics). Si ya estaba aplicando/revirtiendo algo,
                // se pospone al proximo arranque para no interrumpir un cambio a medias.
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (!_busy && !_sessionDirty && OperatingSystem.IsWindows() && !string.IsNullOrEmpty(Environment.ProcessPath)
                    && AppUpdater.TryApplyStaged(Environment.ProcessPath, stageDir, ver, out _))
                {
                    _eng.Log($"Auto-update: installing v{staged.Version} and restarting");
                    ShowToast(Loc.T("upd.installing", staged.Version));
                    await Task.Delay(1200);   // deja ver el aviso antes de reiniciar
                    Application.Current.Shutdown();
                    return;
                }
                ShowToast(Loc.T("upd.ready", staged.Version));
            }
            catch (Exception ex) { _eng.Log("Auto-update: " + ex.Message); }
        }

        private async Task SaveProfile()
        {
            var ids = CheckedIds().ToList();
            if (ids.Count == 0) { ShowToast(Loc.T("profile.pick"), ToastKind.Warn); return; }
            var name = SotheDialog.Prompt(this, Loc.T("profile.save.title"),
                Loc.T("profile.save.body", ids.Count),
                Loc.T("profile.default"), Loc.T("profile.save.ok"), Loc.T("profile.name.ph"),
                validate: s =>
                {
                    if (string.IsNullOrWhiteSpace(s)) return Loc.T("profile.err.empty");
                    if (_eng.IsReservedProfileName(s, out var suggestion))
                        return Loc.T("profile.err.reserved", s.Trim(), suggestion);
                    return null;
                });
            if (name == null) return;
            name = name.Trim();
            try { _eng.SaveProfile(name, ids); }
            catch (Exception ex) { SotheDialog.Info(this, Loc.T("profile.fail.title"), Loc.T("profile.fail.body", ex.Message)); return; }
            BuildProfileBar();
            ShowToast(Loc.T("profile.saved", name));
        }

        // Un clic: copia el codigo al portapapeles y avisa con un toast
        private void ExportCode()
        {
            var ids = CheckedIds().ToList();
            if (ids.Count == 0) { ShowToast(Loc.T("export.none"), ToastKind.Warn); return; }
            try
            {
                Clipboard.SetText(_eng.ExportCode(ids));
                ShowToast(Loc.T("export.done"));
            }
            catch { ShowToast(Loc.T("export.fail"), ToastKind.Error); }
        }

        private void ImportCode()
        {
            string prefill = "";
            try
            {
                if (Clipboard.ContainsText())
                {
                    var clip = Clipboard.GetText().Trim();
                    if (clip.StartsWith(SotheEngine.ExportPrefix, StringComparison.OrdinalIgnoreCase)) prefill = clip;
                }
            }
            catch { }

            List<string> ids = null;
            var code = SotheDialog.Prompt(this, Loc.T("import.title"),
                Loc.T("import.body"),
                prefill, Loc.T("import.ok"), SotheEngine.ExportPrefix + "…", code: true,
                validate: text =>
                {
                    if (string.IsNullOrWhiteSpace(text)) return Loc.T("import.empty");
                    try { ids = _eng.ImportCode(text); }
                    catch { return Loc.T("import.bad"); }
                    if (!ids.Any(id => id != null && _boxes.ContainsKey(id))) return Loc.T("import.none");
                    return null;
                });
            if (code == null) return;

            var set = new HashSet<string>(ids.Where(id => id != null));
            foreach (var kv in _boxes) kv.Value.IsChecked = set.Contains(kv.Key);
            ClearProfileSelection(Loc.T("profile.imported"));
            UpdateCount();
            ShowToast(Loc.T("import.done", set.Count(id => _boxes.ContainsKey(id))));
        }
    }
}
