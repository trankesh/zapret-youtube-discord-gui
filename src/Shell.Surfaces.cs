using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZapretDesktop
{
    public sealed partial class Shell
    {
        public string GuiVersion { get { var version = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute),false).Cast<AssemblyInformationalVersionAttribute>().FirstOrDefault(); return version == null ? Assembly.GetExecutingAssembly().GetName().Version.ToString(3) : version.InformationalVersion; } }
        string watchedRoot;
        DateTime watchedVersionTime;
        DateTime watchedFallbackTime;
        string installedVersion = "не выбран";
        TextBlock installedVersionText;
        readonly HashSet<string> testSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void RefreshVersion()
        {
            DateTime changed = Ready ? File.GetLastWriteTimeUtc(Path.Combine(Root, "service.bat")) : DateTime.MinValue;
            string fallback = Ready ? Path.Combine(Root, ".service", "version.txt") : "";
            DateTime fallbackTime = File.Exists(fallback) ? File.GetLastWriteTimeUtc(fallback) : DateTime.MinValue;
            if (watchedRoot != Root || watchedVersionTime != changed || watchedFallbackTime != fallbackTime) {
                watchedRoot = Root; watchedVersionTime = changed; watchedFallbackTime = fallbackTime;
                try { installedVersion = Bundle.Version(Root); }
                catch (IOException) { installedVersion = "недоступна"; watchedVersionTime = DateTime.MinValue; }
                catch (UnauthorizedAccessException) { installedVersion = "недоступна"; watchedVersionTime = DateTime.MinValue; }
            }
            Find<TextBlock>("BundleBadge").Text = Ready ? "Zapret " + installedVersion : String.IsNullOrWhiteSpace(Root) ? "Набор не выбран" : "Набор недоступен";
            if (installedVersionText != null) installedVersionText.Text = "Набор Flowseal · " + installedVersion;
        }
        UIElement VersionsCard()
        {
            RefreshVersion();
            installedVersionText = Text("Набор Flowseal · " + installedVersion, 16, Muted);
            var content=Stack(Label("ВЕРСИИ"),Text("Zapret Desktop GUI · "+GuiVersion,16,null),new Border {Height=10},installedVersionText,new Border {Height=16});
            guiCheckButton=Wide("Проверить обновления GUI",()=>{ var ignored=CheckGuiUpdate(); }); guiCheckButton.Margin=new Thickness(0); content.Children.Add(guiCheckButton);
            guiUpdateNote=Text(guiUpdateMessage ?? "",14,Muted); guiUpdateNote.Margin=new Thickness(0,12,0,0); content.Children.Add(guiUpdateNote);
            guiReleaseButton=Wide("Открыть релиз на GitHub ↗",()=>{ if(guiRelease!=null) Process.Start(guiRelease.ReleaseUrl); },true); guiReleaseButton.Margin=new Thickness(0,12,0,0); content.Children.Add(guiReleaseButton);
            RefreshGuiUpdateControls(); return Card(content);
        }
        bool guiUpdateChecking;
        string guiUpdateMessage;
        GuiRelease guiRelease;
        TextBlock guiUpdateNote;
        Button guiCheckButton,guiReleaseButton;
        void RefreshGuiUpdateControls()
        {
            if(guiCheckButton==null) return;
            guiCheckButton.IsEnabled=!guiUpdateChecking && !busy; guiCheckButton.Content=guiUpdateChecking ? "Проверяем…" : "Проверить обновления GUI";
            guiUpdateNote.Text=guiUpdateMessage ?? ""; guiUpdateNote.Visibility=String.IsNullOrEmpty(guiUpdateMessage) ? Visibility.Collapsed : Visibility.Visible;
            guiReleaseButton.Visibility=guiRelease==null || guiUpdateChecking ? Visibility.Collapsed : Visibility.Visible;
        }
        async Task CheckGuiUpdate()
        {
            if(guiUpdateChecking || SweepLocked || busy) return;
            guiUpdateChecking=true; guiRelease=null; guiUpdateMessage="Проверяем последний стабильный релиз на GitHub…"; RefreshGuiUpdateControls();
            try {
                var release=await GuiUpdates.Check(CancellationToken.None); int comparison=release.CompareTo(GuiVersion);
                if(comparison>0) { guiRelease=release; guiUpdateMessage="Доступна новая версия · "+release.VersionText; }
                else guiUpdateMessage=comparison==0 ? "Установлена актуальная версия · "+GuiVersion : "Установленная версия новее опубликованной на GitHub ("+release.VersionText+").";
            }
            catch(OperationCanceledException) { guiUpdateMessage="Не дождались ответа GitHub. Попробуй ещё раз."; }
            catch(HttpRequestException) { guiUpdateMessage="Не удалось связаться с GitHub. Проверь интернет и повтори проверку."; }
            catch(IOException error) { guiUpdateMessage=error.Message; }
            catch(Exception) { guiUpdateMessage="Не удалось проверить обновления. Попробуй позже."; }
            finally { guiUpdateChecking=false; RefreshGuiUpdateControls(); }
        }
        Window Surface(string title, UIElement content, UIElement footer)
        {
            var dialog = new Window { Title = title, Owner = Window, Width = Window.Width, Height = Window.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Color("#0B0B0D"), Foreground = Color("#E0E0E7"), FontFamily = Window.FontFamily, FontSize = 15, Icon = Window.Icon, UseLayoutRounding = true };
            dialog.Resources.MergedDictionaries.Add(Window.Resources); WindowTheme.Attach(dialog);
            var grid = new Grid { Margin = new Thickness(20) }; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = Text(title, 25, null); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 2, 0, 20); grid.Children.Add(heading);
            var scroll = Scroll(content); Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
            if (footer == null) { var close = Button("Закрыть", () => dialog.Close(), true, false); close.HorizontalAlignment = HorizontalAlignment.Stretch; close.Margin = new Thickness(0, 14, 0, 0); footer = close; }
            Grid.SetRow(footer, 2); grid.Children.Add(footer); dialog.Content = new Border { Background = Color("#0B0B0D"), Child = grid }; return dialog;
        }
        UIElement ReportCards(string report)
        {
            var panel = new StackPanel();
            foreach (string paragraph in Regex.Split(report.Trim(), @"\r?\n\s*\r?\n")) {
                if (String.IsNullOrWhiteSpace(paragraph)) continue;
                var step = Regex.Match(paragraph, @"^(\d+)\.\s*([\s\S]*)$");
                StackPanel body;
                if (step.Success) { body = Stack(Text("ШАГ " + step.Groups[1].Value, 14, Accent), new Border { Height = 8 }, Text(step.Groups[2].Value, 15, null)); }
                else body = Stack(Text(paragraph, 15, null));
                var card = Card(body); card.Padding = new Thickness(16); card.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(card);
            }
            return panel;
        }
        void StyledReport(string title, string report)
        {
            var content = Stack(title == "Предпросмотр hosts" ? HostsPreviewCards(report) : ReportCards(report));
            var copy = Button("Скопировать текст", () => { Clipboard.SetText(report); }, false, false); content.Children.Add(copy);
            Surface(title, content, null).ShowDialog();
        }
        UIElement HostsPreviewCards(string report)
        {
            var content = new StackPanel();
            if (hostsPlan != null) { content.Children.Add(Text("Новых: " + hostsPlan.Added + " · заменить: " + hostsPlan.Changed + " · удалить из блока: " + hostsPlan.Removed, 16, Accent)); Gap(content, 16); }
            foreach (string line in Regex.Split(report, @"\r?\n")) {
                string trimmed = line.Trim(); if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                var parts = Regex.Split(trimmed.Split('#')[0].Trim(), @"\s+"); if (parts.Length < 2) continue;
                var address = Text(parts[0], 14, Muted); address.Margin = new Thickness(0, 8, 0, 0);
                var card = Card(Stack(Text(String.Join(", ", parts.Skip(1)), 16, null), address)); card.Padding = new Thickness(14); card.Margin = new Thickness(0, 0, 0, 10); content.Children.Add(card);
            }
            return content;
        }
        UIElement LogEntry(string line)
        {
            string time = line.Length > 8 ? line.Substring(0, 8) : "";
            string message = line.Length > 10 ? line.Substring(10).TrimStart() : line;
            var body = Stack(Text(time, 14, Muted), new Border { Height = 5 }, Text(message, 14, line.Contains("ОШИБКА") ? Color("#DD9696") : Color("#D5D5DD")));
            return new Border { Padding = new Thickness(0, 12, 0, 12), BorderBrush = Color("#25252B"), BorderThickness = new Thickness(0, 0, 0, 1), Child = body };
        }
        UIElement ArgumentsCards(Strategy strategy)
        {
            string args = Bundle.Arguments(strategy, Root, GameFilter.Read(Root));
            var content = Stack(Text(strategy.Name, 19, Accent)); Gap(content, 16); int index = 0;
            foreach (string profile in args.Split(new[] { " --new " }, StringSplitOptions.None)) {
                index++; var filter = Regex.Match(profile, @"--filter-(tcp|udp)=([^\s]+)");
                string title = "Профиль " + index + (filter.Success ? " · " + filter.Groups[1].Value.ToUpperInvariant() + " " + filter.Groups[2].Value : "");
                var detail = Text(profile.Replace(" --", "\n--"), 14, Muted);
                var item = new Expander { Header = Text(title, 16, null), Content = detail, Foreground = Color("#E0E0E7"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var card = Card(item); card.Padding = new Thickness(14); card.Margin = new Thickness(0, 0, 0, 10); content.Children.Add(card);
            }
            content.Children.Add(Button("Скопировать аргументы", () => Clipboard.SetText(args), false, false)); return content;
        }
        void ChooseTestStrategies(Action changed) { TestSelectionSurface(changed).ShowDialog(); }
        Window TestSelectionSurface(Action changed)
        {
            var draft = new HashSet<string>(testSelection, StringComparer.OrdinalIgnoreCase);
            var content = new StackPanel();
            content.Children.Add(Text("Отметь варианты, которые хочешь проверить. Остальные будут пропущены.", 14, Muted)); Gap(content, 12);
            var list = new StackPanel(); var search = new TextBox { Tag = "Поиск стратегии", Margin = new Thickness(0, 0, 0, 14) };
            var count = Text("", 14, Accent);
            Button apply = null;
            Action render = () => {
                list.Children.Clear(); count.Text = "Выбрано: " + draft.Count;
                if (apply != null) apply.IsEnabled = draft.Count > 0;
                foreach (var strategy in strategies.Where(s => s.Name.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)) {
                    string name = strategy.Name;
                    var toggle = new CheckBox { IsChecked = draft.Contains(name), Margin = new Thickness(0) };
                    toggle.Click += (s, e) => { if (toggle.IsChecked == true) draft.Add(name); else draft.Remove(name); count.Text = "Выбрано: " + draft.Count; if (apply != null) apply.IsEnabled = draft.Count > 0; };
                    var row = ToggleRow(name, strategy.Methods, toggle); list.Children.Add(row);
                }
            };
            content.Children.Add(ButtonGrid(2,Button("Выбрать все", () => { foreach (var strategy in strategies) draft.Add(strategy.Name); render(); }, false, false), Button("Снять все", () => { draft.Clear(); render(); }, false, false)));
            Gap(content,12);
            content.Children.Add(search); content.Children.Add(count); Gap(content, 14); content.Children.Add(list); search.TextChanged += (s, e) => render(); render();
            Window dialog = null;
            apply = Wide("Сохранить выбор", () => { testSelection.Clear(); foreach (string name in draft) testSelection.Add(name); settings.TestStrategies = testSelection.ToArray(); settings.TestSelectedOnly = true; Save(); dialog.Close(); changed(); }, true);
            apply.IsEnabled = draft.Count > 0; apply.Margin = new Thickness(0, 14, 0, 0); dialog = Surface("Стратегии для теста", content, apply); return dialog;
        }
        UIElement DiagnosticsCards(List<Tuple<string, string, bool?>> checks)
        {
            var content = new StackPanel();
            foreach (var check in checks) {
                var state = Text(check.Item2, 14, check.Item3 == true ? Color("#91BD9E") : check.Item3 == false ? Color("#D6AD73") : Muted); state.Margin = new Thickness(0, 7, 0, 0);
                var card = Card(Stack(Text(check.Item1, 17, null), state)); card.Padding = new Thickness(16); card.Margin = new Thickness(0, 0, 0, 12); content.Children.Add(card);
            }
            return content;
        }
        public void PreviewSurface(string name)
        {
            if (!preview) throw new InvalidOperationException();
            UIElement content;
            if (name == "Updates") {
                var previous = settings.HostsReceipt; settings.HostsReceipt = new HostsReceipt();
                content = UpdateCard(); settings.HostsReceipt = previous;
            }
            else if (name == "Guide") content = ReportCards("1. В мастере актуальность hosts и IPSet проверяется автоматически.\n\n2. Нажми «Обновить hosts и IPSet», если есть новые списки.\n\n3. Подтверди изменения hosts. Другие записи сохранятся.\n\n4. Перезапусти браузер и подключи Zapret.");
            else content = DiagnosticsCards(new List<Tuple<string, string, bool?>> { Tuple.Create("Набор Zapret", "Flowseal " + (Ready ? Bundle.Version(Root) : "не выбран"), (bool?)true), Tuple.Create("Права администратора", "Пример состояния диагностики", (bool?)null), Tuple.Create("Служба Windows", "Пример: остановлена", (bool?)null), Tuple.Create("Файлы набора", "Пример: все файлы на месте", (bool?)true) });
            var dialog = name == "Dns" ? DnsSurface() : name == "TestSelection" ? TestSelectionSurface(delegate { }) : Surface(name == "Updates" ? "Hosts и IPSet — пример" : name == "Guide" ? "Как обновить hosts" : "Диагностика — пример", content, null);
            dialog.ShowInTaskbar = false; dialog.Left = -20000; dialog.Top = 0; dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Show(); dialog.UpdateLayout();
            var visual = (FrameworkElement)dialog.Content; var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"); Directory.CreateDirectory(directory);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(file);
            dialog.Close();
        }
    }
}
