using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ZapretDesktop
{
    internal static class MouseScrolling
    {
        sealed class State
        {
            public State() { }
            public bool Pressed, Dragging;
            public Point Origin;
            public double Offset, Pull;
            public ListBoxItem ClickItem;
            public readonly Stopwatch Clock = new Stopwatch();
            public Action<double> Pulling;
            public Action<double, long> Released;
        }
        static readonly ConditionalWeakTable<ScrollViewer, State> States = new ConditionalWeakTable<ScrollViewer, State>();
        static bool initialized;
        public static void Initialize()
        {
            if (initialized) return; initialized = true;
            EventManager.RegisterClassHandler(typeof(ScrollViewer), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down), true);
            EventManager.RegisterClassHandler(typeof(ScrollViewer), Mouse.PreviewMouseMoveEvent, new MouseEventHandler(Move), true);
            EventManager.RegisterClassHandler(typeof(ScrollViewer), Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Up), true);
            EventManager.RegisterClassHandler(typeof(ScrollViewer), Mouse.LostMouseCaptureEvent, new MouseEventHandler(Lost), true);
        }
        public static void Sheet(ScrollViewer viewer, Action<double> pulling, Action<double, long> released)
        {
            var state = States.GetOrCreateValue(viewer); state.Pulling = pulling; state.Released = released;
        }
        static DependencyObject Parent(DependencyObject node)
        {
            if (node is Visual || node is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(node);
            var content = node as FrameworkContentElement; return content == null ? null : content.Parent;
        }
        static Point Position(MouseEventArgs e, ScrollViewer viewer) { return e.GetPosition((IInputElement)Window.GetWindow(viewer) ?? viewer); }
        static void Down(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.Handled) return;
            var viewer = (ScrollViewer)sender; bool button = false; ListBoxItem item = null;
            ScrollViewer nearest = null;
            for (var node = e.OriginalSource as DependencyObject; node != null; node = Parent(node)) {
                if (node is TextBoxBase || node is PasswordBox || node is ComboBox || node is ComboBoxItem || node is CheckBox || node is RadioButton || node is Thumb) return;
                if (node is ButtonBase) button = true;
                if (node is ListBoxItem && item == null) item = (ListBoxItem)node;
                if (node is ScrollViewer && nearest == null) nearest = (ScrollViewer)node;
            }
            if (nearest != viewer) return;
            var state = States.GetOrCreateValue(viewer);
            if (viewer.ScrollableHeight <= 0 && state.Pulling == null) return;
            if (item != null && Keyboard.Modifiers != ModifierKeys.None) return;
            state.Pressed = true; state.Dragging = false; state.Origin = Position(e, viewer); state.Offset = viewer.VerticalOffset; state.Pull = 0; state.ClickItem = item; state.Clock.Restart();
            if (!button) viewer.CaptureMouse();
            if (item != null) e.Handled = true;
        }
        static void Move(object sender, MouseEventArgs e)
        {
            var viewer = (ScrollViewer)sender; State state;
            if (!States.TryGetValue(viewer, out state) || !state.Pressed || e.LeftButton != MouseButtonState.Pressed) return;
            var point = Position(e, viewer); double dy = point.Y - state.Origin.Y;
            if (!state.Dragging) {
                if (Math.Abs(dy) < 6 || Math.Abs(dy) < Math.Abs(point.X - state.Origin.X)) return;
                state.Dragging = true; viewer.CaptureMouse();
            }
            viewer.ScrollToVerticalOffset(Math.Max(0, state.Offset - dy));
            state.Pull = state.Pulling == null ? 0 : GestureMath.Pull(dy, state.Offset);
            if (state.Pulling != null) state.Pulling(state.Pull);
            e.Handled = true;
        }
        static void Up(object sender, MouseButtonEventArgs e)
        {
            var viewer = (ScrollViewer)sender; State state;
            if (e.ChangedButton != MouseButton.Left || !States.TryGetValue(viewer, out state) || !state.Pressed) return;
            bool dragged = state.Dragging; state.Pressed = false; state.Dragging = false;
            if (Mouse.Captured == viewer) viewer.ReleaseMouseCapture();
            if (!dragged) { if (state.ClickItem != null) { state.ClickItem.IsSelected = true; state.ClickItem.Focus(); e.Handled = true; } return; }
            e.Handled = true;
            if (state.Released != null) state.Released(state.Pull, state.Clock.ElapsedMilliseconds);
        }
        static void Lost(object sender, MouseEventArgs e)
        {
            var viewer = (ScrollViewer)sender; State state;
            if (e.OriginalSource != viewer || !States.TryGetValue(viewer, out state) || !state.Pressed || Mouse.Captured == viewer) return;
            bool dragged = state.Dragging; state.Pressed = false; state.Dragging = false;
            if (dragged && state.Released != null) state.Released(0, 0);
        }
    }
}
