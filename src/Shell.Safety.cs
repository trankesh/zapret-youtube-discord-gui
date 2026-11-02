using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZapretDesktop
{
    internal sealed class SweepInterlock
    {
        public bool Active { get; private set; }
        public bool Cancelling { get; private set; }
        public void Begin() { if (Active) throw new InvalidOperationException("Подбор уже идёт."); Active=true; Cancelling=false; }
        public bool Cancel() { if (!Active || Cancelling) return false; Cancelling=true; return true; }
        public void End() { Active=false; Cancelling=false; }
    }
    public sealed partial class Shell
    {
        readonly SweepInterlock sweepLock=new SweepInterlock();
        readonly Dictionary<UIElement,bool> lockedElements=new Dictionary<UIElement,bool>();
        Border sweepOverlay;
        TextBlock sweepOverlayProgress;
        Button sweepOverlayCancel;
        string bundleProblem;
        string notifiedBundleRoot;
        bool SweepLocked { get { return sweepLock.Active; } }
        void LockSweep()
        {
            sweepLock.Begin();
            var root=(Grid)Window.Content;
            foreach (UIElement child in root.Children) { lockedElements[child]=child.IsEnabled; child.IsEnabled=false; }
            sweepOverlayProgress=Text("Подготавливаем проверку…",16,Accent);
            sweepOverlayCancel=new Button { Content="Отменить подбор",Style=(Style)Window.FindResource("Primary"),HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,18,0,0),IsDefault=true };
            sweepOverlayCancel.Click += (s,e)=>CancelSweep();
            var body=Stack(Text("Подбираем стратегию",23,null),new Border {Height=14},sweepOverlayProgress,new Border {Height=14},Text("Приложение по очереди проверяет варианты подключения. Настройки и другие действия временно недоступны, чтобы не мешать проверке.",15,Muted),sweepOverlayCancel, new Border {Height=10},Text("Можно отменить проверку кнопкой или клавишей Esc. Перед закрытием дождись завершения отмены.",14,Muted));
            var panel=Card(body); panel.Padding=new Thickness(22); panel.Margin=new Thickness(20); panel.VerticalAlignment=VerticalAlignment.Center;
            sweepOverlay=new Border { Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(238,11,11,13)),Child=panel };
            Grid.SetRowSpan(sweepOverlay,root.RowDefinitions.Count); Panel.SetZIndex(sweepOverlay,1000); root.Children.Add(sweepOverlay); sweepOverlayCancel.Focus();
        }
        void UnlockSweep()
        {
            if (sweepOverlay!=null) ((Grid)Window.Content).Children.Remove(sweepOverlay);
            foreach (var pair in lockedElements) pair.Key.IsEnabled=pair.Value;
            lockedElements.Clear(); sweepOverlay=null; sweepOverlayCancel=null; sweepOverlayProgress=null; sweepLock.End();
        }
        void CancelSweep()
        {
            if (!sweepLock.Cancel()) return;
            if (sweepOverlayCancel!=null) { sweepOverlayCancel.IsEnabled=false; sweepOverlayCancel.Content="Завершаем проверку…"; }
            if (sweepOverlayProgress!=null) sweepOverlayProgress.Text="Сохраняем результаты и возвращаем прежнее подключение. Подожди немного.";
            if (operation!=null) operation.Cancel();
        }
        void SweepKey(object sender,KeyEventArgs e)
        {
            if (!SweepLocked) return;
            if (e.Key==Key.Tab) return;
            if (e.Key==Key.Escape || e.Key==Key.Enter || e.Key==Key.Space) CancelSweep();
            e.Handled=true;
        }
        void CheckBundleLocation()
        {
            if (preview) return;
            string previous=bundleProblem; bundleProblem=Bundle.RuntimeProblem(Root);
            if (bundleProblem==null) { notifiedBundleRoot=null; if (previous!=null && setupVisible && setupStep==1 && !busy) { Reload(); RenderSetup(); } return; }
            if (busy) { if (operation!=null) { if (SweepLocked) CancelSweep(); else operation.Cancel(); } return; }
            if (Window.OwnedWindows.Cast<Window>().Any(w=>w.IsVisible)) return;
            if (!setupVisible || setupStep!=1) StartSetup();
            if (!Window.IsVisible && tray!=null && notifiedBundleRoot!=Root) tray.ShowBalloonTip(6000,"Выбери набор Flowseal","Папка набора недоступна. Открой Zapret и укажи её новое расположение.",System.Windows.Forms.ToolTipIcon.Warning);
            notifiedBundleRoot=Root;
        }
        public void PreviewSweepLock(bool enabled)
        {
            if (!preview) throw new InvalidOperationException();
            if (enabled) {
                Navigate("Tests"); LockSweep(); sweepOverlayProgress.Text="Пример: проверяем вариант 2 из 22";
                Navigate("Settings");
                if (currentPage!="Tests" || Find<Button>("NavSettings").IsEnabled || !sweepOverlayCancel.IsEnabled) throw new InvalidOperationException("Блокировка интерфейса не сработала.");
            } else { CancelSweep(); CancelSweep(); if (!SweepLocked || !sweepLock.Cancelling || sweepOverlayCancel.IsEnabled || Find<Button>("NavSettings").IsEnabled) throw new InvalidOperationException("Отмена слишком рано разблокировала интерфейс."); UnlockSweep(); if (!Find<Button>("NavSettings").IsEnabled) throw new InvalidOperationException("Интерфейс не разблокирован."); }
        }
        public void PreviewSetupSafety()
        {
            if (!preview) throw new InvalidOperationException();
            int version=settings.SetupVersion;
            foreach (int step in new[]{1,2,3,4}) {
                setupVisible=true; setupStep=step; FinishSetup(false);
                if (setupVisible || currentPage!="Home" || busy) throw new InvalidOperationException("Необязательные шаги не удалось пропустить.");
            }
            settings.SetupVersion=version;
            string previous=settings.Root;
            settings.Root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Zapret-missing-example-"+Guid.NewGuid().ToString("N"));
            bundleProblem=Bundle.RuntimeProblem(Root); setupVisible=true; setupStep=1; RenderSetup();
            bool refused=false; try { FinishSetup(false); } catch (InvalidOperationException) { refused=true; }
            if (!refused || !setupVisible) throw new InvalidOperationException("Удалось пропустить обязательный выбор набора.");
            settings.Root=previous; bundleProblem=null;
            // Keep the missing-folder screen for capture; resume normal preview
            // when PreviewSweepLock is called next.
            setupVisible=false;
        }
    }
}
