using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZapretDesktop
{
    public sealed partial class Shell
    {
        bool setupVisible;
        int setupStep = 1;
        HostsPlan hostsPlan;
        string hostsRemote, ipsetRemote;
        string hostsNote = "Ещё не проверяли", ipsetNote = "Ещё не проверяли";
        bool ipsetCurrent;
        string BackupDirectory { get { return Path.Combine(data, "backups"); } }
        const string IpsetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
        void StartSetup() { setupVisible = true; setupStep = 1; RenderSetup(); }
        public void PreviewSetup(int step) { if (!preview) throw new InvalidOperationException(); setupVisible = true; setupStep = step; RenderSetup(); }
        Button Wide(string label, Action action, bool primary = false)
        {
            var button = Button(label, action, primary); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0, 0, 0, 10); return button;
        }
        void RenderSetup()
        {
            actions.Clear(); leaveEditor = null; gameToggle = null; gameCaption = null; testResults = null; testProgress = null; serviceState = null; logItems = null; connect = null; installedVersionText = null;
            strategyOverlay = null; homeRoot = null; currentPage = "Setup";
            ((RowDefinition)Window.FindName("HeaderRow")).Height = new GridLength(58);
            ((RowDefinition)Window.FindName("FooterRow")).Height = new GridLength(0);
            ((RowDefinition)Window.FindName("NavigationRow")).Height = new GridLength(0);
            Find<Grid>("HeaderBar").Visibility = Visibility.Visible; Find<StackPanel>("PageHeader").Visibility = Visibility.Visible;
            Find<Border>("BottomNavigation").Visibility = Visibility.Collapsed; Find<TextBlock>("StatusLine").Visibility = Visibility.Collapsed;
            Find<Grid>("PageLayout").Margin = new Thickness(20, 6, 20, 16);
            Find<TextBlock>("PageTitle").Text = "Подготовка Zapret";
            Find<TextBlock>("PageSubtitle").Text = "Шаг " + setupStep + " из 4 · " + (setupStep == 1 ? "Набор конфигураций" : setupStep == 2 ? "Hosts и IPSet" : setupStep == 3 ? "Защищённый DNS" : "Завершение настройки");
            var content = new StackPanel(); var navigation = new StackPanel();
            if (setupStep == 1) {
                if (bundleProblem!=null && !String.IsNullOrWhiteSpace(Root)) {
                    var notice=Card(Stack(Text("Набор Flowseal недоступен",19,Accent),new Border {Height=10},Text(bundleProblem,15,null),new Border {Height=10},Text("Прежняя папка: " + Root,14,Muted)));
                    notice.Padding=new Thickness(16); content.Children.Add(notice); Gap(content,16);
                    if (engine.Running || Engine.ServiceState()=="Running" && Engine.ServiceMatches(Root))
                        content.Children.Add(Wide("Отключить прежнее подключение",()=>{ var ignored=Guard(async token=>{ if (engine.Running) engine.Stop(); if (Engine.ServiceState()=="Running" && Engine.ServiceMatches(Root)) await Engine.ServiceAction(Root,"stop",token); RenderSetup(); },true); }));
                }
                content.Children.Add(Text("Выбери набор Zapret", 20, null)); Gap(content, 12);
                content.Children.Add(Text("Набор Flowseal содержит файлы и готовые варианты обхода. Выбери распакованную папку или используй набор, который уже включён в EXE. Этот шаг обязателен; остальные можно пропустить.", 14, Muted)); Gap(content, 20);
                content.Children.Add(Wide("Выбрать папку…", SelectSetupFolder, true));
                content.Children.Add(Wide("Встроенный Flowseal " + EmbeddedBundle.Version, () => { var ignored = Guard(async token => {
                    string root = await Task.Run(() => EmbeddedBundle.Extract(data), token); token.ThrowIfCancellationRequested(); SetSetupRoot(root);
                }, true); }));
                content.Children.Add(Button("Как скачать свежий набор?", BundleGuide, false, false)); Gap(content, 16);
                if (Ready) {
                    var found = Stack(Text("Подключён Flowseal " + Bundle.Version(Root), 16, Accent), new Border { Height = 8 }, Text(Root, 14, Muted)); content.Children.Add(Card(found)); Gap(content, 16);
                    navigation.Children.Add(Wide("Продолжить настройку", () => { Bundle.ValidateForSetup(Root); setupStep = 2; RenderSetup(); }, true));
                    navigation.Children.Add(Button("Пропустить остальные шаги",()=>FinishSetup(false),false,false));
                }
            }
            else if (setupStep == 2) {
                content.Children.Add(Text("Проверим списки", 20, null)); Gap(content, 10);
                content.Children.Add(Text("Необязательный шаг. Эти списки помогают Zapret находить нужные адреса сайтов. Обновить их можно сейчас или позже в настройках.", 14, Muted)); Gap(content, 18);
                content.Children.Add(UpdateCard()); Gap(content, 14);
                navigation.Children.Add(Wide(hostsPlan != null && hostsPlan.Current && ipsetCurrent ? "Далее" : "Пропустить этот шаг", () => { setupStep = 3; RenderSetup(); }, true));
                navigation.Children.Add(Button("Назад к выбору набора", () => { setupStep = 1; RenderSetup(); }, false, false));
                content.Children.Add(Text("Если проверка недоступна, можно продолжить и вернуться к обновлениям в настройках.", 14, Muted));
            }
            else if (setupStep == 3) {
                content.Children.Add(Text("Сделаем DNS защищённее", 22, null)); Gap(content, 12);
                content.Children.Add(Text("Рекомендуемый шаг · можно пропустить. DoH шифрует DNS-запросы и помогает против их подмены. Доступность зависит от сети и выбранного сервера.", 14, Muted)); Gap(content, 18);
                content.Children.Add(DnsCard()); Gap(content, 14);
                navigation.Children.Add(Wide("Далее", () => { setupStep=4; RenderSetup(); },true));
                navigation.Children.Add(Button("Пропустить и открыть Zapret",()=>FinishSetup(false),false,false));
                navigation.Children.Add(Button("Назад к hosts и IPSet", () => { setupStep=2; RenderSetup(); },false,false));
            }
            else {
                content.Children.Add(Text(hostsPlan != null && hostsPlan.Current && ipsetCurrent ? "Всё подготовлено" : "Набор подключён", 22, null)); Gap(content, 14);
                if (hostsPlan == null || !hostsPlan.Current || !ipsetCurrent) { content.Children.Add(Text("Необязательные шаги можно выполнить позже в настройках.", 14, Muted)); Gap(content, 14); }
                content.Children.Add(Card(Stack(Text("Набор: " + (Ready ? "Flowseal " + Bundle.Version(Root) : "не выбран"), 16, null), new Border { Height = 12 }, Text("Hosts: " + hostsNote, 14, Muted), new Border { Height = 10 }, Text("IPSet: " + ipsetNote, 14, Muted)))); Gap(content, 18);
                content.Children.Add(Text("На главной нажми «Подключить». Если сайты не открываются, запусти автоподбор стратегии. Крестик скрывает приложение в трей.", 14, Muted)); Gap(content, 18);
                content.Children.Add(Wide("Открыть Zapret", () => FinishSetup(false), true));
                content.Children.Add(Wide("Перейти к автоподбору", () => FinishSetup(true)));
                content.Children.Add(Button("Назад к настройке DNS", () => { setupStep = 3; RenderSetup(); }, false, false));
            }
            var layout=new Grid(); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            layout.Children.Add(Scroll(content)); navigation.Margin=new Thickness(0,12,0,0); Grid.SetRow(navigation,1); layout.Children.Add(navigation);
            Find<ContentControl>("Page").Content = layout;
            if (busy) foreach (var button in actions) button.IsEnabled = false;
        }
        void FinishSetup(bool tests)
        {
            NeedBundle(); Bundle.ValidateForSetup(Root); settings.SetupVersion = 1; Save(); setupVisible = false; Navigate(tests ? "Tests" : "Home");
        }
        void SetSetupRoot(string root)
        {
            Bundle.ValidateForSetup(root);
            bool same=!String.IsNullOrWhiteSpace(Root) && AutoStart.SamePath(Root,root);
            if (!same && (Engine.AnyWinws() || Engine.ServiceState() == "Running")) throw new InvalidOperationException("Сначала отключи работающий Zapret. Если он запущен другой программой, отключи его в той программе. Затем выбери новую папку.");
            settings.Root = root; settings.SetupVersion = 0; settings.Strategy = null; ipsetRemote = null; ipsetCurrent = false; ipsetNote = "Ещё не проверяли";
            bundleProblem=null; notifiedBundleRoot=null;
            Save(); Reload(); RenderSetup();
        }
        void SelectSetupFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Выбери папку Flowseal с файлом service.bat и папкой bin", SelectedPath = Ready ? Root : "", ShowNewFolderButton = false })
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) SetSetupRoot(dialog.SelectedPath);
        }
        void BundleGuide()
        {
            var content = Stack(ReportCards("1. Открой официальные релизы Flowseal.\n\n2. Скачай ZIP-архив и полностью распакуй его.\n\n3. Нажми «Выбрать папку» и укажи корень набора с service.bat, bin, lists и utils.\n\nВстроенный набор можно использовать без скачивания. Версия указана на кнопке."));
            content.Children.Add(Wide("Открыть релизы Flowseal ↗", () => System.Diagnostics.Process.Start("https://github.com/Flowseal/zapret-discord-youtube/releases/latest"), true));
            Surface("Подключение набора", content, null).ShowDialog();
        }
        UIElement UpdateCard()
        {
            var content = Stack(Label("HOSTS И IPSET"), Text("Hosts: " + hostsNote, 14, Muted), new Border { Height = 10 }, Text("IPSet: " + ipsetNote, 14, Muted)); Gap(content, 16);
            var dnsHosts = new CheckBox { IsChecked=settings.IncludeDnsHosts };
            dnsHosts.Click += (s,e) => { settings.IncludeDnsHosts=dnsHosts.IsChecked==true; hostsPlan=hostsRemote==null ? null : HostsUpdate.Plan(HostsUpdate.SystemPath,SecureDns.HostsSource(hostsRemote,settings.IncludeDnsHosts)); hostsNote=hostsPlan==null ? "проверь актуальность" : hostsPlan.Current ? "актуален" : "есть изменения, открой предпросмотр"; Save(); RefreshUpdates(); };
            content.Children.Add(ToggleRow("Адреса Secure DNS в hosts", "Добавляет известные адреса DNS-серверов. Запишется только по кнопке «Обновить hosts».",dnsHosts)); Gap(content,12);
            content.Children.Add(Wide("Проверить актуальность", () => { var ignored = Guard(CheckPrerequisites, false); }));
            var updateButtons = ButtonGrid(2, Button("Обновить hosts", () => { var ignored = Guard(UpdateHosts, true); }, true), Button("Обновить IPSet", () => { var ignored = Guard(InstallIpSet, true); }));
            updateButtons.Margin = new Thickness(0, 0, 0, 12); content.Children.Add(updateButtons);
            content.Children.Add(Button("Как обновить hosts?", HostsGuide, false, false));
            if (hostsPlan != null && !hostsPlan.Current) content.Children.Add(Button("Посмотреть изменения hosts", () => ShowReport("Предпросмотр hosts", hostsPlan.Text), false, false));
            if (settings.HostsReceipt != null) content.Children.Add(Button("Восстановить hosts из копии", () => { var ignored = Guard(RestoreHosts, true); }));
            return Card(content);
        }
        async Task CheckPrerequisites(CancellationToken token)
        {
            NeedBundle(); hostsNote = "Проверяем…"; ipsetNote = "Проверяем…"; RefreshUpdates();
            await Task.WhenAll(CheckHosts(token), CheckIpSet(token));
            Save(); RefreshUpdates();
        }
        async Task CheckHosts(CancellationToken token)
        {
            try {
                hostsRemote = await HostsUpdate.Download(HostsUpdate.Url, token); hostsPlan = HostsUpdate.Plan(HostsUpdate.SystemPath, SecureDns.HostsSource(hostsRemote,settings.IncludeDnsHosts));
                hostsNote = hostsPlan.Current ? "актуален" : "нужно обновить: +" + hostsPlan.Added + ", заменить " + hostsPlan.Changed + ", удалить " + hostsPlan.Removed;
                if (hostsPlan.Current) settings.HostsCheckedAt = DateTimeOffset.Now.ToString("o");
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); hostsPlan = null; hostsNote = "истекло время ожидания; повтори проверку"; }
            catch (Exception e) { hostsPlan = null; hostsNote = "не удалось проверить: " + e.Message; AddLog(hostsNote); }
        }
        string CanonicalIpSet(string text)
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            if (lines.Count < 10 || lines.Any(l => !ValidCidr(l))) throw new InvalidDataException("Некорректный список IPSet.");
            return String.Join("\n", lines.OrderBy(l => l, StringComparer.Ordinal));
        }
        async Task CheckIpSet(CancellationToken token)
        {
            try {
                ipsetRemote = await HostsUpdate.Download(IpsetUrl, token); string expected = CanonicalIpSet(ipsetRemote);
                string file = Path.Combine(Root, "lists", "ipset-all.txt"); if (Bundle.IpMode(Root) != "loaded") file += ".backup";
                ipsetCurrent = false;
                if (File.Exists(file)) { try { ipsetCurrent = expected == CanonicalIpSet(File.ReadAllText(file)); } catch (InvalidDataException) { } }
                ipsetNote = ipsetCurrent ? "актуален" : "доступен свежий список";
                if (ipsetCurrent) settings.IpsetCheckedAt = DateTimeOffset.Now.ToString("o");
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); ipsetRemote = null; ipsetCurrent = false; ipsetNote = "истекло время ожидания; повтори проверку"; }
            catch (Exception e) { ipsetRemote = null; ipsetCurrent = false; ipsetNote = "не удалось проверить: " + e.Message; AddLog(ipsetNote); }
        }
        async Task UpdateHosts(CancellationToken token)
        {
            Engine.RequireAdmin(); await CheckHosts(token);
            if (hostsPlan == null) throw new IOException(hostsNote);
            if (hostsPlan.Current) { Save(); RefreshUpdates(); return; }
            if (MessageBox.Show(Window, "Будет добавлено доменов: " + hostsPlan.Added + ".\nБудут заменены адреса доменов: " + hostsPlan.Changed + ".\nУдалено устаревших доменов из блока приложения: " + hostsPlan.Removed + ".\n\n" + (settings.IncludeDnsHosts ? "Включены официальные адреса Secure DNS.\n" : "") + "Остальные записи сохранятся. Перед записью создаётся резервная копия. Применить обновление?", "Обновление hosts", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { RefreshUpdates(); return; }
            token.ThrowIfCancellationRequested();
            var receipt = HostsUpdate.Apply(hostsPlan, BackupDirectory);
            if (receipt != null) { settings.HostsReceipt = receipt; AddLog("Hosts обновлён. Резервная копия: " + receipt.Backup); }
            settings.HostsCheckedAt = DateTimeOffset.Now.ToString("o"); Save();
            hostsPlan = HostsUpdate.Plan(HostsUpdate.SystemPath, SecureDns.HostsSource(hostsRemote,settings.IncludeDnsHosts)); hostsNote = "обновлён";
            try { await Engine.RunTool("ipconfig.exe", new[] { "/flushdns" }, CancellationToken.None); }
            catch (Exception e) { AddLog("Hosts сохранён, очистка DNS не выполнена: " + e.Message); hostsNote += "; перезапусти браузер"; }
            RefreshUpdates();
        }
        async Task InstallIpSet(CancellationToken token)
        {
            NeedBundle(); EnsureStopped(true);
            await CheckIpSet(token); if (ipsetRemote == null) throw new IOException(ipsetNote);
            NeedBundle();
            if (!ipsetCurrent) {
                token.ThrowIfCancellationRequested(); EnsureStopped(true);
                string file = Path.Combine(Root, "lists", "ipset-all.txt"); if (Bundle.IpMode(Root) != "loaded") file += ".backup";
                Bundle.WriteBackup(file, ipsetRemote); AddLog("IPSet обновлён. Режим фильтра сохранён.");
            }
            ipsetCurrent = true; ipsetNote = "актуален"; settings.IpsetCheckedAt = DateTimeOffset.Now.ToString("o"); Save(); RefreshUpdates();
        }
        async Task RestoreHosts(CancellationToken token)
        {
            Engine.RequireAdmin();
            if (MessageBox.Show(Window, "Восстановить hosts из резервной копии последнего обновления?", "Откат hosts", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            HostsUpdate.Restore(HostsUpdate.SystemPath, settings.HostsReceipt, BackupDirectory); settings.HostsReceipt = null; settings.HostsCheckedAt = null; Save();
            hostsPlan = null; hostsNote = "восстановлен из копии";
            try { await Engine.RunTool("ipconfig.exe", new[] { "/flushdns" }, CancellationToken.None); } catch (Exception e) { AddLog("Не удалось очистить DNS: " + e.Message); }
            RefreshUpdates();
        }
        void RefreshUpdates() { if (setupVisible) RenderSetup(); else if (currentPage == "Settings") Navigate("Settings"); }
        void HostsGuide()
        {
            ShowReport("Как обновить hosts", "1. Открой настройки приложения.\n\n2. Нажми «Проверить актуальность». Приложение скачает список Flowseal и добавит адреса DNS-сервисов, если включён переключатель «Адреса Secure DNS в hosts». Затем сравнит записи доменов.\n\n3. Если нужно обновление, при желании открой предпросмотр. Нажми «Обновить hosts» и подтверди изменения.\n\n4. Приложение сохранит резервную копию, обновит только соответствующие домены и очистит DNS-кэш.\n\n5. Перезапусти браузер и подключи Zapret.\n\nЕсли что-то перестало работать, нажми «Восстановить hosts из копии». Автооткат доступен, пока hosts не менялся после обновления. Копии хранятся в папке данных приложения, в backups.\n\nСистемный файл: " + HostsUpdate.SystemPath + "\n\nИсточник: " + HostsUpdate.Url);
        }
    }
}
