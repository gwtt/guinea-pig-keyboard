using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WheekKeys
{
    internal sealed partial class PetSettings : Window
    {
        private static readonly Brush Paper = BrushFrom(252, 248, 238);
        private static readonly Brush Paper2 = BrushFrom(243, 236, 218);
        private static readonly Brush Ink = BrushFrom(79, 59, 43);
        private static readonly Brush Muted = BrushFrom(126, 113, 96);
        private static readonly Brush Sage = BrushFrom(139, 157, 120);
        private static readonly Brush SageSoft = BrushFrom(222, 230, 210);
        private static readonly Brush Line = BrushFrom(223, 215, 199);
        private static readonly Brush Danger = BrushFrom(174, 80, 62);
        private static readonly ControlTemplate ButtonTemplate = CreateButtonTemplate();
        private static readonly ControlTemplate SwitchTemplate = CreateSwitchTemplate();

        private readonly PetWindow owner;
        private readonly Dictionary<string, Button> skinButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> deskSkinButtons = new Dictionary<string, Button>();
        private readonly UniformGrid skinGrid = new UniformGrid { Columns = 3, Rows = 0, Margin = new Thickness(-2, 2, -2, -2) };
        private readonly TextBlock importStatus = Label(10.5, Muted), updateStatus = Label(10.5, Muted);
        private readonly ToggleButton startupSwitch;
        private readonly StartupRegistration startupRegistration;
        private readonly TextBlock startupStatus = Label(10.5, Muted);
        private readonly DispatcherTimer deactivateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        private Button installUpdate;
        private readonly TextBlock todayActivity = Label(12, Ink), totalActivity = Label(10.5, Muted);
        private readonly Dictionary<string, Button> groupButtons = new Dictionary<string, Button>();
        private readonly TextBlock emptySkins = Label(11, Muted);
        private string selectedGroup;
        private bool closing, showingImportDialog, refreshingStartup;

        public PetSettings(PetWindow owner) : this(owner, StartupRegistration.CurrentUser) { }

        internal PetSettings(PetWindow owner, StartupRegistration startupRegistration)
        {
            this.owner = owner;
            this.startupRegistration = startupRegistration;
            Title = "豚鼠键盘设置";
            Width = 340;
            Height = 446;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = true;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel header = BuildHeader();
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            StackPanel body = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            DockPanel activity = new DockPanel { Margin = new Thickness(1, 0, 1, 8) };
            Button feed = ActionButton("喂草", SageSoft, delegate { Close(); owner.InteractLocally(true); });
            DockPanel.SetDock(feed, Dock.Right);
            activity.Children.Add(feed);
            activity.Children.Add(new StackPanel { Children = { todayActivity, totalActivity } });
            body.Children.Add(activity);
            body.Children.Add(SliderRow("大小", "pet-size", 180, 600, owner.PetWidth, 100.0 / 310, owner.SetWidth));
            body.Children.Add(SliderRow("透明度", "pet-opacity", 20, 100, owner.Opacity * 100, 1, owner.SetOpacityPercent));
            body.Children.Add(BuildToggleRow("全屏应用时自动隐藏", MakeSwitch(owner.HideInFullscreen, owner.SetHideInFullscreen)));
            ComboBox sleep = new ComboBox { Width = 78, Height = 25, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(sleep, "打瞌睡");
            foreach (int seconds in new[] { 15, 30, 60, 0 })
            {
                var item = new ComboBoxItem { Content = seconds == 0 ? "关闭" : seconds + " 秒", Tag = seconds };
                sleep.Items.Add(item);
                if (seconds == owner.SleepSeconds) sleep.SelectedItem = item;
            }
            sleep.SelectionChanged += delegate { owner.SetSleepSeconds((int)((ComboBoxItem)sleep.SelectedItem).Tag); };
            body.Children.Add(SettingRow("打瞌睡", sleep));
            selectedGroup = owner.ActiveSkin.Group;
            body.Children.Add(BuildSkins());
            body.Children.Add(BuildDeskSkins());

            StackPanel toggles = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            ToggleButton labels = MakeSwitch(owner.LabelsVisible, delegate(bool value) { owner.SetLabels(value); });
            ToggleButton passthrough = MakeSwitch(owner.MouseClickThrough, delegate(bool value) { owner.SetClickThrough(value); });
            AutomationProperties.SetAutomationId(labels, "toggle-key-bubbles");
            AutomationProperties.SetName(labels, "按键气泡");
            AutomationProperties.SetAutomationId(passthrough, "toggle-mouse-pass-through");
            AutomationProperties.SetName(passthrough, "鼠标穿透");
            toggles.Children.Add(BuildToggleRow("按键气泡", labels));
            toggles.Children.Add(BuildToggleRow("连击键帽流光", MakeSwitch(owner.KeyboardGlowEnabled, delegate(bool value) { owner.KeyboardGlowEnabled = value; })));
            toggles.Children.Add(BuildToggleRow("鼠标穿透", passthrough));
            startupSwitch = MakeSwitch(false, SetStartupEnabled);
            AutomationProperties.SetAutomationId(startupSwitch, "toggle-start-at-login");
            AutomationProperties.SetName(startupSwitch, "开机自启");
            toggles.Children.Add(BuildToggleRow("开机自启", startupSwitch));
            startupStatus.TextWrapping = TextWrapping.Wrap;
            toggles.Children.Add(startupStatus);
            RefreshStartup(null);
            body.Children.Add(toggles);

            ScrollViewer petPage = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            networkPage = BuildNetworkPage();
            DockPanel groupPage = BuildGroupPage();
            Grid.SetRow(petPage, 2);
            Grid.SetRow(networkPage, 2);
            Grid.SetRow(groupPage, 2);
            layout.Children.Add(petPage);
            layout.Children.Add(networkPage);
            layout.Children.Add(groupPage);
            Border tabs = BuildTabs(petPage, networkPage, groupPage);
            Grid.SetRow(tabs, 1);
            layout.Children.Add(tabs);

            Grid footer = BuildFooter();
            Grid.SetRow(footer, 3);
            layout.Children.Add(footer);

            Border shell = new Border
            {
                Margin = new Thickness(8),
                Padding = new Thickness(13, 11, 13, 10),
                CornerRadius = new CornerRadius(16),
                Background = Paper,
                BorderBrush = BrushFrom(255, 255, 255),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Color.FromRgb(47, 37, 25), BlurRadius = 22, ShadowDepth = 5, Opacity = 0.24 }
            };
            shell.Child = layout;
            Content = shell;
            WheekPackLoader.EnableDrop(this, ImportSkin);

            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    if (nameEditor.IsVisible) CancelRename();
                    else Close();
                    e.Handled = true;
                }
            };
            Closing += delegate { closing = true; };
            Activated += delegate { deactivateTimer.Stop(); };
            Deactivated += delegate { if (!closing && !showingImportDialog) deactivateTimer.Start(); };
            deactivateTimer.Tick += delegate
            {
                if (PetWindow.IsKeyDown(0x01)) return;
                deactivateTimer.Stop();
                if (!IsActive && !closing && !showingImportDialog) Close();
            };
            owner.Updater.Changed += RefreshUpdate;
            Closed += delegate
            {
                deactivateTimer.Stop();
                StopRoomDiscovery();
                owner.Updater.Changed -= RefreshUpdate;
                owner.SettingsClosed(this);
            };
            RefreshUpdate();
            RefreshState();
        }

        private Border BuildTabs(UIElement petPage, UIElement roomPage, UIElement groupPage)
        {
            UniformGrid tabs = new UniformGrid { Rows = 1, Columns = 3 };
            RadioButton petTab = PageTab("桌宠", petPage);
            tabs.Children.Add(petTab);
            tabs.Children.Add(PageTab("联机", roomPage));
            tabs.Children.Add(PageTab("QQ 群", groupPage));
            petTab.IsChecked = true;
            return new Border { Background = Paper2, Padding = new Thickness(3), CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 7, 0, 8), Child = tabs };
        }

        private RadioButton PageTab(string name, UIElement page)
        {
            RadioButton tab = new RadioButton { Content = name, Template = ButtonTemplate, Height = 26,
                FontSize = 12, Foreground = Muted, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand, GroupName = "settingsPages" };
            AutomationProperties.SetName(tab, name);
            page.Visibility = Visibility.Collapsed;
            tab.Checked += delegate {
                tab.Background = Paper;
                tab.Foreground = Ink;
                tab.FontWeight = FontWeights.SemiBold;
                page.Visibility = Visibility.Visible;
                RefreshActivity();
            };
            tab.Unchecked += delegate {
                tab.Background = Brushes.Transparent;
                tab.Foreground = Muted;
                tab.FontWeight = FontWeights.Normal;
                page.Visibility = Visibility.Collapsed;
                if (page == networkPage) StopRoomDiscovery();
            };
            return tab;
        }

        private DockPanel BuildGroupPage()
        {
            DockPanel page = new DockPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            TextBlock hint = Label(12, Muted);
            hint.Text = "用手机 QQ 扫码加群";
            hint.TextAlignment = TextAlignment.Center;
            hint.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(hint, Dock.Top);
            page.Children.Add(hint);
            Button support = ActionButton("在爱发电支持豚鼠", SageSoft,
                delegate { PetWindow.OpenLink("https://afdian.com/a/guinea_pig_garden", "爱发电"); });
            support.HorizontalAlignment = HorizontalAlignment.Center;
            support.Margin = new Thickness(0, 6, 0, 0);
            DockPanel.SetDock(support, Dock.Bottom);
            page.Children.Add(support);
            Image code = new Image { Source = LoadSettingsImage("qq-group.png"), MaxWidth = 224, MaxHeight = 224, Stretch = Stretch.Uniform };
            AutomationProperties.SetName(code, "QQ 群二维码");
            page.Children.Add(code);
            return page;
        }

        internal void PlaceAt(double left, double top)
        {
            Left = left;
            Top = top;
        }

        private StackPanel BuildHeader()
        {
            Grid row = new Grid { Margin = new Thickness(1, 1, 1, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Image avatar = new Image { Width = 31, Height = 31, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
            avatar.Source = LoadSettingsImage("avatar.png");
            Grid.SetColumn(avatar, 0);
            row.Children.Add(avatar);

            StackPanel title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            installUpdate = ActionButton("更新到最新版本", SageSoft, async delegate { await owner.InstallUpdateAsync(); });
            AutomationProperties.SetAutomationId(installUpdate, "install-update");
            installUpdate.Height = 22;
            installUpdate.Padding = new Thickness(7, 0, 7, 0);
            installUpdate.Margin = new Thickness(7, 0, 0, 0);
            DockPanel.SetDock(installUpdate, Dock.Right);
            DockPanel headline = new DockPanel { Children = { installUpdate,
                new TextBlock { Text = "豚鼠键盘", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ink,
                    VerticalAlignment = VerticalAlignment.Center } } };
            title.Children.Add(headline);
            string version = typeof(PetSettings).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            title.Children.Add(new TextBlock { Text = "v" + version, FontSize = 9, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) });
            updateStatus.Margin = new Thickness(0, 3, 0, 0);
            updateStatus.TextWrapping = TextWrapping.Wrap;
            title.Children.Add(updateStatus);
            Grid.SetColumn(title, 1);
            row.Children.Add(title);

            Button close = MakeButton(new TextBlock { Text = "×", FontSize = 18, Margin = new Thickness(0, -3, 0, 0) }, Paper2, Muted, 25, 25);
            close.Padding = new Thickness(0);
            AutomationProperties.SetAutomationId(close, "settings-close");
            AutomationProperties.SetName(close, "关闭设置");
            close.Click += delegate { Close(); };
            Grid.SetColumn(close, 2);
            row.Children.Add(close);
            return new StackPanel { Children = { row, new Border { Height = 1, Background = Line } } };
        }

        private UIElement BuildSkins()
        {
            StackPanel section = BuildSection("外观");
            UniformGrid groups = new UniformGrid { Rows = 1, Columns = 4 };
            foreach (string name in new[] { "短毛", "长毛", "趴趴猪", "自定义" })
            {
                string group = name;
                Button tab = ActionButton(group, Paper2, delegate { ShowSkinGroup(group); });
                tab.Margin = new Thickness(1);
                groupButtons.Add(group, tab);
                groups.Children.Add(tab);
            }
            section.Children.Add(groups);
            section.Children.Add(skinGrid);
            emptySkins.Text = "还没有自定义外观";
            emptySkins.Margin = new Thickness(2, 5, 0, 0);
            section.Children.Add(emptySkins);
            Button import = ActionButton("导入外观 (.wheek)…", Paper2, PickSkin);
            import.HorizontalAlignment = HorizontalAlignment.Left;
            import.Margin = new Thickness(0, 5, 0, 1);
            section.Children.Add(import);
            importStatus.TextWrapping = TextWrapping.Wrap;
            importStatus.Visibility = Visibility.Collapsed;
            section.Children.Add(importStatus);
            RefreshSkinButtons();
            return section;
        }

        private UIElement BuildDeskSkins()
        {
            StackPanel section = BuildSection("键鼠皮肤");
            UniformGrid grid = new UniformGrid { Columns = 3, Rows = 1, Margin = new Thickness(-2, 2, -2, -2) };
            foreach (DeskSkin desk in DeskSkin.All)
            {
                DeskSkin current = desk;
                StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                content.Children.Add(new Image { Source = current.SwatchImage(), Width = 17, Height = 13, Stretch = Stretch.Uniform });
                content.Children.Add(new TextBlock { Text = current.Name, FontSize = 9.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, Margin = new Thickness(4, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = current.Name });
                Button button = MakeButton(content, Paper2, Ink, double.NaN, 29);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Padding = new Thickness(5, 2, 3, 2);
                button.Margin = new Thickness(2);
                button.Tag = current.Id;
                AutomationProperties.SetAutomationId(button, "desk-" + current.Id);
                AutomationProperties.SetName(button, current.Name);
                button.Click += delegate { owner.SetDeskSkin(current); RefreshState(); };
                deskSkinButtons[current.Id] = button;
                grid.Children.Add(button);
            }
            section.Children.Add(grid);
            return section;
        }

        private void RefreshSkinButtons()
        {
            skinGrid.Children.Clear();
            skinButtons.Clear();
            foreach (PetSkin skin in PetSkin.Available)
            {
                PetSkin current = skin;
                StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                content.Children.Add(BuildSwatch(current));
                content.Children.Add(new TextBlock { Text = current.Name, FontSize = 9.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, Margin = new Thickness(4, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = current.Name });
                Button button = MakeButton(content, Paper2, Ink, double.NaN, 29);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Padding = new Thickness(5, 2, 3, 2);
                button.Margin = new Thickness(2);
                button.Tag = current.Id;
                AutomationProperties.SetAutomationId(button, "skin-" + current.Id);
                AutomationProperties.SetName(button, current.Name);
                button.Click += delegate { owner.SetSkin(current); RefreshState(); };
                skinButtons.Add(current.Id, button);
                FrameworkElement tile = button;
                if (current.IsCustom)
                {
                    var remove = ActionButton("×", Paper, delegate { ConfirmUninstall(current); });
                    remove.Padding = new Thickness(2, 0, 2, 0);
                    remove.Width = 18;
                    remove.ToolTip = "删除 " + current.Name;
                    AutomationProperties.SetName(remove, "删除 " + current.Name);
                    DockPanel.SetDock(remove, Dock.Right);
                    tile = new DockPanel { Children = { remove, button } };
                }
                tile.Tag = current.Group;
                skinGrid.Children.Add(tile);
            }
            ShowSkinGroup(selectedGroup);
        }

        private void ShowSkinGroup(string name)
        {
            selectedGroup = name;
            bool empty = true;
            foreach (FrameworkElement tile in skinGrid.Children)
            {
                bool shown = (string)tile.Tag == name;
                tile.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                if (shown) empty = false;
            }
            emptySkins.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            foreach (var tab in groupButtons) tab.Value.Background = tab.Key == name ? SageSoft : Paper2;
        }

        private void ConfirmUninstall(PetSkin skin)
        {
            showingImportDialog = true;
            try
            {
                if (MessageBox.Show(this, "删除已安装的「" + skin.Name + "」？", "删除自定义外观",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                ShowSkinStatus(owner.UninstallSkin(skin));
            }
            finally { showingImportDialog = false; }
        }

        internal void SkinImported(string message)
        {
            selectedGroup = owner.ActiveSkin.Group;
            ShowSkinStatus(message);
        }

        private void ShowSkinStatus(string message)
        {
            RefreshSkinButtons();
            RefreshState();
            importStatus.Text = message;
            importStatus.Visibility = Visibility.Visible;
            importStatus.BringIntoView();
        }

        private void ImportSkin(string path)
        {
            if (IsVisible) Activate();
            owner.ImportSkin(path);
        }

        private void PickSkin()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入外观", Filter = "豚鼠外观包 (*.wheek)|*.wheek", CheckFileExists = true, Multiselect = false
            };
            showingImportDialog = true;
            try { if (dialog.ShowDialog(this) == true) ImportSkin(dialog.FileName); }
            finally { showingImportDialog = false; }
        }

        private void SetStartupEnabled(bool enabled)
        {
            if (refreshingStartup) return;
            string error = null;
            try { startupRegistration.SetEnabled(enabled); }
            catch (Exception ex) { error = "无法修改启动项：" + ex.Message; }
            RefreshStartup(error);
        }

        private void RefreshStartup(string message)
        {
            refreshingStartup = true;
            try
            {
                StartupRegistration.State state = startupRegistration.Read();
                startupSwitch.IsChecked = state == StartupRegistration.State.On;
                startupSwitch.IsEnabled = state != StartupRegistration.State.DisabledByWindows;
                if (message == null && state == StartupRegistration.State.DisabledByWindows)
                    message = "请在 Windows 设置的「应用 → 启动」中启用 WheekKeys。";
                if (message == null && state == StartupRegistration.State.DifferentPath)
                    message = "启动项指向其他位置。开启后将使用当前的桌宠。";
            }
            catch (Exception ex)
            {
                startupSwitch.Visibility = Visibility.Hidden;
                message = "无法读取启动项：" + ex.Message;
            }
            finally { refreshingStartup = false; }
            startupStatus.Text = message ?? "";
            startupStatus.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshUpdate()
        {
            AppUpdater updater = owner.Updater;
            AppUpdater.Release latest = updater.Available;
            installUpdate.Visibility = latest == null ? Visibility.Collapsed : Visibility.Visible;
            installUpdate.IsEnabled = !updater.Busy;
            installUpdate.ToolTip = latest == null ? null : ("更新到 v" + latest.Version + "\n" + latest.Notes).TrimEnd();
            updateStatus.Text = updater.Status;
            updateStatus.Visibility = string.IsNullOrEmpty(updater.Status) ? Visibility.Collapsed : Visibility.Visible;
        }

        private StackPanel BuildSection(string label)
        {
            StackPanel section = new StackPanel { Margin = new Thickness(0, 3, 0, 4) };
            section.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(1, 0, 0, 4) });
            section.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 3, 0, 0) });
            return section;
        }

        private static UIElement BuildSwatch(PetSkin skin)
        {
            var image = new Image { Source = Portrait(skin), Width = 14, Height = 14, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            return image;
        }

        private Grid BuildToggleRow(string label, ToggleButton toggle)
        {
            AutomationProperties.SetName(toggle, label);
            return SettingRow(label, toggle);
        }

        private Grid SliderRow(string label, string id, double min, double max, double initial, double percentScale, Action<double> change)
        {
            var value = Label(11, Ink);
            value.Width = 37;
            value.TextAlignment = TextAlignment.Right;
            var slider = new Slider { Minimum = min, Maximum = max, Value = initial, TickFrequency = 1,
                IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Width = 138, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(slider, label);
            AutomationProperties.SetAutomationId(slider, id);
            Action update = delegate { value.Text = Math.Round(slider.Value * percentScale).ToString("0", CultureInfo.InvariantCulture) + "%"; };
            update();
            slider.ValueChanged += delegate { update(); change(slider.Value); };
            return SettingRow(label, new StackPanel { Orientation = Orientation.Horizontal, Children = { slider, value } });
        }

        private Grid SettingRow(string label, FrameworkElement control)
        {
            Grid row = new Grid { Height = 31, Margin = new Thickness(1, 0, 1, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink });
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        private Grid BuildFooter()
        {
            Grid footer = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Border line = new Border { Height = 1, Background = Line, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumnSpan(line, 2);
            footer.Children.Add(line);
            Button feedback = ActionButton("反馈问题", Paper2, owner.OpenFeedback);
            feedback.Height = 31;
            feedback.Margin = new Thickness(0, 9, 4, 0);
            footer.Children.Add(feedback);
            Button exit = MakeButton(new TextBlock { Text = "退出", FontSize = 10.5, FontWeight = FontWeights.SemiBold }, Paper, Danger, double.NaN, 31);
            AutomationProperties.SetAutomationId(exit, "exit");
            AutomationProperties.SetName(exit, "退出");
            exit.BorderBrush = BrushFrom(231, 205, 194);
            exit.Margin = new Thickness(4, 9, 0, 0);
            exit.Click += delegate { owner.Close(); };
            Grid.SetColumn(exit, 1);
            footer.Children.Add(exit);
            return footer;
        }

        private ToggleButton MakeSwitch(bool isChecked, Action<bool> change)
        {
            ToggleButton toggle = new ToggleButton
            {
                Width = 38,
                Height = 21,
                Margin = new Thickness(8, 0, 0, 0),
                IsChecked = isChecked,
                Background = BrushFrom(215, 210, 199),
                BorderThickness = new Thickness(0),
                Template = SwitchTemplate,
                FocusVisualStyle = null,
                Cursor = Cursors.Hand
            };
            toggle.Checked += delegate { change(true); };
            toggle.Unchecked += delegate { change(false); };
            return toggle;
        }

        private Button MakeButton(UIElement content, Brush background, Brush foreground, double width, double height)
        {
            Button button = new Button
            {
                Content = content,
                Width = width,
                Height = height,
                Background = background,
                Foreground = foreground,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 3, 7, 3),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Template = ButtonTemplate,
                FocusVisualStyle = null,
                Cursor = Cursors.Hand
            };
            return button;
        }

        private void RefreshState()
        {
            foreach (PetSkin skin in PetSkin.Available)
            {
                Button button = skinButtons[skin.Id];
                bool selected = owner.ActiveSkin.Id == skin.Id;
                button.Background = selected ? SageSoft : Paper2;
                button.BorderBrush = selected ? Sage : Brushes.Transparent;
                button.BorderThickness = new Thickness(selected ? 1.2 : 1);
                button.ToolTip = selected ? skin.Name + "（当前）" : skin.Name;
            }
            foreach (DeskSkin desk in DeskSkin.All)
            {
                Button button;
                if (!deskSkinButtons.TryGetValue(desk.Id, out button)) continue;
                bool selected = owner.ActiveDeskSkin.Id == desk.Id;
                button.Background = selected ? SageSoft : Paper2;
                button.BorderBrush = selected ? Sage : Brushes.Transparent;
                button.BorderThickness = new Thickness(selected ? 1.2 : 1);
                button.ToolTip = selected ? desk.Name + "（当前）" : desk.Name;
            }
        }

        private static BitmapSource LoadSettingsImage(string resource)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch { return null; }
        }

        private static ControlTemplate CreateButtonTemplate()
        {
            FrameworkElementFactory face = new FrameworkElementFactory(typeof(Border), "Face");
            face.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            face.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            face.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            face.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            face.AppendChild(content);
            ControlTemplate template = new ControlTemplate(typeof(ButtonBase)) { VisualTree = face };
            Trigger pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82, "Face"));
            template.Triggers.Add(pressed);
            return template;
        }

        private static ControlTemplate CreateSwitchTemplate()
        {
            FrameworkElementFactory track = new FrameworkElementFactory(typeof(Border), "Track");
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            track.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            FrameworkElementFactory knob = new FrameworkElementFactory(typeof(Ellipse), "Knob");
            knob.SetValue(FrameworkElement.WidthProperty, 15.0);
            knob.SetValue(FrameworkElement.HeightProperty, 15.0);
            knob.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
            knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            knob.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            knob.SetValue(Shape.FillProperty, Brushes.White);
            knob.SetValue(UIElement.EffectProperty, new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.22 });
            track.AppendChild(knob);
            ControlTemplate template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = track };
            Trigger checkedState = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedState.Setters.Add(new Setter(Border.BackgroundProperty, Sage, "Track"));
            checkedState.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Knob"));
            template.Triggers.Add(checkedState);
            return template;
        }

        private static Brush BrushFrom(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }
    }
}
