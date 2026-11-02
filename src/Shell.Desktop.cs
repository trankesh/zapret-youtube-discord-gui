using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace ZapretDesktop
{
    public sealed partial class Shell
    {
        Forms.NotifyIcon tray;
        Forms.ContextMenuStrip trayMenu;
        Icon trayIcon;
        bool forceExit, sessionEnding, windowClosed, exitPending, startInTray, trayHintShown;
        void InitializeTray(bool visible = true)
        {
            Window.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Zapret;component/icons/app.ico"));
            trayIcon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location) ?? (Icon)SystemIcons.Application.Clone();
            trayMenu = new Forms.ContextMenuStrip { Renderer = new TrayRenderer(), ShowImageMargin = false, BackColor = System.Drawing.Color.FromArgb(21, 21, 24), ForeColor = System.Drawing.Color.FromArgb(230, 230, 237) };
            var service = new Forms.ToolStripMenuItem { Enabled = false };
            var state = new Forms.ToolStripMenuItem { Enabled = false };
            var config = new Forms.ToolStripMenuItem { Enabled = false };
            trayMenu.Items.Add(service); trayMenu.Items.Add(state); trayMenu.Items.Add(config); trayMenu.Items.Add(new Forms.ToolStripSeparator());
            var open = new Forms.ToolStripMenuItem("Открыть Zapret"); open.Click += (s, e) => ShowFromTray(); trayMenu.Items.Add(open);
            var cancelSweep = new Forms.ToolStripMenuItem("Отменить автоподбор"); cancelSweep.Click += (s,e)=>CancelSweep(); trayMenu.Items.Add(cancelSweep);
            var stop = new Forms.ToolStripMenuItem("Выключить обход"); stop.Click += async (s, e) => await Guard(async token => {
                if (engine.Running) engine.Stop();
                else if (Engine.ServiceState() == "Running") { await Engine.ServiceAction(Root, "stop", token); AddLog("Служба остановлена из трея."); }
            }, true); trayMenu.Items.Add(stop);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            var exit = new Forms.ToolStripMenuItem("Выйти из приложения"); exit.Click += async (s, e) => await ExitApplication(); trayMenu.Items.Add(exit);
            trayMenu.Opening += (s, e) => {
                string status = Engine.ServiceState(); bool active = engine.Running || status == "Running";
                service.Text = "Служба: " + ServiceLabel(status);
                state.Text = "Обход: " + (active ? "работает" : Engine.AnyWinws() ? "внешний процесс" : "выключен");
                string strategy = engine.Running ? engine.Active.Name : selected == null ? "не выбран" : selected.Name;
                if (status == "Running") using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret")) {
                    if (key != null) strategy = Convert.ToString(key.GetValue("zapret-discord-youtube", strategy));
                }
                config.Text = "Конфиг: " + strategy;
                stop.Enabled = !busy && (engine.Running || status == "Running" && !String.IsNullOrWhiteSpace(Root) && Engine.ServiceMatches(Root));
                exit.Enabled = !exitPending && !SweepLocked;
                cancelSweep.Visible=SweepLocked; cancelSweep.Enabled=SweepLocked && !sweepLock.Cancelling;
                open.Text=SweepLocked ? "Показать автоподбор" : "Открыть Zapret";
                string tip = "Zapret · " + (active ? "работает" : "выключен") + " · " + strategy; tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            };
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Zapret", ContextMenuStrip = trayMenu, Visible = visible };
            tray.DoubleClick += (s, e) => ShowFromTray();
            Window.StateChanged += (s, e) => { if (SweepLocked && Window.WindowState!=WindowState.Normal) { Window.WindowState=WindowState.Normal; return; } if (Window.WindowState == WindowState.Minimized && !setupVisible) HideToTray(); };
            Window.Loaded += (s, e) => { if (startInTray && !setupVisible) HideToTray(); };
            Application.Current.SessionEnding += (s, e) => { sessionEnding = true; forceExit = true; if (operation != null) operation.Cancel(); };
        }
        public void PreviewTray()
        {
            if (!preview) throw new InvalidOperationException();
            InitializeTray(false);
            if (tray.Icon == null || trayMenu.Items.Count < 8) throw new InvalidOperationException("Не удалось создать иконку и меню трея.");
        }
        public void ShowFromTray()
        {
            if (!Window.Dispatcher.CheckAccess()) { Window.Dispatcher.BeginInvoke(new Action(ShowFromTray)); return; }
            if (windowClosed) return;
            startInTray = false; Window.Show(); Window.WindowState = WindowState.Normal; Window.Activate();
        }
        void HideToTray()
        {
            if (SweepLocked) return;
            if (tray == null) return;
            Window.Hide(); Save();
            if (!trayHintShown && !startInTray) { tray.ShowBalloonTip(2500, "Zapret в трее", "Двойной щелчок — открыть. Правая кнопка — статус и управление.", Forms.ToolTipIcon.Info); trayHintShown = true; }
        }
        async Task ExitApplication()
        {
            if (SweepLocked && !sessionEnding) { ShowFromTray(); return; }
            if (exitPending || preview) return;
            exitPending = true;
            try {
                if (operation != null) operation.Cancel();
                for (int i = 0; busy && i < 300; i++) await Task.Delay(100);
                if (busy) { ShowFromTray(); MessageBox.Show(Window, "Операция ещё завершается. Повторите выход через несколько секунд.", "Zapret"); return; }
                if (leaveEditor != null) ShowFromTray();
                forceExit = true; Window.Close();
                if (!windowClosed) forceExit = false;
            }
            finally { exitPending = false; }
        }
        void DisposeTray()
        {
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            if (trayMenu != null) { trayMenu.Dispose(); trayMenu = null; }
            if (trayIcon != null) { trayIcon.Dispose(); trayIcon = null; }
        }
        sealed class TrayColors : Forms.ProfessionalColorTable
        {
            public override System.Drawing.Color ToolStripDropDownBackground { get { return System.Drawing.Color.FromArgb(21, 21, 24); } }
            public override System.Drawing.Color MenuBorder { get { return System.Drawing.Color.FromArgb(54, 54, 62); } }
            public override System.Drawing.Color MenuItemSelected { get { return System.Drawing.Color.FromArgb(48, 48, 55); } }
            public override System.Drawing.Color MenuItemBorder { get { return System.Drawing.Color.FromArgb(64, 64, 72); } }
            public override System.Drawing.Color SeparatorDark { get { return System.Drawing.Color.FromArgb(54, 54, 62); } }
            public override System.Drawing.Color SeparatorLight { get { return System.Drawing.Color.FromArgb(54, 54, 62); } }
        }
        sealed class TrayRenderer : Forms.ToolStripProfessionalRenderer
        {
            public TrayRenderer() : base(new TrayColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? System.Drawing.Color.FromArgb(230, 230, 237) : System.Drawing.Color.FromArgb(160, 160, 172);
                base.OnRenderItemText(e);
            }
        }
    }
}
