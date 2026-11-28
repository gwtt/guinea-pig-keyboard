using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing = System.Drawing;

namespace WheekKeys
{
    internal sealed class FriendDesktop : IDisposable
    {
        private readonly Window owner;
        private readonly FrameworkElement viewport;
        private readonly Dictionary<string, FriendWindow> windows = new Dictionary<string, FriendWindow>();
        private bool disposed;

        public FriendDesktop(Window owner) : this(owner, null) { }

        public FriendDesktop(Window owner, FrameworkElement viewport)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            this.owner = owner;
            this.viewport = viewport;
            owner.IsVisibleChanged += OwnerVisibilityChanged;
        }

        public void Update(LocalPeers network, double now, bool clickThrough, bool showLabels)
        {
            Update(network, now, clickThrough, showLabels, false);
        }

        internal void Update(LocalPeers network, double now, bool clickThrough, bool showLabels, bool altBypass)
        {
            if (disposed) return;
            foreach (string id in windows.Keys.Where(x => network == null || !network.Members.ContainsKey(x)).ToArray())
            { windows[id].Close(); windows.Remove(id); }
            if (network == null) return;
            foreach (var pair in network.Members)
            {
                FriendWindow view;
                if (windows.TryGetValue(pair.Key, out view) && view.IsClosed) windows.Remove(pair.Key);
                if (!windows.TryGetValue(pair.Key, out view))
                {
                    int slot = 0;
                    while (windows.Values.Any(w => w.Slot == slot)) slot++;
                    string id = pair.Key;
                    windows.Add(id, view = new FriendWindow(owner, viewport, slot,
                        delegate(string kind, double when) { return network.TrySendInteraction(id, kind, when); }));
                }
                view.SetPointerMode(clickThrough, altBypass);
                view.Update(pair.Value, now, showLabels);
                view.SetOwnerVisible(owner.IsVisible);
            }
        }

        internal void SetPointerMode(bool clickThrough, bool altBypass)
        {
            foreach (FriendWindow view in windows.Values) view.SetPointerMode(clickThrough, altBypass);
        }

        internal void ShowInteraction(string peerId, string kind)
        {
            FriendWindow view;
            if (windows.TryGetValue(peerId, out view) && !view.IsClosed) view.ShowInteraction(kind);
        }

        private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        { foreach (FriendWindow view in windows.Values) view.SetOwnerVisible(owner.IsVisible); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.IsVisibleChanged -= OwnerVisibilityChanged;
            foreach (FriendWindow view in windows.Values) view.Close();
            windows.Clear();
        }

        private sealed class FriendWindow
        {
            private const int ExStyle = -20, Transparent = 0x20, Tool = 0x80, NoActivate = 0x08000000;
            private const double LabelHeadroom = 14;
            [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="GetWindowLong")] private static extern int GetWindowLong(IntPtr hwnd, int index);
            [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="SetWindowLong")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
            private readonly Window window;
            private readonly FrameworkElement viewport;
            private readonly Viewbox box;
            private readonly PixelPetScene scene;
            private readonly TextBlock label;
            private readonly TranslateTransform labelPosition = new TranslateTransform();
            private readonly WindowPlacement placement;
            private readonly PetWindowOverlay overlay;
            private readonly Func<string, double, bool> send;
            private bool through, altHint, nextPoke = true;
            private double now;
            public readonly int Slot;
            public bool IsClosed { get; private set; }

            public FriendWindow(Window owner, FrameworkElement viewport, int slot, Func<string, double, bool> send)
            {
                this.viewport = viewport;
                this.send = send;
                Slot = slot;
                scene = new PixelPetScene(false);
                scene.ShowKeyLabels = true;
                box = new Viewbox { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom, Child = scene };
                label = new TextBlock { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(190, 50, 38, 26)),
                    FontSize = 12, Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center, RenderTransform = labelPosition };
                Grid panel = new Grid { Children = { box, label } };
                overlay = new PetWindowOverlay(panel, scene, 30);
                window = new Window { Owner = owner, Width = Math.Max(180, owner.Width), Height = Math.Max(150, owner.Width * 308.0 / 400.0 + LabelHeadroom),
                    WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
                    ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, Content = panel };
                window.SetBinding(Window.OpacityProperty, new System.Windows.Data.Binding("Opacity") { Source = owner });
                placement = new WindowPlacement(window, null, Interact);
                scene.HeadPositionChanged += PositionLabel;
                label.SizeChanged += delegate { PositionLabel(); };
                MatchSize();
                window.SourceInitialized += delegate { ApplyStyles(); };
                window.Closed += delegate { IsClosed = true; };
                window.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
                {
                    PetWindow pet = owner as PetWindow;
                    if (pet == null) return;
                    Point point = window.PointToScreen(e.GetPosition(window));
                    pet.OpenSettings(new Drawing.Point((int)point.X, (int)point.Y));
                    e.Handled = true;
                };
                PlaceNear(owner, slot);
            }

            private bool Interact()
            {
                string kind = scene.IsDozing || nextPoke ? "poke" : "feed";
                if (send(kind, now))
                {
                    nextPoke = kind != "poke";
                    ShowInteraction(kind);
                }
                else
                {
                    scene.MoveMouse(0.05, 0);
                    overlay.Show("互动暂未发送");
                }
                return true;
            }

            internal void ShowInteraction(string kind)
            {
                scene.TriggerHop();
                overlay.Show(kind == "feed" ? "送出一片草叶 ☘" : "轻轻戳了一下 ♡");
            }

            private void PlaceNear(Window owner, int slot)
            {
                Rect area = WindowPlacement.WorkArea(owner);
                double step = window.Width + 12;
                double left = owner.Left - step * (slot + 1), top = owner.Top - LabelHeadroom;
                if (left < area.Left) left = owner.Left + step * (slot + 1);
                if (left + window.Width > area.Right)
                {
                    int columns = Math.Max(1, (int)(area.Width / step));
                    left = area.Left + (slot % columns) * step;
                    top = owner.Top - (slot / columns + 1) * (window.Height + 10);
                }
                bool ignored;
                Point position = WindowPlacement.Calculate(new Rect(left, top, window.Width, window.Height), area, area, false, false, out ignored);
                window.Left = position.X;
                window.Top = position.Y;
            }

            private void ApplyStyles()
            {
                HwndSource source = PresentationSource.FromVisual(window) as HwndSource;
                if (source == null) return;
                int style = GetWindowLong(source.Handle, ExStyle) | Tool | NoActivate;
                SetWindowLong(source.Handle, ExStyle, through ? style | Transparent : style & ~Transparent);
            }

            internal void SetPointerMode(bool clickThrough, bool altBypass)
            {
                bool nextThrough = clickThrough && !altBypass;
                bool nextHint = clickThrough && altBypass;
                if (through == nextThrough && altHint == nextHint) return;
                through = nextThrough;
                altHint = nextHint;
                if (through) placement.StopDrag();
                overlay.SetAltHint(altHint);
                ApplyStyles();
            }

            public void Update(LocalPeers.Peer peer, double now, bool showLabels)
            {
                this.now = now;
                scene.Skin = peer.Skin;
                PetWindow pet = window.Owner as PetWindow;
                if (pet != null && scene.SleepSeconds != pet.SleepSeconds) scene.SleepSeconds = pet.SleepSeconds;
                if (scene.ShowKeyLabels != showLabels) scene.ShowKeyLabels = showLabels;
                MatchSize();
                LocalPeers.PeerAction action;
                if (!window.Owner.IsVisible) peer.ClearActions();
                else if (peer.TryTakeAction(now, out action))
                {
                    if (action.Kind == 'k') scene.PressKey(action.Scan);
                    else scene.ClickMouse(action.Kind == 'r');
                }
                string name = String.IsNullOrEmpty(peer.Nickname) ? peer.Skin.Name : peer.Nickname;
                label.Text = name;
                if (!peer.SupportsActivity) label.Text += " · 旧版 · 仅外观";
                else if (!peer.SharingActivity) label.Text += " · 已暂停动作";
                label.ToolTip = name + " · " + peer.Skin.Name;
                window.Title = "联机伙伴 · " + name;
            }
            private void MatchSize()
            {
                double width = viewport != null && viewport.ActualWidth > 0 ? viewport.ActualWidth : window.Owner.Width;
                double height = viewport != null && viewport.ActualHeight > 0 ? viewport.ActualHeight : width * 308.0 / 400.0;
                if (box.Width == width && box.Height == height) return;
                box.Width = width;
                box.Height = height;
                placement.Resize(width, height + LabelHeadroom);
                PositionLabel();
            }

            private void PositionLabel()
            {
                double scale = Math.Min(box.Width / PixelPetScene.SceneWidth, box.Height / PixelPetScene.SceneHeight);
                Point head = scene.HeadTop;
                double x = (head.X - PixelPetScene.SceneWidth / 2) * scale;
                double maxX = Math.Max(0, (box.Width - label.ActualWidth) / 2);
                double sceneTop = LabelHeadroom + (box.Height - PixelPetScene.SceneHeight * scale) / 2;
                labelPosition.X = Math.Max(-maxX, Math.Min(maxX, x));
                labelPosition.Y = Math.Max(0, sceneTop + head.Y * scale - label.ActualHeight - 4);
            }

            public void SetOwnerVisible(bool visible) { if (IsClosed) return; if (visible && !window.IsVisible) window.Show(); else if (!visible && window.IsVisible) window.Hide(); }
            public void Close() { if (!IsClosed) window.Close(); }
        }
    }

    internal sealed partial class PixelPetScene
    {
        internal bool IsDozing { get { return sleep.Progress(clock.Elapsed.TotalMilliseconds) > 0; } }
    }
}
