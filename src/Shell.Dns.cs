using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ZapretDesktop
{
    public sealed partial class Shell
    {
        UIElement DnsCard()
        {
            return Card(Stack(HelpHeader("ЗАЩИЩЁННЫЙ DNS",DnsGuide), Text("Шифрует запросы адресов сайтов. Необязательная настройка.", 14, Muted), new Border { Height=14 }, Wide("Выбрать DNS-сервер", OpenDns)));
        }
        void OpenDns() { DnsSurface().ShowDialog(); }
        Window DnsSurface()
        {
            var content = new StackPanel();
            content.Children.Add(HelpHeader("НАСТРОЙКА DNS",DnsGuide));
            content.Children.Add(Text("Выбери подключение и провайдера. Изменения сохраняются в Windows и действуют после закрытия Zapret.",14,Muted)); Gap(content,16);
            content.Children.Add(Label("ПОДКЛЮЧЕНИЕ"));
            var adapters = new ComboBox { Margin=new Thickness(0,4,0,12) }; content.Children.Add(adapters);
            var current = Text("Читаем настройки Windows…",14,Muted); content.Children.Add(current); Gap(content,16);
            content.Children.Add(Label("ПРОВАЙДЕР"));
            var providers = new ComboBox { ItemsSource=SecureDns.Providers, SelectedItem=SecureDns.Providers.FirstOrDefault(p=>p.Id==settings.DnsProviderId) ?? SecureDns.Providers[0], Margin=new Thickness(0,4,0,12) }; content.Children.Add(providers);
            var details = new StackPanel(); content.Children.Add(details);
            var note = Text("",14,Muted); Gap(content,12); content.Children.Add(note); Gap(content,12);
            Window dialog = null; bool working=false; bool supported=false;
            Button apply=null, restore=null;
            Action update = () => {
                var provider = (DnsProvider)providers.SelectedItem; details.Children.Clear();
                details.Children.Add(Text(provider.Description,15,null)); Gap(details,8);
                details.Children.Add(Text(String.Join(" · ",provider.V4),14,Muted)); Gap(details,8);
                details.Children.Add(Text(provider.Template,14,Muted));
                if (apply != null) apply.IsEnabled=supported && adapters.SelectedItem != null && ((DnsAdapter)adapters.SelectedItem).DohSupported && !working;
                if (restore != null) restore.IsEnabled=!String.IsNullOrEmpty(settings.DnsBackup) && File.Exists(settings.DnsBackup) && !working;
            };
            adapters.SelectionChanged += (s,e) => { var adapter=adapters.SelectedItem as DnsAdapter; current.Text=adapter==null ? "Выбери активное подключение." : "Текущие DNS: " + String.Join(", ",adapter.Servers ?? new string[0])+"\nDoH на адаптере: "+(adapter.DohEnabled==true ? "включён" : "не подтверждён"); update(); };
            providers.SelectionChanged += (s,e) => update();
            Func<Func<Task>,Task> run = async job => {
                if (working || busy) return;
                working=true; busy=true; dialog.IsEnabled=false;
                try { await job(); }
                catch (Exception error) { note.Text="Не удалось завершить настройку: " + error.Message; AddLog("DNS: " + error.Message); }
                finally { working=false; busy=false; dialog.IsEnabled=true; update(); }
            };
            Func<Task> read = async () => {
                DnsState state = preview ? new DnsState { Doh=true, Adapters=new[]{new DnsAdapter { Guid="00000000-0000-0000-0000-000000000001", Name="Wi-Fi · пример", Servers=new[]{"192.168.1.1"},DohSupported=true,DohEnabled=false }} } : await SecureDns.Read(CancellationToken.None);
                var previous=adapters.SelectedItem as DnsAdapter;
                supported=state.Doh; adapters.ItemsSource=state.Adapters ?? new DnsAdapter[0];
                adapters.SelectedItem=previous==null ? null : (state.Adapters ?? new DnsAdapter[0]).FirstOrDefault(a=>a.Guid==previous.Guid);
                if (adapters.SelectedItem==null && adapters.Items.Count>0) adapters.SelectedIndex=0;
                note.Text=supported ? "Будет включён DoH без перехода на обычные незашифрованные запросы. Перед изменением сохраняется копия настроек." : "Системный DoH недоступен. Включи Secure DNS в браузере: инструкция открывается значком «?» вверху. Настройки Windows не будут изменены.";
                if (adapters.Items.Count==0) current.Text="Нет подключённых сетевых адаптеров. Подключись к Wi-Fi или Ethernet и обнови список.";
            };
            apply=Button("Применить DNS over HTTPS", () => { var ignored=run(async () => {
                if (preview) return;
                Engine.RequireAdmin(); var adapter=(DnsAdapter)adapters.SelectedItem; var provider=(DnsProvider)providers.SelectedItem;
                if (MessageBox.Show(dialog,"Подключение: " + adapter.Name + "\nПровайдер: " + provider.Name + "\n\nБудут изменены DNS для включённых IPv4/IPv6 этого подключения. Незашифрованные запросы будут отключены. Если сервер недоступен, сайты могут не открываться. Доступен откат последнего изменения.\n\nПрименить?","Системный DNS",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
                Directory.CreateDirectory(BackupDirectory);
                string previous=settings.DnsBackup; string backup=Path.Combine(BackupDirectory,"dns-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,8) + ".xml");
                string[] history=settings.DnsBackupHistory ?? new string[0];
                settings.DnsBackupHistory=String.IsNullOrEmpty(previous) || !File.Exists(previous) ? history : history.Concat(new[]{previous}).ToArray();
                settings.DnsBackup=backup; Save();
                try { await SecureDns.Execute("Apply",adapter.Guid,provider,backup,CancellationToken.None); settings.DnsProviderId=provider.Id; Save(); }
                catch { if (!File.Exists(backup)) { settings.DnsBackup=previous; settings.DnsBackupHistory=history; Save(); } throw; }
                await read(); note.Text="Настроен " + provider.Name + " · DNS over HTTPS. Перезапусти браузер. При проблемах используй откат ниже."; AddLog("DNS over HTTPS: " + provider.Name + " · " + adapter.Name);
            }); },true,false); apply.HorizontalAlignment=HorizontalAlignment.Stretch; content.Children.Add(apply);
            restore=Button("Откатить последнее изменение DNS", () => { var ignored=run(async () => {
                if (preview) return; Engine.RequireAdmin();
                if (MessageBox.Show(dialog,"Восстановить DNS выбранного при изменении подключения и прежние параметры DoH из последней копии?","Откат DNS",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                await SecureDns.Execute("Restore",null,null,settings.DnsBackup,CancellationToken.None);
                var history=settings.DnsBackupHistory ?? new string[0]; settings.DnsBackup=history.LastOrDefault(); settings.DnsBackupHistory=history.Take(Math.Max(0,history.Length-1)).ToArray();
                settings.DnsProviderId=null; Save(); await read(); note.Text="Прежние настройки DNS восстановлены."; AddLog(note.Text);
            }); },false,false); restore.HorizontalAlignment=HorizontalAlignment.Stretch; content.Children.Add(restore);
            content.Children.Add(Button("Обновить подключения", () => { var ignored=run(read); },false,false));
            dialog=Surface("Защищённый DNS",content,null);
            dialog.Closing += (s,e) => { if (working) e.Cancel=true; };
            dialog.Loaded += (s,e) => { var ignored=run(read); };
            update(); return dialog;
        }
        void DnsGuide()
        {
            var content=Stack(ReportCards("1. Открой «Выбрать DNS-сервер», выбери своё подключение Wi-Fi или Ethernet и провайдера. Если активны оба подключения, настрой их отдельно.\n\n2. Нажми «Применить DNS over HTTPS». Windows будет шифровать запросы для выбранных серверов. Доступность сайта или сервера это не гарантирует: блокировка IP или HTTPS может сохраниться.\n\n3. В hosts можно добавить адреса DNS-сервисов: включи «Адреса Secure DNS в hosts» при обновлении hosts. Это помогает получить адрес сервиса без обычного DNS, но само по себе не шифрует запросы и не обходит блокировку IP.\n\n4. На Windows 10 системный DoH через этот интерфейс недоступен. В Chrome/Edge открой настройки → конфиденциальность и безопасность → безопасность → защищённый DNS. В Firefox: настройки → приватность и защита → DNS через HTTPS. Выбери провайдера или введи его HTTPS-адрес. Это действует только в браузере.\n\n5. Перезапусти браузер. Если связь ухудшилась, попробуй другого провайдера или нажми «Откатить последнее изменение DNS». Для ручного возврата открой свойства подключения Windows и верни назначение DNS «Автоматически (DHCP)», если раньше использовался DHCP.\n\nВ рабочей сети или при VPN используй настройки администратора сети: публичный DNS может не знать внутренние домены. Политики Windows и отдельные DNS-настройки браузеров/VPN могут переопределять результат."));
            foreach (var provider in SecureDns.Providers) {
                var captured=provider;
                var card=Stack(Text(provider.Name,18,Accent),new Border{Height=8},Text(provider.Description,14,Muted),new Border{Height=8},Text("IPv4: " + String.Join(", ",provider.V4),14,null),new Border{Height=8},Text("IPv6: " + String.Join(", ",provider.V6),14,Muted),new Border{Height=8},Text(provider.Template,14,Muted));
                card.Children.Add(Button("Скопировать HTTPS-адрес",()=>Clipboard.SetText(captured.Template),false,false));
                card.Children.Add(Button("Документация провайдера ↗",()=>System.Diagnostics.Process.Start(captured.Source),false,false)); content.Children.Add(Card(card)); Gap(content,12);
            }
            Surface("Как настроить Secure DNS",content,null).ShowDialog();
        }
    }
}
