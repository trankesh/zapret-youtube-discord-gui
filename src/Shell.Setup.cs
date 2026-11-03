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
        CancellationTokenSource setupCheck;
        bool checkingLists;
        void StopSetupCheck() { if (setupCheck!=null) setupCheck.Cancel(); }
        void SetupStep(int step)
        {
            StopSetupCheck(); setupStep=step; RenderSetup();
            if (step==2 && !preview) { var ignored=CheckSetupLists(); }
        }
        async Task CheckSetupLists()
        {
            var check=new CancellationTokenSource(); setupCheck=check;
            try { await CheckPrerequisites(check.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { AddLog("Проверка списков: "+error.Message); }
            finally { if (setupCheck==check) { setupCheck=null; RefreshUpdates(); } check.Dispose(); }
        }
        void RenderSetup()
        {
            actions.Clear(); leaveEditor=null; gameToggle=null; gameCaption=null; testResults=null; testProgress=null; serviceState=null; logItems=null; connect=null; installedVersionText=null;
            strategyOverlay=null; homeRoot=null; currentPage="Setup";
            ((RowDefinition)Window.FindName("HeaderRow")).Height=new GridLength(58);
            ((RowDefinition)Window.FindName("FooterRow")).Height=new GridLength(0);
            ((RowDefinition)Window.FindName("NavigationRow")).Height=new GridLength(0);
            Find<Grid>("HeaderBar").Visibility=Visibility.Visible; Find<StackPanel>("PageHeader").Visibility=Visibility.Visible;
            Find<Border>("BottomNavigation").Visibility=Visibility.Collapsed; Find<TextBlock>("StatusLine").Visibility=Visibility.Collapsed;
            Find<Grid>("PageLayout").Margin=new Thickness(20,6,20,16);
            Find<TextBlock>("PageTitle").Text="Подготовка Zapret";
            Find<TextBlock>("PageSubtitle").Text="Шаг "+setupStep+" из 4 · "+(setupStep==1 ? "Набор Zapret" : setupStep==2 ? "Обновление списков" : setupStep==3 ? "Защищённый DNS" : "Всё готово к запуску");
            var content=new StackPanel(); var navigation=new StackPanel();
            if (setupStep==1) {
                content.Children.Add(HelpHeader("ПОДКЛЮЧИМ НАБОР",BundleGuide));
                content.Children.Add(Text("Используй встроенный набор или выбери папку со скачанным Flowseal.",16,null)); Gap(content,18);
                if (bundleProblem!=null && !String.IsNullOrWhiteSpace(Root)) {
                    content.Children.Add(Card(Stack(Text("Папка набора недоступна",17,null),new Border {Height=8},Text("Выбери новое расположение или встроенный набор.",14,Muted)))); Gap(content,14);
                    if (engine.Running || Engine.ServiceState()=="Running" && Engine.ServiceMatches(Root))
                        content.Children.Add(Wide("Отключить прежний Zapret",()=>{ var ignored=Guard(async token=>{ if(engine.Running) engine.Stop(); if(Engine.ServiceState()=="Running" && Engine.ServiceMatches(Root)) await Engine.ServiceAction(Root,"stop",token); RenderSetup(); },true); }));
                }
                content.Children.Add(Wide("Встроенный Flowseal "+EmbeddedBundle.Version,()=>{ var ignored=Guard(async token=>{ string root=await Task.Run(()=>EmbeddedBundle.Extract(data),token); token.ThrowIfCancellationRequested(); SetSetupRoot(root); },true); },!Ready));
                content.Children.Add(Wide("Выбрать папку…",SelectSetupFolder));
                content.Children.Add(Wide("Скачать свежий Flowseal ↗",OpenFlowsealReleases)); Gap(content,8);
                if (Ready) {
                    var location=Text(Root,14,Muted); location.TextWrapping=TextWrapping.NoWrap; location.TextTrimming=TextTrimming.CharacterEllipsis; location.ToolTip=Root;
                    content.Children.Add(Card(Stack(Text("Набор выбран · Flowseal "+Bundle.Version(Root),16,Accent),new Border {Height=8},location)));
                    navigation.Children.Add(Wide("Далее",()=>{ Bundle.ValidateForSetup(Root); SetupStep(2); },true));
                    navigation.Children.Add(Wide("Пропустить остальные шаги",()=>FinishSetup(false)));
                } else content.Children.Add(Text("Выбор набора обязателен. Остальную настройку можно пропустить.",14,Muted));
            } else if (setupStep==2) {
                content.Children.Add(Text("Обновим адреса сайтов",21,null)); Gap(content,10);
                content.Children.Add(Text("Списки проверяются автоматически. Обновление можно пропустить.",14,Muted)); Gap(content,16);
                content.Children.Add(UpdateCard());
                bool updates=hostsPlan!=null && !hostsPlan.Current || ipsetRemote!=null && !ipsetCurrent;
                navigation.Children.Add(Wide(checkingLists || updates ? "Пропустить" : "Далее",()=>SetupStep(3),!checkingLists && !updates));
                navigation.Children.Add(Wide("Назад",()=>SetupStep(1)));
            } else if (setupStep==3) {
                content.Children.Add(Text("Защитим DNS-запросы",21,null)); Gap(content,10);
                content.Children.Add(Text("Это помогает против подмены адресов сайтов. Можно настроить сейчас или позже в настройках.",14,Muted)); Gap(content,16);
                content.Children.Add(DnsCard());
                navigation.Children.Add(Wide("Далее",()=>SetupStep(4),true));
                navigation.Children.Add(Wide("Назад",()=>SetupStep(2)));
            } else {
                content.Children.Add(Text("Zapret готов к запуску",23,null)); Gap(content,12);
                content.Children.Add(Text("Теперь найдём стратегию, которая подходит твоему подключению. Автоподбор проверит варианты и покажет результат.",16,null)); Gap(content,18);
                content.Children.Add(Card(Stack(Text("Flowseal "+(Ready ? Bundle.Version(Root) : "не выбран"),17,null),new Border {Height=10},Text("Необязательные настройки доступны в любое время в разделе «Настройки».",14,Muted))));
                navigation.Children.Add(Wide("Подобрать стратегию",()=>FinishSetup(true),true));
                navigation.Children.Add(Wide("Пропустить и открыть главную",()=>FinishSetup(false)));
                navigation.Children.Add(Wide("Назад",()=>SetupStep(3)));
            }
            var layout=new Grid(); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            layout.Children.Add(Scroll(content)); navigation.Margin=new Thickness(0,16,0,0); Grid.SetRow(navigation,1); layout.Children.Add(navigation);
            Find<ContentControl>("Page").Content=layout;
            if(busy) foreach(var button in actions) button.IsEnabled=false;
        }
        void OpenFlowsealReleases() { System.Diagnostics.Process.Start("https://github.com/Flowseal/zapret-discord-youtube/releases/latest"); }
        void FinishSetup(bool tests)
        {
            StopSetupCheck(); NeedBundle(); Bundle.ValidateForSetup(Root); settings.SetupVersion = 1; Save(); setupVisible = false; Navigate(tests ? "Tests" : "Home");
        }
        void SetSetupRoot(string root)
        {
            StopSetupCheck(); Bundle.ValidateForSetup(root);
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
            var content=Stack(HelpHeader("HOSTS И IPSET",HostsGuide),Text("Hosts: "+hostsNote,14,Muted),new Border {Height=8},Text("IPSet: "+ipsetNote,14,Muted)); Gap(content,16);
            var dnsHosts=new CheckBox {IsChecked=settings.IncludeDnsHosts,IsEnabled=!checkingLists};
            dnsHosts.Click+=(s,e)=>{ settings.IncludeDnsHosts=dnsHosts.IsChecked==true; hostsPlan=hostsRemote==null ? null : HostsUpdate.Plan(HostsUpdate.SystemPath,SecureDns.HostsSource(hostsRemote,settings.IncludeDnsHosts)); hostsNote=hostsPlan==null ? "ещё не проверяли" : hostsPlan.Current ? "актуален" : "доступно обновление"; Save(); RefreshUpdates(); };
            var dnsRow=(FrameworkElement)ToggleRow("Адреса DNS в hosts","Добавляются при обновлении списков.",dnsHosts); dnsRow.Margin=new Thickness(0,0,0,6); content.Children.Add(dnsRow); Gap(content,12);
            bool updates=hostsPlan!=null && !hostsPlan.Current || ipsetRemote!=null && !ipsetCurrent;
            var update=Wide("Обновить hosts и IPSet",()=>{ var ignored=Guard(UpdateLists,true); },updates && !checkingLists);
            update.IsEnabled=!checkingLists; content.Children.Add(update);
            var check=Wide(checkingLists ? "Проверяем обновления…" : "Проверить снова",()=>{ var ignored=Guard(CheckPrerequisites,false); }); check.IsEnabled=!checkingLists; content.Children.Add(check);
            var card=Card(content); card.Padding=new Thickness(16); return card;
        }
        int listCheckGeneration;
        async Task CheckPrerequisites(CancellationToken token)
        {
            NeedBundle(); int generation=++listCheckGeneration; checkingLists=true; hostsNote="Проверяем…"; ipsetNote="Проверяем…"; RefreshUpdates();
            try { await Task.WhenAll(CheckHosts(token),CheckIpSet(token)); token.ThrowIfCancellationRequested(); Save(); }
            finally { if(generation==listCheckGeneration) { checkingLists=false; RefreshUpdates(); } }
        }
        async Task CheckHosts(CancellationToken token)
        {
            try {
                string downloaded=await HostsUpdate.Download(HostsUpdate.Url,token); token.ThrowIfCancellationRequested(); hostsRemote=downloaded; hostsPlan = HostsUpdate.Plan(HostsUpdate.SystemPath, SecureDns.HostsSource(hostsRemote,settings.IncludeDnsHosts));
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
                string downloaded=await HostsUpdate.Download(IpsetUrl,token); token.ThrowIfCancellationRequested(); ipsetRemote=downloaded; string expected = CanonicalIpSet(ipsetRemote);
                string file = Path.Combine(Root, "lists", "ipset-all.txt"); if (Bundle.IpMode(Root) != "loaded") file += ".backup";
                ipsetCurrent = false;
                if (File.Exists(file)) { try { ipsetCurrent = expected == CanonicalIpSet(File.ReadAllText(file)); } catch (InvalidDataException) { } }
                ipsetNote = ipsetCurrent ? "актуален" : "доступен свежий список";
                if (ipsetCurrent) settings.IpsetCheckedAt = DateTimeOffset.Now.ToString("o");
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); ipsetRemote = null; ipsetCurrent = false; ipsetNote = "истекло время ожидания; повтори проверку"; }
            catch (Exception e) { ipsetRemote = null; ipsetCurrent = false; ipsetNote = "не удалось проверить: " + e.Message; AddLog(ipsetNote); }
        }
        async Task InstallIpSet(CancellationToken token)
        {
            NeedBundle(); EnsureStopped(true); await CheckIpSet(token);
            if(ipsetRemote==null) throw new IOException(ipsetNote);
            token.ThrowIfCancellationRequested(); NeedBundle(); EnsureStopped(true);
            if(!ipsetCurrent) { string file=Path.Combine(Root,"lists","ipset-all.txt"); if(Bundle.IpMode(Root)!="loaded") file+=".backup"; Bundle.WriteBackup(file,ipsetRemote); }
            ipsetCurrent=true; ipsetNote="актуален"; settings.IpsetCheckedAt=DateTimeOffset.Now.ToString("o"); Save(); RefreshUpdates();
        }
        async Task UpdateLists(CancellationToken token)
        {
            Engine.RequireAdmin(); NeedBundle(); await CheckPrerequisites(token);
            if(hostsPlan==null || ipsetRemote==null) throw new IOException("Не удалось получить оба списка. Проверь подключение и попробуй снова. Текущие файлы не изменены.");
            bool updateHosts=!hostsPlan.Current, updateIpset=!ipsetCurrent;
            if(!updateHosts && !updateIpset) return;
            if(updateIpset) EnsureStopped(true);
            if(updateHosts && MessageBox.Show(Window,"Обновить списки адресов?\n\nHosts: добавить доменов "+hostsPlan.Added+", изменить "+hostsPlan.Changed+", убрать устаревших "+hostsPlan.Removed+".\nIPSet: "+(updateIpset ? "обновить" : "уже актуален")+".\n\nДругие записи hosts сохранятся. Перед записью будет создана резервная копия.","Обновление hosts и IPSet",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
            token.ThrowIfCancellationRequested(); NeedBundle();
            if(updateIpset) EnsureStopped(true);
            if(updateHosts) {
                var receipt=HostsUpdate.Apply(hostsPlan,BackupDirectory);
                if(receipt!=null) settings.HostsReceipt=receipt;
                hostsPlan=HostsUpdate.Plan(HostsUpdate.SystemPath,SecureDns.HostsSource(hostsRemote,settings.IncludeDnsHosts));
                hostsNote="актуален"; settings.HostsCheckedAt=DateTimeOffset.Now.ToString("o"); Save();
            }
            if(updateIpset) {
                string file=Path.Combine(Root,"lists","ipset-all.txt"); if(Bundle.IpMode(Root)!="loaded") file+=".backup";
                Bundle.WriteBackup(file,ipsetRemote); ipsetCurrent=true; ipsetNote="актуален"; settings.IpsetCheckedAt=DateTimeOffset.Now.ToString("o"); Save();
            }
            if(updateHosts) try { await Engine.RunTool("ipconfig.exe",new[]{"/flushdns"},CancellationToken.None); } catch(Exception error) { AddLog("Списки сохранены. Перезапусти браузер: "+error.Message); }
            AddLog("Hosts и IPSet обновлены."); RefreshUpdates();
        }
        void RefreshUpdates() { if (setupVisible) { if(setupStep==2) RenderSetup(); } else if (currentPage == "Settings") Navigate("Settings"); }
        void HostsGuide()
        {
            ShowReport("Hosts и IPSet","Hosts помогает Windows находить адреса нужных сайтов. IPSet — список адресов для обхода.\n\nВ мастере проверка начинается автоматически. В настройках нажми «Проверить снова».\n\nЕсли есть обновления, нажми «Обновить hosts и IPSet». Перед обновлением отключи Zapret. Изменения hosts потребуют подтверждения. Остальные записи сохранятся.\n\nКопия hosts сохраняется в папке данных приложения, в backups. После обновления перезапусти браузер.\n\nАдреса Secure DNS в hosts помогают находить DNS-серверы, но сами не включают шифрование. Защищённый DNS настраивается отдельно.");
        }
    }
}
