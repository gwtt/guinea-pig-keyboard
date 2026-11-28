using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Rectangle = System.Windows.Shapes.Rectangle;

[assembly: AssemblyProduct("WheekKeys")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly: AssemblyVersion("0.2.1.0")]
[assembly: AssemblyInformationalVersion("0.2.1")]

namespace WheekKeys
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\WheekKeys.DesktopPet", out created))
            {
                if (!created) return;
                try
                {
                    WindowPlacement.EnablePerMonitorDpi();
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    WheekPackLoader.LoadInstalled(Path.Combine(PetWindow.DataDirectory, "skins"));
                    app.Run(new PetWindow());
                }
                catch (Exception ex)
                {
                    MessageBox.Show("桌宠启动失败：\n" + ex.Message, "豚鼠键盘", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    internal sealed class PetWindow : Window
    {
        private const double MinimumPetWidth = 180;
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x20;
        private const int WsExToolWindow = 0x80;
        private const int WsExNoActivate = 0x08000000;
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        internal static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheekKeys");

        private readonly PixelPetScene scene;
        private readonly Forms.NotifyIcon tray;
        private readonly string settingsPath;
        private readonly LocalPeers peers = new LocalPeers();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer networkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly FriendDesktop friends;
        private readonly WindowPlacement placement;
        private readonly PetWindowOverlay overlay;
        private readonly AppUpdater updater = new AppUpdater(DataDirectory);
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long keyCount, clickCount;
        private string nickname = "";
        private bool settingsDirty;
        private double nextSave;
        private DesktopInput input;
        private PenInput penInput;
        private bool clickThrough, altHeld;
        private bool ready;
        private PetSettings settingsWindow;
        private Drawing.Icon ownedIcon;

        public PetWindow()
        {
            Title = "豚鼠键盘";
            Width = MinimumPetWidth;
            Height = Width * 308.0 / 400.0;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            scene = new PixelPetScene();
            scene.ShowKeyLabels = true;
            Viewbox view = new Viewbox();
            view.Width = double.NaN;
            view.Height = double.NaN;
            view.Stretch = Stretch.Uniform;
            view.Child = scene;
            Grid panel = new Grid { Children = { view } };
            overlay = new PetWindowOverlay(panel, scene);
            Content = panel;
            WheekPackLoader.EnableDrop(this, ImportSkin);
            settingsPath = Path.Combine(DataDirectory, "settings.txt");
            placement = new WindowPlacement(this, delegate { SaveSettings(); }, delegate { InteractLocally(); return true; });
            RestoreSettings();
            friends = new FriendDesktop(this, view);
            tray = new Forms.NotifyIcon();
            tray.Text = "豚鼠键盘";
            try { ownedIcon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch { }
            tray.Icon = ownedIcon ?? Drawing.SystemIcons.Application;
            try
            {
                Icon = Imaging.CreateBitmapSourceFromHIcon(tray.Icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }
            tray.ContextMenuStrip = BuildTrayMenu();
            tray.Visible = true;
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate
            {
                if (!ready) CheckForUpdates();
                ready = true;
                timer.Start();
                networkTimer.Start();
            };
            networkTimer.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                bool pressed = IsKeyDown(0x12);
                if (!pressed && input != null) input.ClearHeldAlt();
                SetAltHeld(pressed);
                peers.Tick(now, scene.Skin, keyCount, clickCount, nickname);
                friends.Update(peers, now, clickThrough, scene.ShowKeyLabels, altHeld);
                LocalPeers.ReceivedInteraction interaction;
                if (peers.TryTakeReceivedInteraction(out interaction))
                {
                    scene.TriggerHop();
                    overlay.Show(interaction.Kind == "feed" ? "来自 " + interaction.DisplayName + " 的投喂 ☘" :
                        interaction.DisplayName + " 戳了戳你的小猪 ♡");
                }
            };
            timer.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                if (settingsWindow != null) settingsWindow.RefreshActivity();
                if (settingsDirty && now >= nextSave)
                {
                    SaveSettings();
                    nextSave = now + 5;
                }
            };
            MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                Point screen = PointToScreen(e.GetPosition(this));
                OpenSettings(new Drawing.Point((int)screen.X, (int)screen.Y));
                e.Handled = true;
            };
            Closed += delegate
            {
                timer.Stop();
                networkTimer.Stop();
                updater.Cancel();
                friends.Dispose();
                peers.Dispose();
                SaveSettings();
                if (settingsWindow != null) settingsWindow.Close();
                if (input != null) input.Dispose();
                if (penInput != null) penInput.Dispose();
                tray.Visible = false;
                tray.Dispose();
                if (ownedIcon != null) ownedIcon.Dispose();
            };
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            HwndSource source = (HwndSource)PresentationSource.FromVisual(this);
            altHeld = IsKeyDown(0x12);
            ApplyWindowStyles(source.Handle);
            input = new DesktopInput(source, OnKey, OnClick,
                delegate(double dx, double dy) { if (IsVisible) scene.LocalMouseMove(dx, dy); },
                delegate(int delta) { if (IsVisible) scene.ScrollMouse(delta); });
            input.AltStateChanged += SetAltHeld;
            penInput = new PenInput(source, delegate(PenInput.Sample sample) { if (IsVisible) scene.MovePen(sample); });
        }

        internal static bool IsKeyDown(int virtualKey) { return (GetAsyncKeyState(virtualKey) & 0x8000) != 0; }

        private void SetAltHeld(bool value)
        {
            if (altHeld == value) return;
            altHeld = value;
            if (clickThrough) RefreshPointerMode();
        }

        private void RefreshPointerMode()
        {
            if (clickThrough && !altHeld) placement.StopDrag();
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) ApplyWindowStyles(handle);
            friends.SetPointerMode(clickThrough, altHeld);
        }

        private void OnKey(int scan, bool firstPress)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (firstPress)
            {
                if (IsVisible) scene.LocalKeyDown(scan);
                if (keyCount < long.MaxValue) keyCount++;
                settingsDirty = true;
                peers.RecordTyping(now);
            }
            if (IsVisible) scene.PressKey(scan);
        }

        private void OnClick(bool right)
        {
            if (clickCount < long.MaxValue) clickCount++;
            settingsDirty = true;
            peers.RecordClick(right, clock.Elapsed.TotalSeconds);
            if (IsVisible) scene.ClickMouse(right);
        }

        private void ApplyWindowStyles(IntPtr handle)
        {
            int style = GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow;
            if (clickThrough && !altHeld) style |= WsExTransparent; else style &= ~WsExTransparent;
            SetWindowLong(handle, GwlExStyle, style);
            overlay.SetAltHint(clickThrough && altHeld);
        }

        internal void OpenSettings(Drawing.Point screenPoint)
        {
            if (settingsWindow != null && settingsWindow.IsVisible)
            {
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new PetSettings(this);
            Drawing.Rectangle work = Forms.Screen.FromPoint(screenPoint).WorkingArea;
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            Matrix fromPixels = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            Point cursor = fromPixels.Transform(new Point(screenPoint.X, screenPoint.Y));
            Point start = fromPixels.Transform(new Point(work.Left, work.Top));
            Point end = fromPixels.Transform(new Point(work.Right, work.Bottom));
            settingsWindow.Height = Math.Min(settingsWindow.Height, end.Y - start.Y);
            double left = cursor.X + 10;
            double top = cursor.Y + 10;
            if (left + settingsWindow.Width > end.X) left = cursor.X - settingsWindow.Width - 10;
            if (top + settingsWindow.Height > end.Y) top = cursor.Y - settingsWindow.Height - 10;
            settingsWindow.PlaceAt(
                Math.Max(start.X, Math.Min(left, Math.Max(start.X, end.X - settingsWindow.Width))),
                Math.Max(start.Y, Math.Min(top, Math.Max(start.Y, end.Y - settingsWindow.Height))));
            settingsWindow.Show();
            settingsWindow.Activate();
            CheckForUpdates();
        }

        private async void CheckForUpdates()
        {
            await updater.CheckAsync();
        }

        internal void OpenFeedback()
        {
            OpenLink("https://github.com/gwtt/guinea-pig-keyboard/issues/new/choose", "反馈问题");
        }

        internal static void OpenLink(string url, string title)
        {
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                System.Windows.MessageBox.Show("无法打开浏览器，请在浏览器访问：" + url, title);
            }
        }

        private void OpenSettingsAtCursor()
        {
            Drawing.Point cursor = Forms.Cursor.Position;
            OpenSettings(cursor);
        }

        internal void SettingsClosed(PetSettings window)
        {
            if (ReferenceEquals(settingsWindow, window)) settingsWindow = null;
        }

        internal PetSkin ActiveSkin { get { return scene.Skin; } }
        internal DeskSkin ActiveDeskSkin { get { return scene.Desk; } }
        internal bool LabelsVisible { get { return scene.ShowKeyLabels; } }
        internal bool MouseClickThrough { get { return clickThrough; } }
        internal double PetWidth { get { return Width; } }
        internal LocalPeers Network { get { return peers; } }
        internal long KeyCount { get { return keyCount; } }
        internal long ClickCount { get { return clickCount; } }
        internal string Nickname { get { return nickname; } }
        internal AppUpdater Updater { get { return updater; } }

        internal async Task InstallUpdateAsync()
        {
            if (await updater.InstallAsync()) Close();
        }

        internal bool TrySendInteraction(string peerId, string kind)
        {
            if (!peers.TrySendInteraction(peerId, kind, clock.Elapsed.TotalSeconds)) return false;
            friends.ShowInteraction(peerId, kind);
            return true;
        }

        internal void ImportSkin(string path)
        {
            string message;
            try
            {
                PetSkin skin = WheekPackLoader.Install(path, Path.Combine(Path.GetDirectoryName(settingsPath), "skins"));
                scene.InvalidateSkinCache(skin.Id);
                SetSkin(skin);
                message = "已导入 " + skin.Name;
            }
            catch (Exception e)
            {
                if (!WheekPackLoader.IsPackError(e)) throw;
                message = "导入失败，请确认是像素外观包，并检查文件权限。";
            }
            if (settingsWindow != null) settingsWindow.SkinImported(message);
            if (settingsWindow == null || !settingsWindow.IsActive) overlay.Show(message);
        }

        internal string UninstallSkin(PetSkin skin)
        {
            try
            {
                bool selected = scene.Skin.Id == skin.Id;
                if (!WheekPackLoader.Uninstall(skin.Id, Path.Combine(DataDirectory, "skins"))) return "未找到已安装的自定义外观。";
                if (selected) SetSkin(PetSkin.All[0]);
                else RefreshTrayMenu();
                scene.InvalidateSkinCache(skin.Id);
                return "已删除 " + skin.Name;
            }
            catch (Exception e)
            {
                if (!WheekPackLoader.IsPackError(e)) throw;
                return "删除失败，请检查外观包的文件权限。";
            }
        }

        internal void InteractLocally()
        {
            scene.TriggerHop();
            overlay.Show("摸摸头 ♡");
        }

        internal void SetOpacityPercent(double percent)
        {
            Opacity = Math.Max(20, Math.Min(100, percent)) / 100;
            settingsDirty = true;
        }

        internal bool TrySetNickname(string input, out string error)
        {
            string normalized;
            if (!PetName.TryNormalize(input, out normalized))
            {
                error = "名字最多 16 个字符，不能包含控制字符";
                return false;
            }
            string previous = nickname;
            nickname = normalized;
            if (!SaveSettings())
            {
                nickname = previous;
                error = "保存名字失败，请检查本地设置文件";
                return false;
            }
            error = null;
            peers.RefreshState();
            return true;
        }

        internal void SetClickThrough(bool value)
        {
            clickThrough = value;
            RefreshPointerMode();
            RefreshTrayMenu();
            SaveSettings();
        }

        internal void SetLabels(bool value)
        {
            scene.ShowKeyLabels = value;
            RefreshTrayMenu();
            SaveSettings();
        }

        internal void SetSkin(PetSkin skin)
        {
            scene.Skin = skin;
            peers.RefreshState();
            RefreshTrayMenu();
            SaveSettings();
        }

        internal void SetDeskSkin(DeskSkin desk)
        {
            scene.Desk = desk;
            RefreshTrayMenu();
            SaveSettings();
        }

        internal void SetWidth(double width)
        {
            width = Math.Max(MinimumPetWidth, Math.Min(600, width));
            placement.Resize(width, width * 308.0 / 400.0);
            settingsDirty = true;
        }

        private sealed class MenuAction
        {
            public readonly string Label;
            public readonly Action Action;
            public MenuAction(string label, Action action) { Label = label; Action = action; }
        }

        private MenuAction[] MenuActions()
        {
            return new MenuAction[] {
                new MenuAction("放大", delegate { SetWidth(Width + 40); }),
                new MenuAction("缩小", delegate { SetWidth(Width - 40); }),
                new MenuAction(scene.ShowKeyLabels ? "隐藏按键气泡" : "显示按键气泡", delegate { SetLabels(!scene.ShowKeyLabels); }),
                new MenuAction(clickThrough ? "关闭鼠标穿透" : "开启鼠标穿透（可从托盘关闭）", delegate { SetClickThrough(!clickThrough); }),
                new MenuAction("退出", Close)
            };
        }

        private Forms.ContextMenuStrip BuildTrayMenu()
        {
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("桌宠设置…", null, delegate { OpenSettingsAtCursor(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem skins = new Forms.ToolStripMenuItem("切换外观");
            foreach (PetSkin skin in PetSkin.Available)
            {
                PetSkin currentSkin = skin;
                var group = skins.DropDownItems[skin.Group] as Forms.ToolStripMenuItem;
                if (group == null)
                {
                    group = new Forms.ToolStripMenuItem(skin.Group) { Name = skin.Group };
                    skins.DropDownItems.Add(group);
                }
                Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(currentSkin.Name);
                item.Checked = scene.Skin.Id == currentSkin.Id;
                item.Click += delegate { SetSkin(currentSkin); };
                group.DropDownItems.Add(item);
            }
            menu.Items.Add(skins);
            Forms.ToolStripMenuItem desks = new Forms.ToolStripMenuItem("切换键鼠");
            foreach (DeskSkin desk in DeskSkin.All)
            {
                DeskSkin currentDesk = desk;
                Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(currentDesk.Name);
                item.Checked = scene.Desk.Id == currentDesk.Id;
                item.Click += delegate { SetDeskSkin(currentDesk); };
                desks.DropDownItems.Add(item);
            }
            menu.Items.Add(desks);
            foreach (MenuAction entry in MenuActions())
            {
                MenuAction current = entry;
                menu.Items.Add(current.Label, null, delegate { current.Action(); });
            }
            return menu;
        }

        private void RefreshTrayMenu()
        {
            Forms.ContextMenuStrip old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = BuildTrayMenu();
            if (old != null) old.Dispose();
        }

        private void PlaceAtDefaultPosition()
        {
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, area.Right - Width - 12);
            Top = Math.Max(area.Top, area.Bottom - Height - 12);
            placement.IsDockedToTaskbar = false;
        }

        private void RestoreSettings()
        {
            PlaceAtDefaultPosition();
            try
            {
                if (!File.Exists(settingsPath)) return;
                string[] values = File.ReadAllLines(settingsPath);
                double left, top, width;
                bool labels, through;
                if (values.Length < 5 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left) ||
                    !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top) ||
                    !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                    !bool.TryParse(values[3], out labels) || !bool.TryParse(values[4], out through) ||
                    double.IsNaN(left) || double.IsInfinity(left) || double.IsNaN(top) || double.IsInfinity(top) ||
                    double.IsNaN(width) || double.IsInfinity(width)) return;
                Width = Math.Max(MinimumPetWidth, Math.Min(600, width));
                Height = Width * 308.0 / 400.0;
                Left = left;
                Top = top;
                scene.ShowKeyLabels = labels;
                clickThrough = through;
                scene.Skin = PetSkin.FromId(values.Length > 5 ? values[5] : null);
                if (values.Length > 7) LocalPeers.TryCount(values[7], out keyCount);
                if (values.Length > 8) LocalPeers.TryCount(values[8], out clickCount);
                string loadedName;
                if (values.Length > 9 && PetName.TryNormalize(values[9], out loadedName) && loadedName == values[9])
                    nickname = loadedName;
                bool docked;
                if (values.Length > 10 && bool.TryParse(values[10], out docked)) placement.IsDockedToTaskbar = docked;
                if (values.Length > 11) scene.Desk = DeskSkin.FromId(values[11]);
                int percent;
                if (values.Length > 13 && int.TryParse(values[13], out percent) && percent >= 20 && percent <= 100)
                    Opacity = percent / 100.0;
            }
            catch { PlaceAtDefaultPosition(); }
        }

        private bool SaveSettings()
        {
            if (!ready) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                string pending = settingsPath + ".tmp";
                File.WriteAllLines(pending, new string[] {
                    Left.ToString("R", CultureInfo.InvariantCulture), Top.ToString("R", CultureInfo.InvariantCulture),
                    Width.ToString("R", CultureInfo.InvariantCulture), scene.ShowKeyLabels.ToString(), clickThrough.ToString(), scene.Skin.Id,
                    // Preserve the old setting's slot so the following fields keep their positions.
                    bool.FalseString, keyCount.ToString(CultureInfo.InvariantCulture), clickCount.ToString(CultureInfo.InvariantCulture), nickname,
                    placement.IsDockedToTaskbar.ToString(), scene.Desk.Id, bool.FalseString,
                    Math.Round(Opacity * 100).ToString(CultureInfo.InvariantCulture) });
                if (File.Exists(settingsPath)) File.Replace(pending, settingsPath, null);
                else File.Move(pending, settingsPath);
                settingsDirty = false;
                return true;
            }
            catch { return false; }
        }
    }

    internal sealed class PetWindowOverlay
    {
        private readonly Grid host;
        private readonly PixelPetScene scene;
        private readonly Grid altHint;
        private readonly Canvas notices = new Canvas { IsHitTestVisible = false };
        private readonly Border bubble;
        private readonly TextBlock text;
        private readonly TranslateTransform lift = new TranslateTransform();
        private readonly double clearance;

        internal PetWindowOverlay(Grid host, PixelPetScene scene, double clearance = 4)
        {
            this.host = host;
            this.scene = scene;
            this.clearance = clearance;
            var frame = new Rectangle
            {
                RadiusX = 12, RadiusY = 12, Stroke = new SolidColorBrush(Color.FromArgb(180, 226, 158, 88)),
                StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(1, 255, 234, 204))
            };
            var caption = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(224, 255, 234, 204)), CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 3), Padding = new Thickness(7, 2, 7, 2), IsHitTestVisible = false,
                Child = new TextBlock { Text = "按住 Alt 可拖动 · 松开恢复穿透", FontSize = 10, Foreground = Brushes.SaddleBrown }
            };
            altHint = new Grid { Visibility = Visibility.Collapsed, Children = { frame, caption } };
            text = new TextBlock { Foreground = Brushes.SaddleBrown, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            bubble = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(238, 255, 244, 217)), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3), Child = text, RenderTransform = lift, Opacity = 0
            };
            notices.Children.Add(bubble);
            host.Children.Add(altHint);
            host.Children.Add(notices);
        }

        internal void SetAltHint(bool enabled) { altHint.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; }

        internal void Show(string message)
        {
            if (!host.IsVisible || host.ActualWidth < 12) return;
            text.Text = message;
            bubble.MaxWidth = host.ActualWidth - 8;
            bubble.Measure(new Size(bubble.MaxWidth, double.PositiveInfinity));
            Point head = scene.TranslatePoint(scene.HeadTop, host);
            double width = bubble.DesiredSize.Width, height = bubble.DesiredSize.Height;
            Canvas.SetLeft(bubble, Math.Max(4, Math.Min(host.ActualWidth - width - 4, head.X - width / 2)));
            Canvas.SetTop(bubble, Math.Max(7, head.Y - height - clearance));
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, -7, TimeSpan.FromSeconds(2.2)));
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2))));
            bubble.BeginAnimation(UIElement.OpacityProperty, null);
            bubble.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
