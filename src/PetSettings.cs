using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
        private readonly TextBlock sizeValue;
        private bool closing;

        public PetSettings(PetWindow owner)
        {
            this.owner = owner;
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
            body.Children.Add(BuildSkins());

            StackPanel sizeSection = BuildSection("大小");
            Grid sizeRow = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel sizeButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button decrease = MakeButton(new TextBlock { Text = "−", FontSize = 17, Margin = new Thickness(0, -3, 0, 0) }, Paper2, Ink, 28, 28);
            AutomationProperties.SetAutomationId(decrease, "size-decrease");
            AutomationProperties.SetName(decrease, "缩小");
            decrease.Margin = new Thickness(0, 0, 5, 0);
            decrease.Click += delegate { owner.SetWidth(owner.PetWidth - 40); RefreshState(); };
            sizeValue = new TextBlock { Width = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ink };
            Button increase = MakeButton(new TextBlock { Text = "+", FontSize = 16, Margin = new Thickness(0, -2, 0, 0) }, Paper2, Ink, 28, 28);
            AutomationProperties.SetAutomationId(increase, "size-increase");
            AutomationProperties.SetName(increase, "放大");
            increase.Margin = new Thickness(5, 0, 0, 0);
            increase.Click += delegate { owner.SetWidth(owner.PetWidth + 40); RefreshState(); };
            sizeButtons.Children.Add(decrease);
            sizeButtons.Children.Add(sizeValue);
            sizeButtons.Children.Add(increase);
            Grid.SetColumn(sizeButtons, 1);
            sizeRow.Children.Add(sizeButtons);
            sizeSection.Children.Add(sizeRow);
            body.Children.Add(sizeSection);

            StackPanel toggles = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            ToggleButton labels = MakeSwitch(owner.LabelsVisible, delegate(bool value) { owner.SetLabels(value); });
            ToggleButton passthrough = MakeSwitch(owner.MouseClickThrough, delegate(bool value) { owner.SetClickThrough(value); });
            AutomationProperties.SetAutomationId(labels, "toggle-key-bubbles");
            AutomationProperties.SetName(labels, "按键气泡");
            AutomationProperties.SetAutomationId(passthrough, "toggle-mouse-pass-through");
            AutomationProperties.SetName(passthrough, "鼠标穿透");
            toggles.Children.Add(BuildToggleRow("按键气泡", labels));
            toggles.Children.Add(BuildToggleRow("鼠标穿透", passthrough));
            ToggleButton sogou = MakeSwitch(owner.HideForSogou, owner.SetHideForSogou);
            AutomationProperties.SetName(sogou, "使用搜狗输入法时自动隐藏");
            toggles.Children.Add(BuildToggleRow("使用搜狗输入法时自动隐藏", sogou));
            body.Children.Add(toggles);

            ScrollViewer petPage = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            networkPage = BuildNetworkPage();
            Grid.SetRow(petPage, 2);
            Grid.SetRow(networkPage, 2);
            layout.Children.Add(petPage);
            layout.Children.Add(networkPage);
            Border tabs = BuildTabs(petPage, networkPage);
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
            Deactivated += delegate { if (!closing) Close(); };
            Closed += delegate { owner.SettingsClosed(this); };
            RefreshState();
        }

        private Border BuildTabs(UIElement petPage, UIElement lanPage)
        {
            UniformGrid tabs = new UniformGrid { Rows = 1, Columns = 2 };
            RadioButton petTab = PageTab("桌宠", petPage);
            tabs.Children.Add(petTab);
            tabs.Children.Add(PageTab("联机", lanPage));
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
            };
            return tab;
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
            avatar.Source = LoadAvatar();
            Grid.SetColumn(avatar, 0);
            row.Children.Add(avatar);

            StackPanel title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "豚鼠键盘", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ink });
            TextBlock subtitle = new TextBlock { FontSize = 10, Foreground = Muted, Margin = new Thickness(0, 1, 0, 0) };
            string version = typeof(PetSettings).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            subtitle.Inlines.Add(new Run("桌宠设置"));
            subtitle.Inlines.Add(new Run("  v" + version) { FontSize = 9, Foreground = Brushes.Gray });
            title.Children.Add(subtitle);
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
            StackPanel section = BuildSection("毛色");
            UniformGrid grid = new UniformGrid { Columns = 3, Rows = 0, Margin = new Thickness(-2, 2, -2, -2) };
            foreach (PetSkin skin in PetSkin.All)
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
                grid.Children.Add(button);
            }
            section.Children.Add(grid);
            return section;
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
            var image = new Image { Source = PixelPetScene.Portrait(skin), Width = 14, Height = 14, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, skin.IsDrawn ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
            return image;
        }

        private Grid BuildToggleRow(string label, ToggleButton toggle)
        {
            Grid row = new Grid { Height = 31, Margin = new Thickness(1, 0, 1, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink });
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
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
            Button corner = MakeButton(new TextBlock { Text = "回到右下角", FontSize = 10.5, FontWeight = FontWeights.SemiBold }, Paper2, Muted, double.NaN, 31);
            AutomationProperties.SetAutomationId(corner, "move-to-corner");
            AutomationProperties.SetName(corner, "回到右下角");
            corner.Margin = new Thickness(0, 9, 4, 0);
            corner.Click += delegate { owner.MoveToCorner(); };
            footer.Children.Add(corner);
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
            sizeValue.Text = Math.Round(owner.PetWidth / 310.0 * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
            foreach (PetSkin skin in PetSkin.All)
            {
                Button button = skinButtons[skin.Id];
                bool selected = owner.ActiveSkin.Id == skin.Id;
                button.Background = selected ? SageSoft : Paper2;
                button.BorderBrush = selected ? Sage : Brushes.Transparent;
                button.BorderThickness = new Thickness(selected ? 1.2 : 1);
                button.ToolTip = selected ? skin.Name + "（当前）" : skin.Name;
            }
        }

        private static BitmapSource LoadAvatar()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("avatar.png"))
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
