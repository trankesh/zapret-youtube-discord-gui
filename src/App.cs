using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Collections;

namespace ZapretDesktop
{
    public static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            RuntimeDefaults.Initialize();
            SingleInstance instance = null;
            try
            {
                if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args.Length > 1 ? args[1] : Bundle.Detect());
                if (args.Length > 0 && args[0].StartsWith("--installer-",StringComparison.Ordinal)) return InstallerRuntime.Run(args);
                bool isPreview = args.Contains("--preview");
                if (!isPreview && !Engine.IsAdmin) {
                    if (args.Contains("--elevated")) { MessageBox.Show("Не удалось получить права администратора. Приложение будет закрыто.", "Zapret"); return 1; }
                    try { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, args.Contains("--startup") ? "--startup --elevated" : "--elevated") { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory }); }
                    catch (System.ComponentModel.Win32Exception e) { MessageBox.Show(e.NativeErrorCode == 1223 ? "Для работы Zapret нужны права администратора. Запрос отменён, приложение будет закрыто." : e.Message, "Zapret"); }
                    return 0;
                }
                instance = new SingleInstance(!isPreview);
                if (!instance.First) { instance.ShowExisting(); return 0; }
                if (isPreview) ApplicationPaths.DataDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
                string startupNotice = null;
                if (!isPreview) {
                    ApplicationPaths.Configure();
                    try { if (AutoStart.RefreshFromPortable(Assembly.GetExecutingAssembly().Location)) startupNotice="Автозапуск перенесён/обновлён в Планировщике Windows. Повторные запросы UAC при входе не нужны."; }
                    catch (Exception error) { startupNotice="Не удалось обновить автозапуск: " + error.Message + ". Проверь переключатель в настройках."; }
                }
                var app = new Application();
                MouseScrolling.Initialize();
                app.DispatcherUnhandledException += (s, e) => { MessageBox.Show(e.Exception.Message, "Zapret", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
                var shell = new Shell(isPreview);
                if (!isPreview && ApplicationPaths.MigrationError!=null) shell.Window.Loaded += (s,e) => MessageBox.Show(shell.Window,ApplicationPaths.MigrationError,"Перенос профиля",MessageBoxButton.OK,MessageBoxImage.Warning);
                if (startupNotice != null) shell.RecordStartupNotice(startupNotice);
                instance.Listen(() => { if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(new Action(shell.ShowFromTray)); });
                if (args.Contains("--startup")) shell.ApplyStartup();
                if (args.Contains("--preview"))
                {
                    if (args.Contains("--preview-report")) shell.LoadReportPreview();
                    shell.Window.ShowInTaskbar = false;
                    shell.Window.Left = -20000;
                    shell.Window.Show();
                    shell.Window.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(delegate { }));
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"); Directory.CreateDirectory(dir);
                    shell.PreviewAdvanced(false);
                    foreach (string page in new[] { "Home", "Tests", "Settings" })
                    {
                        CapturePreview(shell, dir, page, page);
                    }
                    shell.PreviewAdvanced(true);
                    foreach (string page in new[] { "Strategies", "Tests", "Lists", "Settings", "Log" }) CapturePreview(shell, dir, page, "Advanced-" + page);
                    CapturePreview(shell, dir, "Settings", "Advanced-Settings-filters", Double.NegativeInfinity);
                    CapturePreview(shell, dir, "Settings", "Advanced-Settings-bottom", Double.PositiveInfinity);
                    shell.PreviewAdvanced(false);
                    foreach (string page in new[] { "Home", "Tests" }) CapturePreview(shell, dir, page, page + "-150pct", Double.NaN, 1.5);
                    CapturePreview(shell, dir, "Home", "StrategyPicker", Double.NaN, 1, true);
                    CapturePreview(shell, dir, "Home", "Home-Active", Double.NaN, 1, false, true);
                    string originalAccent = shell.PreviewAccent("#6EA8FF");
                    CapturePreview(shell, dir, "Settings", "Theme-Blue-Settings");
                    CapturePreview(shell, dir, "Home", "Theme-Blue-Active", Double.NaN, 1, false, true);
                    CapturePreview(shell, dir, "Home", "Theme-Blue-Picker", Double.NaN, 1, true);
                    shell.PreviewAccent("#101010");
                    CapturePreview(shell, dir, "Home", "Theme-Dark-Active", Double.NaN, 1, false, true);
                    shell.PreviewAccent(originalAccent);
                    shell.PreviewTray();
                    foreach (int step in new[] { 1, 2, 3, 4 }) { shell.PreviewSetup(step); shell.Window.UpdateLayout(); CaptureCurrent(shell, dir, "Setup-" + step); }
                    shell.PreviewSurface("Guide"); shell.PreviewSurface("Diagnostics"); shell.PreviewSurface("TestSelection"); shell.PreviewSurface("Dns"); shell.PreviewSurface("Updates");
                    shell.PreviewSetupSafety(); CaptureCurrent(shell,dir,"Setup-MissingBundle");
                    shell.PreviewSweepLock(true); CaptureCurrent(shell,dir,"Tests-Locked"); shell.PreviewSweepLock(false);
                    shell.Window.Close(); return 0;
                }
                return app.Run(shell.Window);
            }
            catch (Exception e)
            {
                try { string errors=args.Contains("--preview") || args.Contains("--self-test") ? AppDomain.CurrentDomain.BaseDirectory : ApplicationPaths.DataDirectory; Directory.CreateDirectory(errors); File.WriteAllText(Path.Combine(errors,"error.log"),e.ToString()); } catch { }
                if (!args.Contains("--preview") && !args.Contains("--self-test")) MessageBox.Show(e.Message, "Zapret — ошибка запуска");
                return 1;
            }
            finally { if (instance != null) instance.Dispose(); }
        }
        static void CaptureCurrent(Shell shell, string dir, string name)
        {
            shell.Window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            var visual = (FrameworkElement)shell.Window.Content;
            var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(dir, name + ".png"))) encoder.Save(file);
        }
        static void CapturePreview(Shell shell, string dir, string page, string name, double scrollOffset = Double.NaN, double scale = 1, bool picker = false, bool active = false)
        {
            shell.Navigate(page); shell.Window.UpdateLayout();
            if (!Double.IsNaN(scrollOffset)) {
                var scroll = ((ContentControl)shell.Window.FindName("Page")).Content as ScrollViewer;
                if (Double.IsNegativeInfinity(scrollOffset)) shell.PreviewFilters();
                else if (scroll != null) { if (Double.IsPositiveInfinity(scrollOffset)) scroll.ScrollToBottom(); else scroll.ScrollToVerticalOffset(scrollOffset); shell.Window.UpdateLayout(); }
            }
            shell.Window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            if (picker) { shell.OpenStrategySheet(); shell.Window.UpdateLayout(); }
            if (active) shell.PreviewActiveButton();
            var frame = new DispatcherFrame();
            var animationTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
            animationTimer.Tick += (s, e) => { animationTimer.Stop(); frame.Continue = false; };
            animationTimer.Start(); Dispatcher.PushFrame(frame);
            shell.Window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            if (picker) shell.PreviewStrategyHeaderHitTest();
            var visual = (FrameworkElement)shell.Window.Content;
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * scale), (int)Math.Ceiling(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(Path.Combine(dir, name + ".png"))) encoder.Save(f);
        }
    }
    public sealed partial class Shell
    {
        public Window Window;
        Settings settings;
        List<Strategy> strategies = new List<Strategy>();
        Strategy selected;
        readonly Engine engine = new Engine();
        readonly bool preview;
        readonly string data = ApplicationPaths.DataDirectory;
        readonly List<string> log = new List<string>();
        readonly List<TestResult> results = new List<TestResult>();
        readonly List<Button> actions = new List<Button>();
        CancellationTokenSource operation;
        bool busy;
        Func<bool> leaveEditor;
        string currentPage = "Home";
        string testStatus = "Проверка ещё не запускалась";
        StackPanel logItems;
        TextBlock liveState, liveHint, serviceState, testProgress;
        Button connect, cancelTest;
        CheckBox gameToggle;
        TextBlock gameCaption;
        Grid homeRoot, strategyOverlay;
        Border strategySheet;
        TextBlock pickerTitle;
        bool sheetClosing;
        Border filtersCard;
        StackPanel testResults;
        string pendingTest;
        readonly List<StandardRow> pendingRows = new List<StandardRow>();
        readonly DispatcherTimer timer = new DispatcherTimer();
        static Brush Color(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        static readonly Brush Muted = Color("#A3A3AD");
        Brush Accent { get { return (Brush)Window.Resources["AccentBrush"]; } }
        Brush AccentText { get { return (Brush)Window.Resources["AccentTextBrush"]; } }
        T Find<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }
        string Root { get { return settings.Root; } }
        bool Ready { get { return Bundle.RuntimeProblem(Root)==null; } }
        public Shell(bool isPreview)
        {
            preview = isPreview;
            if (preview) settings = new Settings { Root=EmbeddedBundle.Extract(Path.Combine(data,"preview-bundle")) };
            else try { settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(data, "settings.json"))); } catch { settings = new Settings(); }
            if (settings == null) settings = new Settings();
            if (!String.IsNullOrEmpty(settings.HostsCheckedAt)) hostsNote = "последняя проверка: " + settings.HostsCheckedAt.Substring(0, Math.Min(10, settings.HostsCheckedAt.Length));
            if (!String.IsNullOrEmpty(settings.IpsetCheckedAt)) ipsetNote = "последняя проверка: " + settings.IpsetCheckedAt.Substring(0, Math.Min(10, settings.IpsetCheckedAt.Length));
            settings.AccentColor = AccentTheme.Normalize(settings.AccentColor) ?? AccentTheme.Default;
            if (settings.TestStrategies != null) foreach (string name in settings.TestStrategies) testSelection.Add(name);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shell.xaml"))
            using (var reader = new StreamReader(stream)) Window = (Window)XamlReader.Parse(AccentTheme.Markup(reader.ReadToEnd(), settings.AccentColor));
            WindowTheme.Attach(Window);
            Window.PreviewKeyDown += SweepKey;
            Window.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && strategyOverlay != null) { CloseStrategySheet(); e.Handled = true; } };
            if (String.IsNullOrWhiteSpace(settings.Root)) settings.Root = Bundle.Detect();
            bundleProblem=Bundle.RuntimeProblem(settings.Root);
            engine.Log = AddLog;
            foreach (var page in new[] { "Home", "Tests", "Settings" })
            {
                string p = page;
                Find<Button>("Nav" + p).Click += (s, e) => Navigate(p);
            }
            Find<Button>("AdminButton").Click += (s, e) => Elevate();
            Find<Button>("AdminButton").Content = Engine.IsAdmin ? "✓  Администратор" : "Повысить права ↗";
            Find<Button>("AdminButton").IsEnabled = !Engine.IsAdmin;
            Window.Closing += (s, e) => {
                if (SweepLocked && !sessionEnding) { e.Cancel=true; return; }
                if (!preview && !forceExit && settings.CloseToTray && tray != null) { e.Cancel = true; HideToTray(); return; }
                if (sessionEnding) { engine.Dispose(); timer.Stop(); Save(); return; }
                if (busy) { MessageBox.Show("Сначала отмените проверку или дождитесь завершения операции.", "Zapret"); e.Cancel = true; return; }
                if (leaveEditor != null && !leaveEditor()) { e.Cancel = true; return; }
                engine.Dispose(); timer.Stop(); Save();
            };
            Window.Closed += (s, e) => { windowClosed = true; DisposeTray(); };
            Reload();
            if (!preview && (settings.SetupVersion < 1 || !Ready)) StartSetup(); else Navigate("Home");
            if (!preview) InitializeTray();
            timer.Interval = TimeSpan.FromSeconds(2); timer.Tick += (s, e) => { CheckBundleLocation(); RefreshStatus(); }; if (!preview) timer.Start();
        }
        void Save()
        {
            if (preview) return;
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "settings.json"), new JavaScriptSerializer().Serialize(settings));
        }
        public void ApplyStartup() { if (settings.StartMinimized && !setupVisible) { startInTray = true; Window.WindowState = WindowState.Minimized; } }
        public void RecordStartupNotice(string message) { AddLog(message); }
        public void PreviewAdvanced(bool value) { if (!preview) throw new InvalidOperationException(); settings.AdvancedMode = value; }
        public void PreviewFilters() { if (!preview) throw new InvalidOperationException(); var scroll = Find<ContentControl>("Page").Content as ScrollViewer; if (filtersCard != null && scroll != null) { scroll.ScrollToVerticalOffset(scroll.VerticalOffset + filtersCard.TranslatePoint(new Point(0, 0), scroll).Y); Window.UpdateLayout(); } }
        public string PreviewAccent(string value) { if (!preview) throw new InvalidOperationException(); string previous = settings.AccentColor; ApplyAccent(value); return previous; }
        public void PreviewActiveButton() { if (!preview) throw new InvalidOperationException(); connect.Tag = "Active"; connect.Foreground = AccentText; connect.Content = "Отключить"; liveState.Text = "Предпросмотр активной кнопки"; }
        public void LoadReportPreview()
        {
            var result = new JavaScriptSerializer().Deserialize<TestResult>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-standard.json")));
            results.Add(result); RenderResults(); testStatus = "Локальный тест интерфейса — не результат проверки интернета";
        }
        void Reload()
        {
            strategies = Ready ? Bundle.Strategies(Root) : new List<Strategy>();
            if (settings.TestStrategies!=null) foreach (string name in settings.TestStrategies) testSelection.Add(name);
            testSelection.IntersectWith(strategies.Select(s => s.Name));
            selected = strategies.FirstOrDefault(s => s.Name == settings.Strategy) ?? strategies.FirstOrDefault();
            RefreshVersion();
            AddLog(Ready ? "Подключена сборка " + Bundle.Version(Root) + ". Найдено стратегий: " + strategies.Count : "Выберите папку со сборкой Zapret в настройках.");
        }
        void AddLog(string text)
        {
            if (!Window.Dispatcher.CheckAccess()) { Window.Dispatcher.BeginInvoke(new Action(() => AddLog(text))); return; }
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            log.Add(line); if (log.Count > 1500) log.RemoveAt(0);
            if (logItems != null) { logItems.Children.Insert(0, LogEntry(line)); if (logItems.Children.Count > 300) logItems.Children.RemoveAt(logItems.Children.Count - 1); }
            if (!preview) { try { Directory.CreateDirectory(data); File.AppendAllText(Path.Combine(data, "zapret.log"), line + Environment.NewLine, Encoding.UTF8); } catch { } }
        }
        void SetStatus(string value) { Find<TextBlock>("StatusLine").Text = value; }
        async Task Guard(Func<CancellationToken, Task> action, bool admin)
        {
            if (busy) return;
            if (admin && !Engine.IsAdmin) { Elevate(); return; }
            busy = true; operation = new CancellationTokenSource();
            if (gameToggle != null) gameToggle.IsEnabled = false;
            if (cancelTest != null) cancelTest.IsEnabled = true;
            foreach (var b in actions) b.IsEnabled = false;
            try { await action(operation.Token); SetStatus("Готово"); }
            catch (OperationCanceledException) { AddLog("Операция отменена."); if (testProgress != null) Progress("Проверка остановлена"); SetStatus("Отменено"); }
            catch (Exception e) { AddLog("ОШИБКА: " + e.Message); SetStatus("Ошибка — подробности в журнале"); MessageBox.Show(Window, e.Message, "Zapret", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { operation.Dispose(); operation = null; busy = false; if (cancelTest != null) cancelTest.IsEnabled = false; foreach (var b in actions) b.IsEnabled = true; CheckBundleLocation(); RefreshStatus(); RenderResults(); }
        }
        void Elevate()
        {
            if (busy) return;
            if (leaveEditor != null && !leaveEditor()) return;
            Save();
            try { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory }); Window.Close(); }
            catch (System.ComponentModel.Win32Exception e) { SetStatus(e.NativeErrorCode == 1223 ? "Запрос прав отменён" : e.Message); }
        }
        void Choose(Strategy strategy)
        {
            selected = strategy; settings.Strategy = selected == null ? null : selected.Name; Save();
        }
        public void Navigate(string page)
        {
            if (SweepLocked) return;
            if (setupVisible) { RenderSetup(); return; }
            Find<Border>("BottomNavigation").Visibility = Visibility.Visible;
            ((RowDefinition)Window.FindName("NavigationRow")).Height = new GridLength(64);
            if (!settings.AdvancedMode && (page == "Strategies" || page == "Lists" || page == "Log")) page = "Settings";
            if (leaveEditor != null && !leaveEditor()) return;
            leaveEditor = null;
            currentPage = page; actions.Clear(); logItems = null; liveState = null; liveHint = null; serviceState = null; connect = null; cancelTest = null; testProgress = null; testResults = null;
            gameToggle = null; gameCaption = null; filtersCard = null; installedVersionText = null;
            homeRoot = null; strategyOverlay = null; strategySheet = null; pickerTitle = null; sheetClosing = false;
            bool home = page == "Home";
            ((RowDefinition)Window.FindName("HeaderRow")).Height = new GridLength(home ? 0 : 58);
            ((RowDefinition)Window.FindName("FooterRow")).Height = new GridLength(0);
            Find<Grid>("HeaderBar").Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            Find<StackPanel>("PageHeader").Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("StatusLine").Visibility = Visibility.Collapsed;
            Find<Grid>("PageLayout").Margin = home ? new Thickness(0) : new Thickness(20, 6, 20, 12);
            var titles = new Dictionary<string, string> { { "Home", "Подключение" }, { "Strategies", "Стратегии" }, { "Tests", "Проверка" }, { "Lists", "Списки" }, { "Settings", "Настройки" }, { "Log", "Журнал" } };
            var subtitles = new Dictionary<string, string> { { "Home", "Выбери стратегию и подключись." }, { "Strategies", "Найди вариант, который работает у твоего провайдера." }, { "Tests", "Проверка сети и подбор стратегии." }, { "Lists", "Управляй доменами и исключениями." }, { "Settings", "Подключение и дополнительные настройки." }, { "Log", "Сообщения о работе приложения и ошибках." } };
            Find<TextBlock>("PageTitle").Text = titles[page]; Find<TextBlock>("PageSubtitle").Text = subtitles[page];
            if (page == "Settings" && !settings.AdvancedMode) Find<TextBlock>("PageSubtitle").Text = "Внешний вид, автозапуск и подключение.";
            foreach (string p in new[] { "Home", "Tests", "Settings" })
            {
                bool active = p == page || p == "Settings" && (page == "Strategies" || page == "Lists" || page == "Log");
                var b = Find<Button>("Nav" + p); b.Background = Brushes.Transparent; b.Foreground = active ? Accent : Muted;
            }
            UIElement content = page == "Home" ? Home() : page == "Strategies" ? StrategiesPage() : page == "Tests" ? TestsPage() : page == "Lists" ? ListsPage() : page == "Settings" ? SettingsPage() : LogPage();
            Find<ContentControl>("Page").Content = content;
            if (busy) foreach (var b in actions) b.IsEnabled = false;
            RefreshStatus();
        }
        TextBlock Text(string value, double size, Brush color)
        {
            return new TextBlock { Text = value, FontSize = Math.Max(14, size), Foreground = color ?? Color("#EDF1F4"), TextWrapping = TextWrapping.Wrap };
        }
        TextBlock Label(string value) { var t = Text(value, 11, Muted); t.Margin = new Thickness(0, 0, 0, 12); return t; }
        StackPanel Stack(params UIElement[] children) { var p = new StackPanel(); foreach (var c in children) p.Children.Add(c); return p; }
        Border Card(UIElement content) { return new Border { Style = (Style)Window.FindResource("Card"), Child = content }; }
        Button Button(string label, Action click, bool primary = false, bool action = true)
        {
            var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 10, 8) };
            if (primary) b.Style = (Style)Window.FindResource("Primary");
            b.Click += (s, e) => { if (SweepLocked) return; try { click(); } catch (Exception ex) { AddLog("ОШИБКА: " + ex.Message); MessageBox.Show(Window, ex.Message, "Zapret"); } };
            if (action) actions.Add(b);
            return b;
        }
        WrapPanel Row(params UIElement[] children) { var p = new WrapPanel(); foreach (var c in children) p.Children.Add(c); return p; }
        Grid ButtonGrid(int columns, params Button[] buttons)
        {
            var grid = new Grid();
            for (int column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int row = 0; row < (buttons.Length + columns - 1) / columns; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int index = 0; index < buttons.Length; index++) {
                var button = buttons[index]; int column = index % columns;
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.FontSize = 14; button.Padding = new Thickness(6, 11, 6, 11);
                button.Margin = new Thickness(column == 0 ? 0 : 4, 0, column == columns - 1 ? 0 : 4, index / columns == (buttons.Length - 1) / columns ? 0 : 8);
                Grid.SetColumn(button, column); Grid.SetRow(button, index / columns); grid.Children.Add(button);
            }
            return grid;
        }
        UIElement Scroll(UIElement content) { return new ScrollViewer { Content = content }; }
        void Gap(StackPanel p, double height = 20) { p.Children.Add(new Border { Height = height }); }
        ComboBox StrategyCombo()
        {
            var combo = new ComboBox { ItemsSource = strategies, SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (s, e) => { if (combo.SelectedItem != null) Choose((Strategy)combo.SelectedItem); };
            return combo;
        }
        void NeedBundle() { string problem=Bundle.RuntimeProblem(Root); if (problem!=null) throw new InvalidOperationException(problem); }
        async Task ToggleConnection(CancellationToken token)
        {
            NeedBundle();
            string svc = Engine.ServiceState();
            if (engine.Running) { engine.Stop(); return; }
            if (svc != "NotInstalled") { await Engine.ServiceAction(Root, svc == "Running" ? "stop" : "start", token); return; }
            if (selected == null) throw new InvalidOperationException("Стратегия не выбрана.");
            await engine.Start(selected, Root, token);
        }
        UIElement Home()
        {
            var content = new Grid();
            homeRoot = content;
            content.RowDefinitions.Add(new RowDefinition()); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 18, 20, 12) };
            content.Children.Add(center);
            connect = Button("Подключить", () => { var ignored = Guard(ToggleConnection, true); });
            connect.Width = 212; connect.Height = 212; connect.Margin = new Thickness(0); connect.FontSize = 20; connect.FontWeight = FontWeights.SemiBold; connect.HorizontalAlignment = HorizontalAlignment.Center;
            connect.Template = (ControlTemplate)XamlReader.Parse(AccentTheme.Markup(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
                <Grid><Ellipse x:Name='Ring' Stroke='#92949C' StrokeThickness='3' Fill='#101013'><Ellipse.Effect><DropShadowEffect Color='#92949C' BlurRadius='17' ShadowDepth='0' Opacity='0.26'/></Ellipse.Effect></Ellipse><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property='Tag' Value='Active'><Setter Property='Foreground' Value='#FFB667'/><Setter TargetName='Ring' Property='Stroke' Value='#FFAD58'/><Setter TargetName='Ring' Property='Effect'><Setter.Value><DropShadowEffect Color='#FFB46C' BlurRadius='17' ShadowDepth='0' Opacity='0.26'/></Setter.Value></Setter></Trigger>
                    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Ring' Property='Stroke' Value='#B4B6BE'/><Setter TargetName='Ring' Property='Fill' Value='#151518'/></Trigger>
                    <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='Active'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Ring' Property='Stroke' Value='#FFBD78'/><Setter TargetName='Ring' Property='Fill' Value='#211A15'/></MultiTrigger>
                    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate>", settings.AccentColor));
            center.Children.Add(connect);
            Gap(center, 18); liveState = Text("", 14, Muted); liveState.TextAlignment = TextAlignment.Center; center.Children.Add(liveState);
            var gameRow = new Grid { Margin = new Thickness(28, 8, 20, 20) };
            gameRow.ColumnDefinitions.Add(new ColumnDefinition()); gameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            gameCaption = Text("Game Filter", 15, Muted); gameCaption.VerticalAlignment = VerticalAlignment.Center; gameRow.Children.Add(gameCaption);
            gameToggle = new CheckBox { IsChecked = Ready && GameFilter.Read(Root).Mode != "disabled", Margin = new Thickness(0) };
            gameToggle.Click += async (s, e) => {
                bool enabled = gameToggle.IsChecked == true;
                await Guard(t => SetGameFilter(enabled, t), Engine.ServiceState() != "NotInstalled"); RefreshStatus();
            };
            Grid.SetColumn(gameToggle, 1); gameRow.Children.Add(gameToggle); Grid.SetRow(gameRow, 1); content.Children.Add(gameRow);
            var bottom = new StackPanel();
            var handle = new Border { Width = 30, Height = 3, CornerRadius = new CornerRadius(2), Background = Color("#3B3C42"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 15) }; bottom.Children.Add(handle);
            pickerTitle = Text(selected == null ? "Выбрать стратегию" : selected.Name, 21, null); pickerTitle.TextAlignment = TextAlignment.Center; pickerTitle.FontWeight = FontWeights.Medium;
            var selectorContent = new Grid(); selectorContent.ColumnDefinitions.Add(new ColumnDefinition()); selectorContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); selectorContent.Children.Add(pickerTitle);
            var arrow = Text("⌄", 20, Muted); arrow.VerticalAlignment = VerticalAlignment.Center; arrow.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(arrow, 1); selectorContent.Children.Add(arrow);
            var selector = Button("", OpenStrategySheet, false, false); selector.Content = selectorContent; selector.HorizontalAlignment = HorizontalAlignment.Stretch; selector.HorizontalContentAlignment = HorizontalAlignment.Stretch; selector.Background = Brushes.Transparent; selector.Padding = new Thickness(0, 7, 0, 7); selector.Margin = new Thickness(0); bottom.Children.Add(selector); Gap(bottom, 14);
            var auto = Button("Автоподбор стратегии  →", () => Navigate("Tests"), true, false); auto.HorizontalAlignment = HorizontalAlignment.Stretch; auto.Margin = new Thickness(0); bottom.Children.Add(auto);
            var panel = new Border { Background = Color("#1B1C20"), BorderBrush = Color("#2A2B30"), BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(16, 16, 0, 0), Padding = new Thickness(22, 12, 22, 18), Child = bottom };
            AttachSheetGesture(panel, true, auto);
            Grid.SetRow(panel, 2); content.Children.Add(panel);
            return content;
        }
        public void OpenStrategySheet()
        {
            if (homeRoot == null || strategyOverlay != null) return;
            var overlay = new Grid { ClipToBounds = true };
            var scrim = new Border { Background = Brushes.Black, Opacity = 0 }; scrim.MouseLeftButtonDown += (s, e) => CloseStrategySheet(); overlay.Children.Add(scrim);
            var body = new DockPanel();
            var header = new StackPanel();
            // A transparent background makes padding and gaps hit-testable too.
            // StackPanel margins alone leave those areas outside the gesture route.
            var headerSurface = new Border { Background=Brushes.Transparent,Padding=new Thickness(20,12,20,0),Child=header,Cursor=Cursors.Hand };
            var handle = new Border { Width = 30, Height = 3, CornerRadius = new CornerRadius(2), Background = Color("#3B3C42"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20), Cursor = Cursors.Hand }; header.Children.Add(handle);
            var title = Text(selected == null ? "Выбор стратегии" : selected.Name, 24, null); title.TextAlignment = TextAlignment.Center; title.FontWeight = FontWeights.SemiBold; header.Children.Add(title);
            var subtitle = Text(selected == null ? "" : selected.Methods, 14, Muted); subtitle.TextAlignment = TextAlignment.Center; subtitle.Margin = new Thickness(0, 10, 0, 24); header.Children.Add(subtitle);
            header.Children.Add(Text("Стратегии", 21, null)); Gap(header, 12);
            var search = new TextBox { ToolTip = "Поиск стратегии", Tag = "Поиск стратегии" };
            var searchBox = new Grid { Margin = new Thickness(0, 0, 0, 10) }; searchBox.Children.Add(search); header.Children.Add(searchBox);
            AttachSheetGesture(headerSurface, false, searchBox);
            DockPanel.SetDock(headerSurface, Dock.Top); body.Children.Add(headerSurface);
            var list = new StackPanel { Margin = new Thickness(8, 0, 8, 10) };
            Action renderStrategyRows = delegate { };
            Action render = () => {
                list.Children.Clear();
                foreach (var strategy in strategies.Where(st => st.Name.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)) {
                    var item = strategy; bool active = selected != null && selected.Name == item.Name;
                    var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                    var radio = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, UseLayoutRounding = false, SnapsToDevicePixels = false };
                    radio.Children.Add(new System.Windows.Shapes.Ellipse { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Stroke = active ? Accent : Color("#686A73"), StrokeThickness = 1 });
                    if (active) radio.Children.Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Fill = Accent }); row.Children.Add(radio);
                    var name = Text(item.Name, 17, active ? Accent : Color("#E0E0E7"));
                    var description = Text(item.Methods, 14, Muted); description.Margin = new Thickness(0, 5, 0, 0); var labels = Stack(name, description); Grid.SetColumn(labels, 1); row.Children.Add(labels);
                    var button = Button("", () => { Choose(item); if (pickerTitle != null) pickerTitle.Text = item.Name; title.Text = item.Name; subtitle.Text = item.Methods; renderStrategyRows(); }, false, false);
                    button.Content = row; button.Background = Brushes.Transparent; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(14, 14, 8, 14); button.Margin = new Thickness(0);
                    list.Children.Add(new Border { Child = button, BorderBrush = Color("#2A2B30"), BorderThickness = new Thickness(0, 0, 0, 1) });
                }
                if (list.Children.Count == 0) list.Children.Add(Text("Стратегии не найдены", 14, Muted));
            };
            renderStrategyRows = render;
            search.TextChanged += (s, e) => render(); render();
            var listScroll = (ScrollViewer)Scroll(list);
            MouseScrolling.Sheet(listScroll, SetSheetOffset, (distance, elapsed) => { if (GestureMath.Commit(distance, elapsed)) CloseStrategySheet(); else SnapSheetOpen(); });
            body.Children.Add(listScroll);
            double height = Math.Max(400, homeRoot.ActualHeight - 40);
            var shift = new TranslateTransform(0, height);
            strategySheet = new Border { Background = Color("#1B1C20"), BorderBrush = Color("#2A2B30"), BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(16, 16, 0, 0), Height = height, VerticalAlignment = VerticalAlignment.Bottom, Child = body, RenderTransform = shift };
            overlay.Children.Add(strategySheet); Grid.SetRowSpan(overlay, 3); Panel.SetZIndex(overlay, 20); strategyOverlay = overlay; homeRoot.Children.Add(overlay);
            scrim.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.45, TimeSpan.FromMilliseconds(240)));
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(height, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        public void PreviewStrategyHeaderHitTest()
        {
            if (!preview || strategySheet==null) throw new InvalidOperationException("Strategy picker preview is not open.");
            var surface=(Border)((DockPanel)strategySheet.Child).Children[0];
            var header=(StackPanel)surface.Child;
            var search=(Grid)header.Children[header.Children.Count-1];
            foreach (var point in new[] { new Point(surface.ActualWidth/2,2), new Point(2,surface.ActualHeight/2), new Point(surface.ActualWidth-2,surface.ActualHeight/2), new Point(surface.ActualWidth/2,24) }) {
                var hit=Window.InputHitTest(surface.TranslatePoint(point,Window)) as DependencyObject;
                if (hit==null || hit!=surface && !surface.IsAncestorOf(hit)) throw new InvalidOperationException("Strategy header has a non-interactive gap.");
            }
            var searchHit=Window.InputHitTest(search.TranslatePoint(new Point(search.ActualWidth/2,search.ActualHeight/2),Window)) as DependencyObject;
            if (searchHit==null || searchHit!=search && !search.IsAncestorOf(searchHit)) throw new InvalidOperationException("Strategy search is not accessible.");
        }
        void CloseStrategySheet()
        {
            if (strategyOverlay == null || strategySheet == null || sheetClosing) return;
            sheetClosing = true;
            var overlay = strategyOverlay; var root = homeRoot; var sheet = strategySheet;
            var animation = new DoubleAnimation(sheet.ActualHeight, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            animation.Completed += (s, e) => { root.Children.Remove(overlay); if (strategyOverlay == overlay) { strategyOverlay = null; strategySheet = null; sheetClosing = false; } };
            ((TranslateTransform)sheet.RenderTransform).BeginAnimation(TranslateTransform.YProperty, animation);
            overlay.Children[0].BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
        }
        void SetSheetOffset(double offset)
        {
            if (strategySheet == null || strategyOverlay == null) return;
            offset = Math.Max(0, Math.Min(strategySheet.Height, offset));
            var shift = (TranslateTransform)strategySheet.RenderTransform;
            shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = offset;
            var scrim = strategyOverlay.Children[0]; scrim.BeginAnimation(UIElement.OpacityProperty, null); scrim.Opacity = 0.45 * (1 - offset / strategySheet.Height);
        }
        void SnapSheetOpen()
        {
            if (strategySheet == null || strategyOverlay == null || sheetClosing) return;
            ((TranslateTransform)strategySheet.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            strategyOverlay.Children[0].BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.45, TimeSpan.FromMilliseconds(220)));
        }
        void AttachSheetGesture(FrameworkElement surface, bool opening, FrameworkElement excluded)
        {
            bool pressed = false, dragging = false;
            Point origin = new Point(); double startOffset = 0; var watch = new Stopwatch();
            surface.PreviewMouseLeftButtonDown += (s, e) => {
                var source = e.OriginalSource as DependencyObject;
                if (sheetClosing || source != null && excluded != null && (source == excluded || excluded.IsAncestorOf(source))) return;
                pressed = true; dragging = false; origin = e.GetPosition(Window); watch.Restart();
                if (!surface.CaptureMouse()) { pressed=false; return; }
                e.Handled = true;
            };
            surface.PreviewMouseMove += (s, e) => {
                if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                Point point = e.GetPosition(Window); double dy = point.Y - origin.Y;
                if (!dragging) {
                    if (Math.Abs(dy) < 6 || Math.Abs(dy) < Math.Abs(point.X - origin.X) || (opening ? dy > 0 : dy < 0)) return;
                    if (opening) { OpenStrategySheet(); if (strategySheet == null) return; startOffset = Math.Max(0, strategySheet.Height - surface.ActualHeight); }
                    else { if (strategySheet == null) return; startOffset = ((TranslateTransform)strategySheet.RenderTransform).Y; }
                    dragging = true;
                }
                SetSheetOffset(startOffset + dy); e.Handled = true;
            };
            surface.PreviewMouseLeftButtonUp += (s, e) => {
                if (!pressed) return;
                double dy = e.GetPosition(Window).Y - origin.Y;
                pressed = false; surface.ReleaseMouseCapture(); e.Handled = true;
                if (!dragging) { if (opening) OpenStrategySheet(); else CloseStrategySheet(); return; }
                double travel = opening ? -dy : dy;
                bool commit = GestureMath.Commit(travel, watch.ElapsedMilliseconds);
                if (opening ? commit : !commit) SnapSheetOpen(); else CloseStrategySheet();
                dragging = false;
            };
            surface.LostMouseCapture += (s, e) => {
                if (!pressed || e.OriginalSource!=surface || Mouse.Captured==surface) return;
                pressed = false;
                if (dragging) { if (opening) CloseStrategySheet(); else SnapSheetOpen(); }
                dragging = false;
            };
        }
        async Task SetGameFilter(bool enabled, CancellationToken token)
        {
            NeedBundle();
            if (engine.Running || Engine.AnyWinws() || Engine.ServiceState() != "NotInstalled" && Engine.ServiceState() != "Stopped") throw new InvalidOperationException("Отключите Zapret перед изменением Game Filter.");
            bool service = Engine.ServiceState() == "Stopped";
            Strategy installed = null;
            if (service) {
                if (!Engine.ServiceMatches(Root)) throw new InvalidOperationException("Служба zapret относится к другой сборке.");
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret")) {
                    string name = Convert.ToString(key.GetValue("zapret-discord-youtube")); installed = strategies.FirstOrDefault(s => s.Name == name);
                }
                if (installed == null) throw new InvalidOperationException("Не удалось определить стратегию службы. Переустановите её в продвинутых настройках.");
            }
            string path = Path.Combine(Root, "utils", "game_filter.enabled"); string previous = File.Exists(path) ? File.ReadAllText(path) : null;
            var filter = GameFilter.Read(Root); filter.Mode = enabled ? "all" : "disabled";
            // Validate service arguments before changing any configuration.
            string args = service ? Bundle.Arguments(installed, Root, filter) : null;
            token.ThrowIfCancellationRequested(); filter.Save(Root);
            try {
                if (service) await Engine.RunTool("sc.exe", new[] { "config", "zapret", "binPath=", Engine.Quote(Path.Combine(Root, "bin", "winws.exe")) + " " + args }, CancellationToken.None);
            }
            catch { if (previous == null) File.Delete(path); else Bundle.WriteBackup(path, previous); throw; }
            AddLog("Game Filter: " + (enabled ? "TCP + UDP" : "выключен") + ". Применится при следующем запуске.");
        }
        void RefreshStatus()
        {
            RefreshVersion();
            string svc = Engine.ServiceState(); bool running = engine.Running || svc == "Running";
            bool external = !engine.Running && svc != "Running" && Engine.AnyWinws();
            if (liveState != null) liveState.Text = running ? "Zapret работает" : external ? "Zapret запущен другой программой" : "Готов к запуску";
            if (liveHint != null) liveHint.Text = !Ready ? "Выбери папку Flowseal или используй встроенный набор." : engine.Running ? "При полном выходе через трей это подключение остановится." : svc == "Running" ? "Служба продолжит работу после закрытия приложения." : external ? "Отключи Zapret в другой программе, которая его запустила." : "Выбери стратегию и нажми «Подключить».";
            if (connect != null) { connect.Tag = running ? "Active" : "Idle"; connect.Foreground = running ? AccentText : Color("#C1C2CA"); connect.Content = running ? "Отключить" : "Подключить"; connect.IsEnabled = !busy && Ready && !external; }
            if (serviceState != null) serviceState.Text = "Статус: " + ServiceLabel(svc);
            if (gameToggle != null) {
                string mode = Ready ? GameFilter.Read(Root).Mode : "disabled";
                gameToggle.IsChecked = mode != "disabled";
                gameToggle.IsEnabled = Ready && !busy && !running && !external && (svc == "NotInstalled" || svc == "Stopped");
                gameToggle.ToolTip = running || external ? "Отключите Zapret, чтобы изменить фильтр" : "Обрабатывать игровые подключения. Менять этот режим можно после отключения Zapret.";
                gameCaption.Text = "Игровой режим · " + (mode == "disabled" ? "выключен" : mode == "all" ? "TCP + UDP" : mode.ToUpperInvariant());
            }
        }
        static string ServiceLabel(string state) { return state == "NotInstalled" ? "не установлена" : state == "Running" ? "работает" : state == "Stopped" ? "остановлена" : state; }
        UIElement StrategiesPage()
        {
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var list = new ListBox { ItemsSource = strategies, SelectedItem = selected, Height = 170 };
            var search = new TextBox { ToolTip = "Поиск по названию", Tag = "Поиск стратегии", Margin = new Thickness(0, 0, 0, 12) };
            var left = new DockPanel { Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(search, Dock.Top); left.Children.Add(search); left.Children.Add(list); grid.Children.Add(left);
            search.TextChanged += (s, e) => { list.ItemsSource = strategies.Where(v => v.Name.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList(); };
            var detail = new StackPanel(); var card = Card(detail); Grid.SetRow(card, 1); grid.Children.Add(card);
            Action show = () => {
                detail.Children.Clear();
                var st = list.SelectedItem as Strategy; if (st == null) { detail.Children.Add(Text("Выбери стратегию слева", 18, Muted)); return; }
                Choose(st); detail.Children.Add(Label("СТРАТЕГИЯ")); detail.Children.Add(Text(st.Name, 25, null)); Gap(detail);
                detail.Children.Add(Label("МЕТОДЫ DPI DESYNC")); detail.Children.Add(Text(st.Methods, 15, Accent)); Gap(detail);
                detail.Children.Add(Text(st.Profiles + " профилей фильтрации · TCP / UDP", 13, Muted)); Gap(detail);
                detail.Children.Add(Button("Выбрать для запуска", () => { Choose(st); Navigate("Home"); }, true)); Gap(detail);
                detail.Children.Add(Button("Проверить эту стратегию", () => { var ignored = Guard(t => Sweep(new[] { st }.ToList(), t), true); })); Gap(detail);
                detail.Children.Add(Button("Посмотреть аргументы", () => ShowArguments(st), false, false)); Gap(detail);
                detail.Children.Add(Text("Параметры читаются из исходного .bat. Файлы стратегии остаются в сборке Flowseal.", 12, Muted));
            };
            list.SelectionChanged += (s, e) => show(); show(); return Scroll(grid);
        }
        void ShowArguments(Strategy strategy)
        {
            Surface("Параметры стратегии", ArgumentsCards(strategy), null).ShowDialog();
        }
        UIElement TestsPage()
        {
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var top = Stack(Text("Автоподбор стратегии", 18, null));
            var desc = Text("Проверим варианты и выберем тот, который открывает больше нужных сайтов.", 12, Muted); desc.Margin = new Thickness(0, 8, 0, 14); top.Children.Add(desc);
            var scope = new Grid { Margin = new Thickness(0, 0, 0, 12) }; scope.ColumnDefinitions.Add(new ColumnDefinition()); scope.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mode = new ComboBox { Margin = new Thickness(0, 0, 8, 0) }; bool loadingScope = false;
            Action refreshScope = () => { loadingScope = true; mode.ItemsSource = new[] { "Все (" + strategies.Count + ")", "Выбранные (" + testSelection.Count + ")" }; mode.SelectedIndex = settings.TestSelectedOnly ? 1 : 0; loadingScope = false; };
            refreshScope(); mode.SelectionChanged += (s, e) => { if (!loadingScope && mode.SelectedIndex >= 0) { settings.TestSelectedOnly = mode.SelectedIndex == 1; Save(); } }; scope.Children.Add(mode);
            var choose = Button("Выбрать", () => ChooseTestStrategies(refreshScope)); choose.Padding = new Thickness(10, 10, 10, 10); choose.Margin = new Thickness(0); Grid.SetColumn(choose, 1); scope.Children.Add(choose); top.Children.Add(scope);
            var controls = new Grid();
            for (int i = 0; i < 3; i++) controls.ColumnDefinitions.Add(new ColumnDefinition());
            var network = Button("Проверить", () => { var ignored = Guard(CurrentTest, false); }); network.ToolTip = "Проверить сайты с текущим подключением";
            var sweep = Button("Подобрать", () => { var candidates = Bundle.TestSubset(strategies, testSelection, settings.TestSelectedOnly); if (candidates.Count == 0) throw new InvalidOperationException("Отметь хотя бы одну стратегию кнопкой «Выбрать»."); var ignored = Guard(t => Sweep(candidates, t), true); }, true); sweep.ToolTip = "По очереди проверить все или выбранные варианты подключения";
            cancelTest = Button("Отмена", () => { if (operation != null) operation.Cancel(); }, false, false); cancelTest.IsEnabled = busy;
            var testButtons = new[] { network, sweep, cancelTest };
            for (int i = 0; i < testButtons.Length; i++) {
                var button = testButtons[i]; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(6, 12, 6, 12); button.FontSize = 14; button.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0); Grid.SetColumn(button, i); controls.Children.Add(button);
            }
            top.Children.Add(controls);
            testProgress = Text(testStatus, 12, Muted); testProgress.Margin = new Thickness(0, 18, 0, 0); top.Children.Add(testProgress);
            var card = Card(top); card.Padding = new Thickness(16); card.Margin = new Thickness(0, 0, 0, 14); grid.Children.Add(card);
            var lower = new DockPanel(); Grid.SetRow(lower, 1); grid.Children.Add(lower);
            if (settings.AdvancedMode) {
                var native = Button("DPI Flowseal ↗", NativeTests); native.ToolTip = "Оригинальный DPI-тест Flowseal";
                var tools = ButtonGrid(2, Button("Сохранить отчёт", ExportResults), native); tools.Margin = new Thickness(0, 0, 0, 12); DockPanel.SetDock(tools, Dock.Top); lower.Children.Add(tools);
            }
            testResults = new StackPanel(); lower.Children.Add(Scroll(testResults)); PaintTranscript(); return grid;
        }
        void Progress(string value) { testStatus = value; if (testProgress != null) testProgress.Text = value; if (sweepOverlayProgress!=null && !sweepLock.Cancelling) sweepOverlayProgress.Text=value; SetStatus(value); }
        void RenderResults()
        {
            pendingTest = null; pendingRows.Clear();
            PaintTranscript();
        }
        void BeginTest(string name)
        {
            pendingTest = name; pendingRows.Clear(); PaintTranscript();
        }
        void TestRow(StandardRow row)
        {
            if (!Window.Dispatcher.CheckAccess()) { Window.Dispatcher.BeginInvoke(new Action(() => TestRow(row))); return; }
            pendingRows.Add(row); PaintTranscript();
        }
        void PaintTranscript()
        {
            if (testResults == null) return;
            testResults.Children.Clear();
            if (results.Count == 0 && pendingTest == null) {
                var empty = Stack(Text("Здесь будут результаты", 17, null), new Border { Height = 8 }, Text("Начни с проверки сети или запусти подбор стратегии.", 14, Muted));
                empty.Margin = new Thickness(4, 20, 4, 0); testResults.Children.Add(empty); return;
            }
            if (pendingTest != null) {
                var live = Stack(Text("Проверяем " + pendingTest, 16, null), new Border { Height = 6 }, Text("Проверено адресов: " + pendingRows.Count, 14, Muted)); live.Margin = new Thickness(0, 6, 0, 18); testResults.Children.Add(live);
            }
            if (!busy && results.Count > 1) {
                var best = StandardReport.Best(results.Where(r => r.Strategy != "Без Zapret"));
                if (best != null) { var note = Text("Лучший результат: " + best.Strategy, 15, Accent); note.Margin = new Thickness(0, 4, 0, 16); testResults.Children.Add(note); }
            }
            foreach (var result in results.AsEnumerable().Reverse()) {
                var title = Text(result.Strategy, 16, null); title.FontWeight = FontWeights.Medium;
                string summary = result.Total == 0 ? "Не удалось запустить" : "Успешно: " + result.Passed + " из " + result.Total;
                var caption = Text(summary, 14, Muted); caption.Margin = new Thickness(0, 5, 0, 0);
                var details = new StackPanel();
                if (result.Rows != null) foreach (var row in result.Rows) {
                    int ok = row.Checks.Count(c => c.Status == "OK");
                    string status = row.Checks.Count == 0 ? (row.Ping == "Timeout" ? "Нет ответа на Ping" : "Задержка: " + row.Ping) : ok == row.Checks.Count ? "Соединение установлено" : ok > 0 ? "Часть проверок прошла" : "Не удалось проверить";
                    var info = Text(status, 14, ok > 0 ? Color("#91BD9E") : Muted); info.Margin = new Thickness(0, 5, 0, 0);
                    var lines = Stack(Text(FriendlyTarget(row.Name), 15, null), info);
                    if (settings.AdvancedMode && row.Checks.Count > 0) { var protocols = Text(String.Join(" · ", row.Checks.Select(c => c.Protocol + ": " + FriendlyStatus(c.Status))), 14, Muted); protocols.Margin = new Thickness(0, 6, 0, 0); lines.Children.Add(protocols); }
                    details.Children.Add(new Border { Child = lines, BorderBrush = Color("#25252B"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 12) });
                }
                var expander = new Expander { Header = Stack(title, caption), Content = details, Foreground = Color("#E0E0E7"), IsExpanded = results.Count == 1 && pendingTest == null, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var card = new Border { Background = Color("#121215"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = expander }; testResults.Children.Add(card);
            }
        }
        static string FriendlyStatus(string status) { return status == "OK" ? "есть ответ" : status == "UNSUP" ? "не поддерживается" : status == "SSL" ? "ошибка DNS / TLS" : "нет ответа"; }
        internal static string FriendlyTarget(string name)
        {
            if (String.IsNullOrEmpty(name)) return "";
            foreach (string brand in new[] { "YouTube", "Cloudflare", "Discord", "Google", "GitHub", "TikTok", "OpenAI" })
                if (name.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) return Regex.Replace((brand + " " + Regex.Replace(name.Substring(brand.Length), "([a-z])([A-Z])", "$1 $2").Replace("_", " ")).Trim(), @"\s+", " ");
            return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").Replace("_", " ").Trim();
        }
        async Task CurrentTest(CancellationToken token)
        {
            NeedBundle(); Progress("Проверяем текущую сеть…");
            var targets = Bundle.Targets(Root);
            BeginTest("текущую сеть");
            var result = await StandardTest.Run(engine.Running ? engine.Active.Name : "Текущая сеть", targets, token, TestRow);
            results.Add(result); SaveResults(); RenderResults(); Progress("Проверка завершена"); AddLog("Проверка сети: " + result.Summary);
        }
        async Task Sweep(List<Strategy> candidates, CancellationToken token)
        {
            NeedBundle(); Navigate("Tests");
            try { LockSweep(); await SweepCore(candidates,token); }
            finally { if (SweepLocked) UnlockSweep(); }
        }
        async Task SweepCore(List<Strategy> candidates, CancellationToken token)
        {
            NeedBundle();
            if (candidates.Count == 0) throw new InvalidOperationException("Стратегии не найдены.");
            if (Engine.ServiceState() != "NotInstalled") throw new InvalidOperationException("Сейчас Zapret настроен как служба Windows. Перед подбором удали её: Настройки → Продвинутый режим → Фоновая служба → Удалить. Если служба настроена другой программой, удали её через ту программу.");
            if (!engine.Running && Engine.AnyWinws()) throw new InvalidOperationException("Zapret уже запущен другой программой. Отключи его там и повтори подбор.");
            var targets = Bundle.Targets(Root);
            var previous = engine.Running ? engine.Active : null;
            Navigate("Tests"); results.Clear(); RenderResults();
            Exception failure = null;
            try
            {
                engine.Stop(); await Task.Delay(500, token); Progress("Исходная сеть: замер без Zapret…");
                BeginTest("сеть без Zapret");
                results.Add(await StandardTest.Run("Без Zapret", targets, token, TestRow)); RenderResults(); SaveResults();
                for (int i = 0; i < candidates.Count; i++)
                {
                    NeedBundle();
                    token.ThrowIfCancellationRequested(); var strategy = candidates[i];
                    Progress((i + 1) + "/" + candidates.Count + " · " + strategy.Name); AddLog("Проверка " + strategy.Name);
                    BeginTest(strategy.Name);
                    try
                    {
                        await engine.Start(strategy, Root, token);
                        var result = await StandardTest.Run(strategy.Name, targets, token, TestRow);
                        if (!engine.Running) {
                            foreach (var row in result.Rows) foreach (var check in row.Checks) { check.Status = "ERROR"; check.Detail = "winws завершился во время теста"; }
                            result = StandardTest.Summarize(strategy.Name, result.Rows);
                            result.Summary = "winws завершился во время теста";
                        }
                        results.Add(result);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { AddLog("Стратегия пропущена: " + ex.Message); results.Add(new TestResult { Strategy = strategy.Name, Summary = "Ошибка запуска", Probes = new List<Probe>() }); }
                    finally { engine.Stop(); }
                    RenderResults(); SaveResults(); await Task.Delay(400, token);
                }
                var best = StandardReport.Best(results.Skip(1));
                if (best == null) Progress("Подходящих стратегий не найдено. Нажми на результат, чтобы увидеть подробности.");
                else
                {
                    Choose(strategies.First(s => s.Name == best.Strategy));
                    int baseline = results[0].Passed;
                    Progress("Выбрана " + best.Strategy + ": " + best.Passed + "/" + best.Total + ". Без обхода: " + baseline + "/" + results[0].Total + "." + (best.Passed <= baseline ? " Результат не лучше, чем без Zapret." : ""));
                }
            }
            catch (OperationCanceledException ex) { Progress("Подбор остановлен. Уже полученные результаты сохранены."); failure = ex; }
            catch (Exception ex) { failure = ex; }
            finally
            {
                engine.Stop(); try { SaveResults(); } catch (Exception error) { AddLog("Не удалось сохранить отчёт: "+error.Message); if (failure==null) failure=error; }
            }
                if (previous != null && !exitPending && !sessionEnding && Ready)
            {
                try { await engine.Start(previous, Root, CancellationToken.None); AddLog("Восстановлена прежняя стратегия: " + previous.Name); }
                catch (Exception e) { AddLog("Не удалось восстановить прежнюю стратегию: " + e.Message); failure=new IOException("Проверка закончена, но прежнее подключение не удалось вернуть. Попробуй включить его вручную.\n"+e.Message,e); }
            }
            if (failure != null) throw failure;
        }
        void SaveResults()
        {
            if (preview) return; Directory.CreateDirectory(data);
            Bundle.WriteBackup(Path.Combine(data, "last-results.json"), new JavaScriptSerializer().Serialize(new { Date = DateTimeOffset.Now.ToString("o"), Root = Root, Results = results }));
            File.WriteAllText(Path.Combine(data, "last-results.txt"), StandardReport.Full(results), Encoding.UTF8);
        }
        void ExportResults()
        {
            if (results.Count == 0) throw new InvalidOperationException("Сначала запустите проверку.");
            var dialog = new SaveFileDialog { Filter = "Текст как в Flowseal|*.txt|JSON с подробностями|*.json", FileName = "zapret-results-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt" };
            if (dialog.ShowDialog(Window) == true) { File.WriteAllText(dialog.FileName, dialog.FilterIndex == 1 ? StandardReport.Full(results) : new JavaScriptSerializer().Serialize(results), Encoding.UTF8); SetStatus("Результаты сохранены"); }
        }
        void NativeTests()
        {
            NeedBundle(); if (engine.Running || Engine.AnyWinws() || Engine.ServiceState() != "NotInstalled") throw new InvalidOperationException("Перед оригинальными тестами остановите winws и удалите службу.");
            string script = Path.Combine(Root, "utils", "test zapret.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("Тесты Flowseal не найдены.");
            if (MessageBox.Show(Window, "Откроются оригинальные интерактивные тесты Flowseal с правами администратора. Они временно меняют IPSet и запускают стратегии. Не запускайте одновременно проверку в интерфейсе.", "Тесты Flowseal", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Engine.Quote(script)) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Root });
        }
        void ShowReport(string title, string report)
        {
            StyledReport(title, report);
        }
        UIElement ListsPage()
        {
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var options = new Dictionary<string, string> { { "Домены пользователя", "lists/list-general-user.txt" }, { "Исключения доменов", "lists/list-exclude-user.txt" }, { "Исключения IP / CIDR", "lists/ipset-exclude-user.txt" }, { "Цели проверки", "utils/targets.txt" } };
            var combo = new ComboBox { ItemsSource = options.Keys, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 16) }; grid.Children.Add(combo);
            var editor = new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, FontFamily = new FontFamily("Consolas"), FontSize = 14 }; Grid.SetRow(editor, 1); grid.Children.Add(editor);
            bool dirty = false, loading = false; string loaded = null;
            Action load = () => { loading = true; loaded = Ready ? Path.Combine(Root, options[(string)combo.SelectedItem].Replace('/', Path.DirectorySeparatorChar)) : null; editor.Text = loaded != null && File.Exists(loaded) ? File.ReadAllText(loaded) : ""; loading = false; dirty = false; };
            editor.TextChanged += (s, e) => { if (!loading) dirty = true; };
            Action saveEditor = () => {
                NeedBundle(); EnsureStopped();
                string value = editor.Text;
                if (String.IsNullOrWhiteSpace(value)) value = loaded.EndsWith("targets.txt") ? "" : loaded.Contains("ipset-") ? "203.0.113.113/32\r\n" : "# User list\r\ndomain.example.abc\r\n";
                Bundle.WriteBackup(loaded, value); dirty = false; AddLog("Сохранён " + Path.GetFileName(loaded)); SetStatus("Сохранено. Предыдущая версия: .ui-backup");
            };
            leaveEditor = () => {
                if (!dirty) return true;
                var choice = MessageBox.Show(Window, "Сохранить изменения списка?", "Zapret", MessageBoxButton.YesNoCancel);
                if (choice == MessageBoxResult.Cancel) return false;
                if (choice == MessageBoxResult.Yes)
                {
                    try { saveEditor(); } catch (Exception e) { MessageBox.Show(Window, e.Message, "Zapret"); return false; }
                }
                dirty = false; return true;
            };
            bool reverting = false; int previousIndex = combo.SelectedIndex;
            combo.SelectionChanged += (s, e) => {
                if (reverting) return;
                if (!leaveEditor()) { reverting = true; combo.SelectedIndex = previousIndex; reverting = false; return; }
                previousIndex = combo.SelectedIndex;
                load();
            }; load();
            var bottom = Stack(); Gap(bottom, 14); bottom.Children.Add(Row(Button("Сохранить", saveEditor, true), Button("Открыть папку", () => { NeedBundle(); Process.Start("explorer.exe", Engine.Quote(Path.Combine(Root, "lists"))); }, false, false)));
            Gap(bottom, 12); bottom.Children.Add(Text("Одна запись на строку. Перед сохранением создаётся .ui-backup. Изменения действуют после перезапуска Zapret.", 12, Muted)); Grid.SetRow(bottom, 2); grid.Children.Add(bottom); return grid;
        }
        UIElement SettingsPage()
        {
            var content = new StackPanel();
            content.Children.Add(VersionsCard()); Gap(content, 14);
            content.Children.Add(Wide("Диагностика подключения", () => { var ignored = Guard(Diagnostics, false); }));
            var closeInTray = new CheckBox { IsChecked = settings.CloseToTray };
            closeInTray.Click += (s, e) => { settings.CloseToTray = closeInTray.IsChecked == true; Save(); };
            var applicationCard = Card(Stack(Label("ПРИЛОЖЕНИЕ"), ToggleRow("Закрывать в трей", "Крестик скрывает окно. Двойной щелчок по значку открывает его.", closeInTray), ButtonGrid(2, Button("Мастер настройки", () => { EnsureStopped(); StartSetup(); }), Button("Выйти", () => { var ignored = ExitApplication(); }, false, false))));
            ((StackPanel)applicationCard.Child).Children.Add(new Border { Height=12 });
            ((StackPanel)applicationCard.Child).Children.Add(Button("Папка данных ↗",()=>{ Directory.CreateDirectory(data); Process.Start("explorer.exe",Engine.Quote(data)); },false,false));
            content.Children.Add(AccentSettings()); Gap(content, 14);
            var advanced = new CheckBox { IsChecked = settings.AdvancedMode, IsEnabled = !busy };
            advanced.Click += (s, e) => {
                if (busy) { advanced.IsChecked = settings.AdvancedMode; return; }
                settings.AdvancedMode = advanced.IsChecked == true; Save(); Navigate("Settings");
            };
            var modeCard = Card(ToggleRow("Продвинутый режим", "Дополнительные настройки и журнал работы. Если всё работает, менять их не нужно.", advanced));
            modeCard.Padding = new Thickness(16, 18, 16, 0);
            var path = new TextBox { Text = Root ?? "", IsReadOnly = true, Margin = new Thickness(0, 0, 0, 14) };
            var source = Stack(Label("НАБОР FLOWSEAL"), path, ButtonGrid(1, Button("Выбрать папку…", SelectFolder)));
            if (settings.AdvancedMode) { Gap(source, 8); source.Children.Add(ButtonGrid(2, Button("Обновления", () => { var ignored = Guard(CheckUpdates, false); }), Button("Релизы ↗", () => Process.Start("https://github.com/Flowseal/zapret-discord-youtube/releases/latest"), false, false))); }
            content.Children.Add(Card(source)); Gap(content, 16);
            string exe = ApplicationPaths.SourceExecutable;
            var startup = new CheckBox { IsChecked = AutoStart.Enabled(exe) };
            string startupReadError=AutoStart.ReadError;
            if (startupReadError != null) { startup.IsEnabled = false; startup.ToolTip = "Не удалось прочитать автозапуск: " + startupReadError; }
            startup.Click += (s, e) => {
                try { AutoStart.Set(exe, startup.IsChecked == true); AddLog("Автозапуск интерфейса: " + (startup.IsChecked == true ? "включён" : "выключен")); }
                catch (Exception ex) { startup.IsChecked = AutoStart.Enabled(exe); MessageBox.Show(Window, ex.Message, "Zapret"); }
            };
            var minimized = new CheckBox { IsChecked = settings.StartMinimized };
            minimized.Click += (s, e) => { settings.StartMinimized = minimized.IsChecked == true; Save(); };
            var autoBypass = new CheckBox { IsChecked = AutoStart.BypassEnabled(Root) };
            autoBypass.Click += async (s, e) => {
                bool enabled = autoBypass.IsChecked == true;
                await Guard(async t => {
                    NeedBundle();
                    if (Engine.ServiceState() == "NotInstalled")
                    {
                        if (!enabled) return;
                        if (selected == null) throw new InvalidOperationException("Выберите стратегию.");
                        await engine.InstallService(Root, selected, t);
                    }
                    else
                    {
                        if (!Engine.ServiceMatches(Root)) throw new InvalidOperationException("Служба zapret принадлежит другой сборке.");
                        await Engine.RunTool("sc.exe", new[] { "config", "zapret", "start=", enabled ? "auto" : "demand" }, t);
                        if (enabled) await Engine.ServiceAction(Root, "start", t);
                    }
                    AddLog("Автозапуск обхода: " + (enabled ? "включён" : "выключен"));
                }, true);
                autoBypass.IsChecked = AutoStart.BypassEnabled(Root);
            };
            var startupCard = Stack(Label("АВТОЗАПУСК"), ToggleRow("Запускать интерфейс с Windows", "Открывать программу при входе в Windows без повторного запроса прав. Для учётной записи администратора.", startup), ToggleRow("Запускать свёрнутым", "При автозапуске сразу скрывать окно в трей.", minimized), ToggleRow("Запускать обход с Windows", "Работает через службу даже без открытого интерфейса. Выключение отменяет будущий автозапуск; текущий обход можно остановить на главном экране.", autoBypass));
            if (startupReadError!=null) { var warning=Stack(Text("Не удалось прочитать автозапуск: " + startupReadError,14,Color("#D6AD73")),new Border { Height=10 },Button("Проверить снова",()=>Navigate("Settings"),false,false)); warning.Margin=new Thickness(0,0,0,14); startupCard.Children.Insert(2,warning); }
            content.Children.Add(Card(startupCard)); Gap(content, 16);
            content.Children.Add(applicationCard); Gap(content, 14);
            content.Children.Add(UpdateCard()); Gap(content, 14);
            content.Children.Add(DnsCard()); Gap(content, 14);
            if (!settings.AdvancedMode) { content.Children.Add(modeCard); return Scroll(content); }
            var svc = Stack(Label("ФОНОВАЯ СЛУЖБА"));
            serviceState = Text("", 17, null); svc.Children.Add(serviceState); Gap(svc, 10);
            svc.Children.Add(Text("Служба запускается вместе с Windows и работает при закрытом интерфейсе. Использует выбранную стратегию. Для смены стратегии переустанови службу.", 12, Muted)); Gap(svc, 16);
            svc.Children.Add(ButtonGrid(2, Button("Установить", () => { var ignored = Guard(async t => { NeedBundle(); if (selected == null) throw new InvalidOperationException("Нет выбранной стратегии."); await engine.InstallService(Root, selected, t); }, true); }, true), Button("Запустить", () => { var ignored = Guard(t => Engine.ServiceAction(Root, "start", t), true); }), Button("Остановить", () => { var ignored = Guard(t => Engine.ServiceAction(Root, "stop", t), true); }), Button("Удалить", () => { var ignored = Guard(async t => {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret")) { if (key != null) AddLog("Удаляемая служба: " + key.GetValue("ImagePath")); }
                await Engine.ServiceAction(Root, "delete", t); AddLog("Служба удалена.");
            }, true); })));
            var serviceCard = Card(svc);
            var filters = Stack(Label("ФИЛЬТРЫ"));
            var game = Ready ? GameFilter.Read(Root) : new GameFilter();
            var modes = new[] { "Выключен", "TCP + UDP", "Только TCP", "Только UDP" }; var codes = new[] { "disabled", "all", "tcp", "udp" };
            var mode = new ComboBox { ItemsSource = modes, SelectedIndex = Array.IndexOf(codes, game.Mode), Margin = new Thickness(0, 6, 0, 12) };
            filters.Children.Add(Text("Игровой фильтр", 14, null)); filters.Children.Add(mode);
            var tcp = new TextBox { Text = game.Tcp, Tag = "Например: 1024-65535", ToolTip = "TCP: порты через запятую или диапазон", Margin = new Thickness(0, 0, 0, 14) };
            var udp = new TextBox { Text = game.Udp, Tag = "Например: 1024-65535", ToolTip = "UDP: порты через запятую или диапазон", Margin = new Thickness(0, 0, 0, 14) };
            filters.Children.Add(Label("TCP ПОРТЫ")); filters.Children.Add(tcp); filters.Children.Add(Label("UDP ПОРТЫ")); filters.Children.Add(udp); Gap(filters, 4);
            var ipModes = new[] { "По списку IPSet", "Выключен (none)", "Все адреса (any)" }; var ipCodes = new[] { "loaded", "none", "any" };
            filters.Children.Add(Text("IPSet", 14, null)); var ip = new ComboBox { ItemsSource = ipModes, SelectedIndex = Ready ? Array.IndexOf(ipCodes, Bundle.IpMode(Root)) : 0, Margin = new Thickness(0, 6, 0, 12) }; filters.Children.Add(ip);
            var auto = new CheckBox { IsChecked = Ready && File.Exists(Path.Combine(Root, "utils", "check_updates.enabled")) }; filters.Children.Add(ToggleRow("Проверка обновлений", "При запуске оригинальных .bat Flowseal.", auto)); Gap(filters, 10);
            filters.Children.Add(ButtonGrid(2, Button("Сохранить", () => {
                NeedBundle(); EnsureStopped();
                if (!GameFilter.ValidPorts(tcp.Text) || !GameFilter.ValidPorts(udp.Text)) throw new InvalidOperationException("Допустимы порты 1–65535 и диапазоны через запятую, например 1024-65535.");
                Bundle.SetIpMode(Root, ipCodes[ip.SelectedIndex]);
                Bundle.WriteBackup(Path.Combine(Root, "utils", "game_filter.enabled"), "mode=" + codes[mode.SelectedIndex] + "\r\ntcp=" + tcp.Text + "\r\nudp=" + udp.Text + "\r\n");
                string flag = Path.Combine(Root, "utils", "check_updates.enabled"); if (auto.IsChecked == true) Bundle.WriteBackup(flag, "ENABLED"); else if (File.Exists(flag)) File.Delete(flag);
                AddLog("Фильтры сохранены. Переустановите службу, чтобы обновить её аргументы."); SetStatus("Фильтры сохранены");
            }, true), Button("Обновить IPSet", () => { var ignored = Guard(UpdateIpSet, false); })));
            filtersCard = Card(filters); content.Children.Add(filtersCard); Gap(content, 16);
            content.Children.Add(serviceCard); Gap(content, 16);
            var fakes = Stack(Label("ПАКЕТЫ ДЛЯ ОБХОДА (FAKE)"));
            var fakeNames = Ready ? Directory.GetFiles(Path.Combine(Root, "bin"), "*.bin").Where(p => !Path.GetFileName(p).StartsWith("ACTIVE_")).Select(Path.GetFileName).OrderBy(p => p).ToList() : new List<string>();
            var fake = new ComboBox { ItemsSource = fakeNames, SelectedIndex = fakeNames.Count > 0 ? 0 : -1, Margin = new Thickness(0, 0, 0, 12) }; fakes.Children.Add(fake);
            fakes.Children.Add(ButtonGrid(2, Button("Discord UDP", () => ReplaceFake(fake.SelectedItem as string, "ACTIVE_DISCORD_UDP.bin")), Button("Game UDP", () => ReplaceFake(fake.SelectedItem as string, "ACTIVE_GAME_UDP.bin"))));
            content.Children.Add(Card(fakes)); Gap(content, 16);
            content.Children.Add(modeCard); Gap(content, 14);
            content.Children.Add(Card(Stack(Label("ДОПОЛНИТЕЛЬНО"), Button("Стратегии и аргументы  ›", () => Navigate("Strategies"), false, false), Button("Списки и исключения  ›", () => Navigate("Lists"), false, false), Button("Журнал  ›", () => Navigate("Log"), false, false)))); Gap(content, 14);
            content.Children.Add(Card(Stack(Label("ИНСТРУМЕНТЫ FLOWSEAL"), Button("Оригинальный менеджер ↗", NativeManager), new Border { Height = 14 }, Text("Очистка кэша Discord и расширенные исправления доступны в оригинальном менеджере service.bat.", 12, Muted))));
            return Scroll(content);
        }
        void ApplyAccent(string value)
        {
            string hex = AccentTheme.Normalize(value);
            if (hex == null) throw new InvalidOperationException("Введите цвет в формате #RRGGBB, например #FFAD58.");
            string source;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shell.xaml"))
            using (var reader = new StreamReader(stream)) source = AccentTheme.Markup(reader.ReadToEnd(), hex);
            int start = source.IndexOf("<Window.Resources>") + "<Window.Resources>".Length;
            string resources = source.Substring(start, source.IndexOf("</Window.Resources>") - start);
            var dictionary = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + resources + "</ResourceDictionary>");
            settings.AccentColor = hex; Save();
            Window.Resources.Clear(); foreach (DictionaryEntry entry in dictionary) Window.Resources.Add(entry.Key, entry.Value);
            Navigate("Settings");
        }
        UIElement AccentSettings()
        {
            var content = Stack(Label("ОСНОВНОЙ ЦВЕТ"));
            var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            string[] colors = { AccentTheme.Default, "#6EA8FF", "#75D9AB", "#B59AFF", "#F090C1", "#61D5DF" };
            string[] names = { "Оранжевый", "Синий", "Зелёный", "Фиолетовый", "Розовый", "Бирюзовый" };
            for (int i = 0; i < colors.Length; i++) {
                string hex = colors[i]; var brush = Color(hex);
                var swatch = Button("", () => ApplyAccent(hex), false, false);
                swatch.Content = new System.Windows.Shapes.Ellipse { Width = 24, Height = 24, Fill = brush, Stroke = settings.AccentColor == hex ? Brushes.White : Brushes.Transparent, StrokeThickness = 2 };
                swatch.ToolTip = names[i]; swatch.Background = Brushes.Transparent; swatch.Padding = new Thickness(6); swatch.Margin = new Thickness(0, 0, 7, 0); swatches.Children.Add(swatch);
            }
            content.Children.Add(swatches);
            var input = new TextBox { Text = settings.AccentColor, Tag = "#RRGGBB", MaxLength = 7, Width = 122, ToolTip = "HEX: #RRGGBB", Margin = new Thickness(0, 0, 8, 8) };
            content.Children.Add(Row(input, Button("Применить", () => ApplyAccent(input.Text), true, false)));
            content.Children.Add(Row(Button("Свой цвет…", PickAccent, false, false), Button("Сбросить", () => ApplyAccent(AccentTheme.Default), false, false)));
            var card = Card(content); card.Padding = new Thickness(16); return card;
        }
        sealed class ColorDialogOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public ColorDialogOwner(Window window) { Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
        }
        void PickAccent()
        {
            var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(settings.AccentColor);
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) }) {
                if (dialog.ShowDialog(new ColorDialogOwner(Window)) == System.Windows.Forms.DialogResult.OK)
                    ApplyAccent(String.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B));
            }
        }
        void EnsureStopped(bool allowBusy = false)
        {
            if ((!allowBusy && busy) || engine.Running || Engine.AnyWinws() || Engine.ServiceState() == "Running") throw new InvalidOperationException("Дождитесь завершения проверки и остановите Zapret перед изменением файлов сборки.");
        }
        UIElement ToggleRow(string title, string description, CheckBox toggle)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            var caption = Text(description, 12, Muted); caption.Margin = new Thickness(0, 7, 20, 0);
            grid.Children.Add(Stack(Text(title, 16, null), caption)); Grid.SetColumn(toggle, 1); toggle.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(toggle); return grid;
        }
        void SelectFolder()
        {
            if (busy || engine.Running) throw new InvalidOperationException("Дождитесь завершения операции и остановите Zapret.");
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Папка с service.bat, bin и lists", SelectedPath = Root, ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                Bundle.ValidateForSetup(dialog.SelectedPath);
                settings.Root = dialog.SelectedPath; bundleProblem=null; notifiedBundleRoot=null; results.Clear(); Save(); Reload(); Navigate("Settings");
            }
        }
        async Task CheckUpdates(CancellationToken token)
        {
            NeedBundle(); SetStatus("Проверяем версию на GitHub…");
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                using (var response = await client.GetAsync("https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt", token))
                {
                    response.EnsureSuccessStatusCode(); string latest = (await response.Content.ReadAsStringAsync()).Trim();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(latest, @"^\d+\.\d+\.\d+([\w.-]*)?$")) throw new IOException("GitHub вернул неизвестный формат версии.");
                    string message = "Установлено: " + Bundle.Version(Root) + "\nВерсия upstream: " + latest + "\n\nСкачать сборку можно кнопкой «Релизы».";
                    AddLog(message.Replace("\n", " ")); MessageBox.Show(Window, message, "Версия Flowseal");
                }
            }
        }
        async Task UpdateIpSet(CancellationToken token) { await InstallIpSet(token); }
        static bool ValidCidr(string value)
        {
            var p = value.Split('/'); System.Net.IPAddress ip; int bits;
            return p.Length <= 2 && System.Net.IPAddress.TryParse(p[0], out ip) && (p.Length == 1 || int.TryParse(p[1], out bits) && bits >= 0 && bits <= (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
        }
        void ReplaceFake(string source, string target)
        {
            NeedBundle(); EnsureStopped(); if (String.IsNullOrEmpty(source)) throw new InvalidOperationException("Выберите .bin файл.");
            string dest = Path.Combine(Root, "bin", target); if (File.Exists(dest)) File.Copy(dest, dest + ".ui-backup", true);
            File.Copy(Path.Combine(Root, "bin", source), dest, true); AddLog(target + " ← " + source); SetStatus("Fake-пакет заменён, предыдущий сохранён в .ui-backup");
        }
        void NativeManager()
        {
            NeedBundle(); if (busy) return;
            Process.Start(new ProcessStartInfo("cmd.exe", "/d /c \"\"" + Path.Combine(Root, "service.bat") + "\" admin\"") { WorkingDirectory = Root, UseShellExecute = true, Verb = "runas" });
        }
        async Task Diagnostics(CancellationToken token)
        {
            var checks = new List<Tuple<string, string, bool?>>(); var report = new StringBuilder();
            Action<string,string,bool?> add = (title, detail, good) => { checks.Add(Tuple.Create(title,detail,good)); report.AppendLine(title + ": " + detail); };
            add("Права администратора", Engine.IsAdmin ? "Есть — системные операции доступны" : "Недостаточно прав", Engine.IsAdmin);
            add("Набор Zapret", Ready ? "Flowseal " + Bundle.Version(Root) + "\n" + Root : "Выбери папку набора в настройках", Ready);
            string state = Engine.ServiceState();
            add("Служба Windows", ServiceLabel(state) + (state != "NotInstalled" && Ready && !Engine.ServiceMatches(Root) ? " · относится к другой папке" : ""), null);
            add("Процесс обхода", engine.Running ? "Запущен этим приложением" : Engine.AnyWinws() ? "Найден внешний winws" : "Остановлен", null);
            foreach (string name in new[] { "BFE", "WinDivert", "WinDivert14", "Adguard", "Killer Network Service", "Intel Connectivity Network Service" }) {
                try { using (var service = new ServiceController(name)) {
                    bool running = service.Status == ServiceControllerStatus.Running;
                    string title = name == "BFE" ? "Базовая фильтрация Windows" : name;
                    string detail = name == "BFE" ? (running ? "Работает — драйвер может фильтровать трафик" : "Остановлена — может мешать подключению") : (running ? "Работает" : "Остановлена");
                    if (running && (name == "Adguard" || name.Contains("Killer") || name.Contains("Intel"))) detail += " · возможен конфликт с обходом";
                    add(title, detail, name == "BFE" ? (bool?)running : null);
                } } catch (InvalidOperationException) { if (name == "BFE") add("Базовая фильтрация Windows", "Служба недоступна", false); }
            }
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings")) if (key != null) {
                bool proxy = Convert.ToInt32(key.GetValue("ProxyEnable",0)) != 0;
                add("Прокси текущей учётной записи", proxy ? "Включён: " + key.GetValue("ProxyServer", "") : "Выключен", null);
            }
            if (Ready) {
                var missing = new[] { "winws.exe", "WinDivert.dll", "WinDivert64.sys", "cygwin1.dll" }.Where(f => !File.Exists(Path.Combine(Root,"bin",f))).ToList();
                add("Файлы движка", missing.Count == 0 ? "Все необходимые файлы на месте" : "Не найдены: " + String.Join(", ", missing), missing.Count == 0);
            }
            var content = Stack(DiagnosticsCards(checks));
            try {
                string tcp = await Engine.RunTool("netsh.exe", new[] { "interface", "tcp", "show", "global" }, token); report.AppendLine(tcp);
                var extra = new Expander { Header = Text("Параметры TCP",16,null), Content = ReportCards(tcp), Foreground=Color("#E0E0E7"), HorizontalContentAlignment=HorizontalAlignment.Stretch }; content.Children.Add(extra);
            } catch (OperationCanceledException) { throw; } catch (Exception e) { content.Children.Add(Text("Не удалось прочитать TCP: " + e.Message,14,Muted)); }
            content.Children.Add(Button("Скопировать отчёт", () => Clipboard.SetText(report.ToString()), false, false));
            AddLog("Диагностика завершена без изменения настроек."); Surface("Диагностика", content, null).ShowDialog();
        }
        UIElement LogPage()
        {
            var panel = new DockPanel();
            var tools = Row(Button("Сохранить журнал", () => {
                var dialog = new SaveFileDialog { Filter = "Text|*.txt", FileName = "zapret-log.txt" }; if (dialog.ShowDialog(Window) == true) File.WriteAllLines(dialog.FileName, log, Encoding.UTF8);
            }), Button("Очистить вид", () => { log.Clear(); if (logItems != null) logItems.Children.Clear(); }, false, false)); tools.Margin = new Thickness(0, 0, 0, 16); DockPanel.SetDock(tools, Dock.Top); panel.Children.Add(tools);
            logItems = new StackPanel(); foreach (string line in log.AsEnumerable().Reverse().Take(300)) logItems.Children.Add(LogEntry(line)); panel.Children.Add(Scroll(logItems)); return panel;
        }
    }
}
