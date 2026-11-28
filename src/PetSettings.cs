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
        private static readonly Brush CardBg = BrushFrom(255, 253, 248);
        private static readonly Brush Paper2 = BrushFrom(242, 235, 217);
        private static readonly Brush PaperHover = BrushFrom(234, 224, 200);
        private static readonly Brush Ink = BrushFrom(74, 55, 40);
        private static readonly Brush Muted = BrushFrom(131, 116, 97);
        private static readonly Brush Sage = BrushFrom(126, 148, 105);
        private static readonly Brush SageSoft = BrushFrom(229, 238, 218);
        private static readonly Brush Line = BrushFrom(229, 222, 201);
        private static readonly Brush Danger = BrushFrom(178, 80, 62);
        private static readonly ControlTemplate ButtonTemplate = CreateButtonTemplate();
        private static readonly ControlTemplate SwitchTemplate = CreateSwitchTemplate();
        private static readonly ControlTemplate SliderTemplate = CreateSliderTemplate();

        private readonly PetWindow owner;
        private readonly Dictionary<string, Button> skinButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> deskSkinButtons = new Dictionary<string, Button>();
        private readonly UniformGrid skinGrid = new UniformGrid { Columns = 3, Rows = 0, Margin = new Thickness(-2, 6, -2, -2) };
        private readonly TextBlock importStatus = Label(10.5, Muted), updateStatus = Label(10.5, Muted);
        private ToggleButton startupSwitch;
        private readonly StartupRegistration startupRegistration;
        private readonly TextBlock startupStatus = Label(10.5, Muted);
        private readonly DispatcherTimer deactivateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        private Button installUpdate;
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
            Width = 352;
            Height = 456;
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

            selectedGroup = owner.ActiveSkin.IsCustom ? "短毛" : owner.ActiveSkin.Group;
            ScrollViewer appearancePage = BuildAppearancePage();
            ScrollViewer preferencesPage = BuildPreferencesPage();
            networkPage = BuildNetworkPage();
            StackPanel groupPage = BuildGroupPage();
            Grid.SetRow(appearancePage, 2);
            Grid.SetRow(preferencesPage, 2);
            Grid.SetRow(networkPage, 2);
            Grid.SetRow(groupPage, 2);
            layout.Children.Add(appearancePage);
            layout.Children.Add(preferencesPage);
            layout.Children.Add(networkPage);
            layout.Children.Add(groupPage);

            Border tabs = BuildTabs(appearancePage, preferencesPage, networkPage, groupPage);
            Grid.SetRow(tabs, 1);
            layout.Children.Add(tabs);

            Grid footer = BuildFooter();
            Grid.SetRow(footer, 3);
            layout.Children.Add(footer);

            Border shell = new Border
            {
                Margin = new Thickness(8),
                Padding = new Thickness(14, 12, 14, 11),
                CornerRadius = new CornerRadius(16),
                Background = Paper,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Color.FromRgb(47, 37, 25), BlurRadius = 24, ShadowDepth = 6, Opacity = 0.22 }
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

        private ScrollViewer BuildAppearancePage()
        {
            StackPanel content = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            content.Children.Add(BuildSkinsCard());
            content.Children.Add(BuildDeskSkinsCard());
            return new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private ScrollViewer BuildPreferencesPage()
        {
            StackPanel content = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

            StackPanel displayRows = new StackPanel();
            displayRows.Children.Add(SliderRow("大小", "pet-size", owner.SizePercent, owner.SetSizePercent));
            displayRows.Children.Add(Divider());
            displayRows.Children.Add(SliderRow("透明度", "pet-opacity", owner.Opacity * 100, owner.SetOpacityPercent));
            content.Children.Add(Card("显示", displayRows));

            StackPanel behaviorRows = new StackPanel();
            ToggleButton labels = MakeSwitch(owner.LabelsVisible, delegate(bool value) { owner.SetLabels(value); });
            ToggleButton passthrough = MakeSwitch(owner.MouseClickThrough, delegate(bool value) { owner.SetClickThrough(value); });
            AutomationProperties.SetAutomationId(labels, "toggle-key-bubbles");
            AutomationProperties.SetName(labels, "按键气泡");
            AutomationProperties.SetAutomationId(passthrough, "toggle-mouse-pass-through");
            AutomationProperties.SetName(passthrough, "鼠标穿透");
            behaviorRows.Children.Add(BuildToggleRow("按键气泡", "敲击键盘时弹出当前键帽", labels));
            behaviorRows.Children.Add(Divider());
            behaviorRows.Children.Add(BuildToggleRow("鼠标穿透", "开启后按住 Alt 可临时拖动", passthrough));
            behaviorRows.Children.Add(Divider());
            startupSwitch = MakeSwitch(false, SetStartupEnabled);
            AutomationProperties.SetAutomationId(startupSwitch, "toggle-start-at-login");
            AutomationProperties.SetName(startupSwitch, "开机自启");
            behaviorRows.Children.Add(BuildToggleRow("开机自启", null, startupSwitch));
            startupStatus.TextWrapping = TextWrapping.Wrap;
            startupStatus.Margin = new Thickness(0, 2, 0, 2);
            behaviorRows.Children.Add(startupStatus);
            RefreshStartup(null);
            Border behaviorCard = Card("交互与系统", behaviorRows);
            behaviorCard.Margin = new Thickness(0, 9, 0, 0);
            content.Children.Add(behaviorCard);

            return new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private Border BuildTabs(UIElement appearancePage, UIElement preferencesPage, UIElement roomPage, UIElement groupPage)
        {
            UniformGrid tabs = new UniformGrid { Rows = 1, Columns = 4 };
            RadioButton appearanceTab = PageTab("外观", appearancePage);
            tabs.Children.Add(appearanceTab);
            tabs.Children.Add(PageTab("设置", preferencesPage));
            tabs.Children.Add(PageTab("联机", roomPage));
            tabs.Children.Add(PageTab("关于", groupPage));
            appearanceTab.IsChecked = true;
            return new Border
            {
                Background = Paper2,
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 8, 0, 9),
                Child = tabs
            };
        }

        private RadioButton PageTab(string name, UIElement page)
        {
            RadioButton tab = new RadioButton
            {
                Content = name,
                Template = ButtonTemplate,
                Height = 27,
                FontSize = 11.5,
                Foreground = Muted,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                GroupName = "settingsPages"
            };
            AutomationProperties.SetName(tab, name);
            page.Visibility = Visibility.Collapsed;
            tab.Checked += delegate
            {
                tab.Background = CardBg;
                tab.Foreground = Ink;
                tab.FontWeight = FontWeights.SemiBold;
                page.Visibility = Visibility.Visible;
                RefreshActivity();
            };
            tab.Unchecked += delegate
            {
                tab.Background = Brushes.Transparent;
                tab.Foreground = Muted;
                tab.FontWeight = FontWeights.Normal;
                page.Visibility = Visibility.Collapsed;
                if (page == networkPage) StopRoomDiscovery();
            };
            return tab;
        }

        private StackPanel BuildGroupPage()
        {
            const string afdianUrl = "https://afdian.com/a/guinea_pig_garden";
            StackPanel page = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

            StackPanel afdianBody = new StackPanel();
            afdianBody.Children.Add(new TextBlock
            {
                Text = "♥ 爱发电支持豚鼠",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Ink,
                Margin = new Thickness(0, 0, 0, 9)
            });
            Button support = MakeButton(
                new TextBlock { Text = "前往爱发电支持 →", FontSize = 12, FontWeight = FontWeights.SemiBold },
                Sage, Brushes.White, double.NaN, 33);
            support.BorderBrush = Sage;
            AutomationProperties.SetName(support, "在爱发电支持豚鼠");
            support.Click += delegate { PetWindow.OpenLink(afdianUrl, "爱发电"); };
            afdianBody.Children.Add(support);
            page.Children.Add(new Border
            {
                Background = BrushFrom(247, 241, 222),
                BorderBrush = BrushFrom(217, 195, 140),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 11, 12, 11),
                Child = afdianBody
            });

            Grid qqGrid = new Grid();
            qqGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            qqGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel qqInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            qqInfo.Children.Add(new TextBlock
            {
                Text = "QQ 交流群",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink
            });
            qqInfo.Children.Add(new TextBlock
            {
                Text = "用手机 QQ 扫描右侧二维码加入",
                FontSize = 10.5,
                Foreground = Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            });
            qqGrid.Children.Add(qqInfo);
            Image code = new Image { Source = LoadSettingsImage("qq-group.png"), Width = 124, Height = 124, Stretch = Stretch.Uniform };
            AutomationProperties.SetName(code, "QQ 群二维码");
            Border qrFrame = new Border
            {
                Background = Paper,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(5),
                Child = code
            };
            Grid.SetColumn(qrFrame, 1);
            qqGrid.Children.Add(qrFrame);
            Border qqCard = CardFrame(qqGrid);
            qqCard.Margin = new Thickness(0, 10, 0, 0);
            page.Children.Add(qqCard);

            return page;
        }

        internal void PlaceAt(double left, double top)
        {
            Left = left;
            Top = top;
        }

        private StackPanel BuildHeader()
        {
            Grid row = new Grid { Margin = new Thickness(1, 0, 1, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Image avatar = new Image { Width = 32, Height = 32, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
            avatar.Source = LoadSettingsImage("avatar.png");
            Grid.SetColumn(avatar, 0);
            row.Children.Add(avatar);

            StackPanel title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            installUpdate = ActionButton("更新到最新版本", SageSoft, async delegate { await owner.InstallUpdateAsync(); });
            AutomationProperties.SetAutomationId(installUpdate, "install-update");
            installUpdate.Height = 22;
            installUpdate.Padding = new Thickness(8, 0, 8, 0);
            installUpdate.Margin = new Thickness(7, 0, 0, 0);
            DockPanel.SetDock(installUpdate, Dock.Right);
            DockPanel headline = new DockPanel
            {
                Children =
                {
                    installUpdate,
                    new TextBlock
                    {
                        Text = "豚鼠键盘",
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        Foreground = Ink,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            title.Children.Add(headline);
            string version = typeof(PetSettings).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            title.Children.Add(new TextBlock { Text = "v" + version, FontSize = 9.5, Foreground = Muted, Margin = new Thickness(0, 1, 0, 0) });
            updateStatus.Margin = new Thickness(0, 2, 0, 0);
            updateStatus.TextWrapping = TextWrapping.Wrap;
            title.Children.Add(updateStatus);
            Grid.SetColumn(title, 1);
            row.Children.Add(title);

            Button close = MakeButton(new TextBlock { Text = "×", FontSize = 17, Margin = new Thickness(0, -2, 0, 0) }, Paper2, Muted, 26, 26);
            close.Padding = new Thickness(0);
            close.BorderBrush = Brushes.Transparent;
            AutomationProperties.SetAutomationId(close, "settings-close");
            AutomationProperties.SetName(close, "关闭设置");
            close.Click += delegate { Close(); };
            Grid.SetColumn(close, 2);
            row.Children.Add(close);
            return new StackPanel { Children = { row, new Border { Height = 1, Background = Line } } };
        }

        private Border BuildSkinsCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = "豚鼠毛色",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink,
                Margin = new Thickness(0, 0, 0, 7)
            });

            UniformGrid groups = new UniformGrid { Rows = 1, Columns = 3 };
            foreach (string name in new[] { "短毛", "长毛", "趴趴猪" })
            {
                string group = name;
                Button tab = ActionButton(group, Brushes.Transparent, delegate { ShowSkinGroup(group); });
                tab.Height = 24;
                tab.Padding = new Thickness(4, 0, 4, 0);
                tab.BorderThickness = new Thickness(0);
                ((TextBlock)tab.Content).FontSize = 11;
                groupButtons.Add(group, tab);
                groups.Children.Add(tab);
            }
            body.Children.Add(new Border
            {
                Background = Paper2,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(2.5),
                Child = groups
            });

            body.Children.Add(skinGrid);
            importStatus.TextWrapping = TextWrapping.Wrap;
            importStatus.Margin = new Thickness(1, 6, 1, 0);
            importStatus.Visibility = Visibility.Collapsed;
            body.Children.Add(importStatus);
            RefreshSkinButtons();
            return CardFrame(body);
        }

        private Border BuildDeskSkinsCard()
        {
            UniformGrid grid = new UniformGrid { Columns = 3, Rows = 1, Margin = new Thickness(-2, 0, -2, -2) };
            foreach (DeskSkin desk in DeskSkin.All)
            {
                DeskSkin current = desk;
                StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                content.Children.Add(new Image { Source = current.SwatchImage(), Width = 18, Height = 14, Stretch = Stretch.Uniform });
                content.Children.Add(new TextBlock { Text = current.Name, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, Margin = new Thickness(5, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = current.Name });
                Button button = MakeButton(content, Paper, Ink, double.NaN, 32);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Padding = new Thickness(6, 2, 4, 2);
                button.Margin = new Thickness(2);
                button.Tag = current.Id;
                AutomationProperties.SetAutomationId(button, "desk-" + current.Id);
                AutomationProperties.SetName(button, current.Name);
                button.Click += delegate { owner.SetDeskSkin(current); RefreshState(); };
                deskSkinButtons[current.Id] = button;
                grid.Children.Add(button);
            }
            Border card = Card("键鼠皮肤", grid);
            card.Margin = new Thickness(0, 9, 0, 0);
            return card;
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
                content.Children.Add(new TextBlock { Text = current.Name, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, Margin = new Thickness(5, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = current.Name });
                Button button = MakeButton(content, Paper, Ink, double.NaN, 31);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Padding = new Thickness(6, 2, 4, 2);
                button.Margin = new Thickness(2);
                button.Tag = current.Id;
                AutomationProperties.SetAutomationId(button, "skin-" + current.Id);
                AutomationProperties.SetName(button, current.Name);
                button.Click += delegate { owner.SetSkin(current); RefreshState(); };
                skinButtons.Add(current.Id, button);
                FrameworkElement tile = button;
                if (current.IsCustom)
                {
                    var remove = ActionButton("×", Paper2, delegate { ConfirmUninstall(current); });
                    remove.Padding = new Thickness(2, 0, 2, 0);
                    remove.Width = 20;
                    remove.Height = 31;
                    remove.Margin = new Thickness(0, 2, 2, 2);
                    remove.BorderBrush = Brushes.Transparent;
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
            foreach (var tab in groupButtons)
            {
                bool active = tab.Key == name;
                tab.Value.Background = active ? CardBg : Brushes.Transparent;
                TextBlock label = (TextBlock)tab.Value.Content;
                label.Foreground = active ? Ink : Muted;
                label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
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
            selectedGroup = owner.ActiveSkin.IsCustom ? "短毛" : owner.ActiveSkin.Group;
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
                Title = "导入外观",
                Filter = "豚鼠外观包 (*.wheek)|*.wheek",
                CheckFileExists = true,
                Multiselect = false
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

        private static Border Card(string title, UIElement body)
        {
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink,
                Margin = new Thickness(0, 0, 0, 7)
            });
            stack.Children.Add(body);
            return CardFrame(stack);
        }

        private static Border CardFrame(UIElement child)
        {
            return new Border
            {
                Background = CardBg,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(11, 9, 11, 9),
                Child = child
            };
        }

        private static Border Divider()
        {
            return new Border { Height = 1, Background = Paper2, Margin = new Thickness(0, 5, 0, 5) };
        }

        private static UIElement BuildSwatch(PetSkin skin)
        {
            var image = new Image { Source = Portrait(skin), Width = 15, Height = 15, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            return image;
        }

        private Grid BuildToggleRow(string label, ToggleButton toggle)
        {
            return BuildToggleRow(label, null, toggle);
        }

        private Grid BuildToggleRow(string label, string hint, ToggleButton toggle)
        {
            AutomationProperties.SetName(toggle, label);
            Grid row = new Grid { MinHeight = hint == null ? 30 : 36 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Ink });
            if (!string.IsNullOrEmpty(hint))
                text.Children.Add(new TextBlock { Text = hint, FontSize = 10, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) });
            row.Children.Add(text);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            return row;
        }

        private StackPanel SliderRow(string label, string id, double initial, Action<double> change)
        {
            var value = Label(10.5, Ink);
            value.FontWeight = FontWeights.SemiBold;
            Border badge = new Border
            {
                Background = SageSoft,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 2, 7, 2),
                Child = value
            };
            DockPanel header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(badge, Dock.Right);
            header.Children.Add(badge);
            header.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = initial,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                Height = 20,
                Template = SliderTemplate,
                FocusVisualStyle = null,
                Cursor = Cursors.Hand
            };
            AutomationProperties.SetName(slider, label);
            AutomationProperties.SetAutomationId(slider, id);
            Action update = delegate { value.Text = Math.Round(slider.Value).ToString("0", CultureInfo.InvariantCulture) + "%"; };
            update();
            slider.ValueChanged += delegate { update(); change(slider.Value); };
            return new StackPanel { Margin = new Thickness(0, 1, 0, 1), Children = { header, slider } };
        }

        private Grid BuildFooter()
        {
            Grid footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Border line = new Border { Height = 1, Background = Line, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumnSpan(line, 2);
            footer.Children.Add(line);
            Button feedback = ActionButton("反馈问题", Paper2, owner.OpenFeedback);
            feedback.Height = 31;
            feedback.BorderBrush = Brushes.Transparent;
            feedback.Margin = new Thickness(0, 9, 4, 0);
            footer.Children.Add(feedback);
            Button exit = MakeButton(new TextBlock { Text = "退出", FontSize = 11, FontWeight = FontWeights.SemiBold }, CardBg, Danger, double.NaN, 31);
            AutomationProperties.SetAutomationId(exit, "exit");
            AutomationProperties.SetName(exit, "退出");
            exit.BorderBrush = BrushFrom(234, 206, 196);
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
                Width = 40,
                Height = 22,
                Margin = new Thickness(8, 0, 0, 0),
                IsChecked = isChecked,
                Background = BrushFrom(215, 208, 195),
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
                Padding = new Thickness(8, 3, 8, 3),
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
                button.Background = selected ? SageSoft : Paper;
                button.BorderBrush = selected ? Sage : Paper2;
                button.BorderThickness = new Thickness(selected ? 1.4 : 1);
                button.ToolTip = selected ? skin.Name + "（当前）" : skin.Name;
            }
            foreach (DeskSkin desk in DeskSkin.All)
            {
                Button button;
                if (!deskSkinButtons.TryGetValue(desk.Id, out button)) continue;
                bool selected = owner.ActiveDeskSkin.Id == desk.Id;
                button.Background = selected ? SageSoft : Paper;
                button.BorderBrush = selected ? Sage : Paper2;
                button.BorderThickness = new Thickness(selected ? 1.4 : 1);
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
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.92, "Face"));
            Trigger pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78, "Face"));
            template.Triggers.Add(hover);
            template.Triggers.Add(pressed);
            return template;
        }

        private static ControlTemplate CreateSwitchTemplate()
        {
            FrameworkElementFactory track = new FrameworkElementFactory(typeof(Border), "Track");
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            track.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            FrameworkElementFactory knob = new FrameworkElementFactory(typeof(Ellipse), "Knob");
            knob.SetValue(FrameworkElement.WidthProperty, 16.0);
            knob.SetValue(FrameworkElement.HeightProperty, 16.0);
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

        private static ControlTemplate CreateSliderTemplate()
        {
            const string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>" +
                "<Grid VerticalAlignment='Center'>" +
                "<Border Height='6' CornerRadius='3' Background='#F2EBD9' VerticalAlignment='Center'/>" +
                "<Track x:Name='PART_Track'>" +
                "<Track.DecreaseRepeatButton>" +
                "<RepeatButton Command='Slider.DecreaseLarge' Focusable='False'>" +
                "<RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>" +
                "<Border Height='6' CornerRadius='3' Background='#7E9469' VerticalAlignment='Center'/>" +
                "</ControlTemplate></RepeatButton.Template></RepeatButton>" +
                "</Track.DecreaseRepeatButton>" +
                "<Track.IncreaseRepeatButton>" +
                "<RepeatButton Command='Slider.IncreaseLarge' Focusable='False'>" +
                "<RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>" +
                "<Border Height='6' Background='Transparent' VerticalAlignment='Center'/>" +
                "</ControlTemplate></RepeatButton.Template></RepeatButton>" +
                "</Track.IncreaseRepeatButton>" +
                "<Track.Thumb>" +
                "<Thumb Width='16' Height='16' Cursor='Hand'>" +
                "<Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Ellipse Fill='#FFFDF8' Stroke='#7E9469' StrokeThickness='2'>" +
                "<Ellipse.Effect><DropShadowEffect BlurRadius='4' ShadowDepth='1' Opacity='0.22'/></Ellipse.Effect>" +
                "</Ellipse>" +
                "</ControlTemplate></Thumb.Template></Thumb>" +
                "</Track.Thumb>" +
                "</Track></Grid></ControlTemplate>";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static Brush BrushFrom(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
