using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WheekKeys
{
    internal sealed partial class PetSettings
    {
        private static readonly Brush OnlineInk = BrushFrom(77, 100, 64);
        private readonly Dictionary<string, PartnerRow> partnerRows = new Dictionary<string, PartnerRow>();
        private readonly StackPanel roster = new StackPanel();
        private readonly TextBlock connection = Label(11, Muted);
        private readonly Ellipse statusDot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0) };
        private readonly TextBlock notice = Label(11, Danger);
        private readonly TextBlock emptyHint = Label(12, Muted);
        private readonly TextBlock selfName = Label(13, Ink);
        private readonly StackPanel nameEditor = new StackPanel { Visibility = Visibility.Collapsed };
        private readonly TextBox nameInput = new TextBox { Height = 27, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBlock nameHint = Label(10.5, Muted);
        private readonly TextBlock selfKeys = Label(17, Ink);
        private readonly TextBlock selfClicks = Label(17, Ink);
        private readonly Image selfPortrait = new Image { Width = 64, Height = 45 };
        private readonly TextBlock rosterTitle = Label(12, Ink);
        private readonly Grid rosterHeader = RosterGrid();
        private readonly DockPanel networkPage;
        private readonly StackPanel lobby = new StackPanel();
        private readonly DockPanel invitation = new DockPanel();
        private readonly TextBox roomInput = new TextBox();
        private readonly TextBlock invitationCode = Label(14, Ink);
        private readonly TextBlock networkTitle = Label(15, Ink);
        private Button create, leave, copy;
        private string formMessage;

        private static TextBlock Label(double size, Brush color)
        {
            return new TextBlock { FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
        }

        private DockPanel BuildNetworkPage()
        {
            DockPanel page = new DockPanel();
            StackPanel controls = new StackPanel { Margin = new Thickness(2, 0, 2, 9) };
            DockPanel.SetDock(controls, Dock.Top);
            page.Children.Add(controls);
            DockPanel heading = new DockPanel();
            create = ActionButton("创建房间", SageSoft, delegate { EnterRoom(RoomTransport.CreateCode()); });
            leave = ActionButton("退出房间", Paper2, delegate {
                owner.SetNetworking(false);
                formMessage = null;
                RefreshActivity();
            });
            DockPanel.SetDock(create, Dock.Right);
            DockPanel.SetDock(leave, Dock.Right);
            heading.Children.Add(create);
            heading.Children.Add(leave);
            networkTitle.FontWeight = FontWeights.Bold;
            heading.Children.Add(networkTitle);
            controls.Children.Add(heading);
            StackPanel state = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            state.Children.Add(statusDot);
            state.Children.Add(connection);
            controls.Children.Add(state);
            controls.Children.Add(BuildToggleRow("同步按键和鼠标动作", MakeSwitch(owner.Network.ShareActivity,
                delegate(bool value) { owner.Network.ShareActivity = value; })));
            DockPanel join = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            Button joinButton = ActionButton("加入", Paper2, JoinTypedRoom);
            joinButton.Margin = new Thickness(5, 0, 0, 0);
            DockPanel.SetDock(joinButton, Dock.Right);
            join.Children.Add(joinButton);
            roomInput.Height = 27;
            roomInput.MaxLength = 64;
            roomInput.Background = Brushes.Transparent;
            roomInput.BorderThickness = new Thickness(0);
            roomInput.VerticalContentAlignment = VerticalAlignment.Center;
            roomInput.Foreground = Ink;
            roomInput.FontSize = 12;
            AutomationProperties.SetName(roomInput, "朋友的房间码");
            Grid field = new Grid();
            TextBlock placeholder = Label(11, Muted);
            placeholder.Text = "粘贴朋友的房间码";
            placeholder.IsHitTestVisible = false;
            field.Children.Add(placeholder);
            field.Children.Add(roomInput);
            roomInput.TextChanged += delegate {
                placeholder.Visibility = roomInput.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
                formMessage = null;
            };
            roomInput.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter) { JoinTypedRoom(); e.Handled = true; }
            };
            join.Children.Add(new Border { Child = field, BorderBrush = Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 0, 3, 0), Background = Brushes.WhiteSmoke });
            lobby.Children.Add(join);
            Button lan = ActionButton("或使用局域网发现", Brushes.Transparent, delegate {
                formMessage = null;
                owner.SetNetworking(true);
                RefreshActivity();
            });
            lan.Foreground = Muted;
            ((TextBlock)lan.Content).Foreground = Muted;
            lan.HorizontalAlignment = HorizontalAlignment.Left;
            lan.BorderThickness = new Thickness(0);
            lan.Padding = new Thickness(0);
            lobby.Children.Add(lan);
            controls.Children.Add(lobby);
            invitation.Margin = new Thickness(0, 8, 0, 1);
            copy = ActionButton("复制房间码", Paper2, CopyCode);
            DockPanel.SetDock(copy, Dock.Right);
            invitation.Children.Add(copy);
            invitationCode.FontFamily = new FontFamily("Consolas");
            invitationCode.FontWeight = FontWeights.SemiBold;
            invitation.Children.Add(invitationCode);
            controls.Children.Add(invitation);
            notice.TextWrapping = TextWrapping.Wrap;
            notice.Margin = new Thickness(0, 4, 0, 0);
            controls.Children.Add(notice);

            StackPanel content = new StackPanel();
            content.Children.Add(SelfCard());
            rosterHeader.Margin = new Thickness(3, 13, 3, 5);
            rosterTitle.FontWeight = FontWeights.SemiBold;
            rosterHeader.Children.Add(rosterTitle);
            foreach (string caption in new[] { "按键", "点击" })
            {
                TextBlock column = Label(10.5, Muted);
                column.Text = caption;
                column.TextAlignment = TextAlignment.Right;
                Grid.SetColumn(column, rosterHeader.Children.Count);
                rosterHeader.Children.Add(column);
            }
            content.Children.Add(rosterHeader);
            emptyHint.TextWrapping = TextWrapping.Wrap;
            emptyHint.Margin = new Thickness(3, 6, 3, 4);
            content.Children.Add(emptyHint);
            content.Children.Add(roster);
            page.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            return page;
        }

        private Button ActionButton(string title, Brush background, Action action)
        {
            TextBlock label = Label(11, Ink);
            label.Text = title;
            Button button = MakeButton(label, background, Ink, double.NaN, 27);
            AutomationProperties.SetName(button, title);
            button.Click += delegate { action(); };
            return button;
        }

        private void JoinTypedRoom()
        {
            string code = RoomTransport.NormalizeCode(roomInput.Text);
            if (code == null)
            {
                formMessage = "房间码有误，请让朋友重新复制完整房间码。";
                RefreshActivity();
                roomInput.Focus();
                roomInput.SelectAll();
                return;
            }
            EnterRoom(code);
        }

        private void EnterRoom(string code)
        {
            owner.Network.EnterRoom(code);
            roomInput.Clear();
            formMessage = null;
            ((TextBlock)copy.Content).Text = "复制房间码";
            RefreshActivity();
        }

        private void CopyCode()
        {
            try
            {
                Clipboard.SetText(invitationCode.Text);
                ((TextBlock)copy.Content).Text = "已复制";
                formMessage = null;
            }
            catch (ExternalException) { formMessage = "剪贴板暂时不可用，请再点一次复制。"; }
            RefreshActivity();
        }

        private Border SelfCard()
        {
            Grid card = new Grid();
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.ColumnDefinitions.Add(new ColumnDefinition());
            card.Children.Add(new Border { Background = Paper, CornerRadius = new CornerRadius(11),
                Padding = new Thickness(5), Margin = new Thickness(0, 0, 12, 0), Child = selfPortrait });
            StackPanel details = new StackPanel();
            Grid.SetColumn(details, 1);
            card.Children.Add(details);
            selfName.FontWeight = FontWeights.SemiBold;
            selfName.TextTrimming = TextTrimming.CharacterEllipsis;
            DockPanel nameRow = new DockPanel();
            Button rename = ActionButton("改名", Paper, BeginRename);
            rename.Height = 21;
            rename.Padding = new Thickness(5, 0, 5, 0);
            rename.Margin = new Thickness(3, 0, 0, 0);
            DockPanel.SetDock(rename, Dock.Right);
            nameRow.Children.Add(rename);
            nameRow.Children.Add(selfName);
            details.Children.Add(nameRow);
            UniformGrid stats = new UniformGrid { Rows = 1, Columns = 2, Margin = new Thickness(0, 4, 0, 0) };
            stats.Children.Add(Metric("累计按键", selfKeys));
            stats.Children.Add(Metric("累计点击", selfClicks));
            details.Children.Add(stats);
            StackPanel body = new StackPanel();
            body.Children.Add(card);
            DockPanel edit = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            Button save = ActionButton("保存", Paper, SaveNickname);
            Button cancel = ActionButton("取消", Brushes.Transparent, CancelRename);
            DockPanel.SetDock(save, Dock.Right);
            DockPanel.SetDock(cancel, Dock.Right);
            edit.Children.Add(save);
            edit.Children.Add(cancel);
            AutomationProperties.SetName(nameInput, "小猪名字");
            nameInput.Padding = new Thickness(5, 0, 5, 0);
            nameInput.Margin = new Thickness(0, 0, 4, 0);
            nameInput.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter) { SaveNickname(); e.Handled = true; }
            };
            edit.Children.Add(nameInput);
            nameHint.TextWrapping = TextWrapping.Wrap;
            nameHint.Margin = new Thickness(0, 3, 0, 0);
            nameEditor.Children.Add(edit);
            nameEditor.Children.Add(nameHint);
            body.Children.Add(nameEditor);
            return new Border { Background = SageSoft, CornerRadius = new CornerRadius(12),
                Padding = new Thickness(11, 8, 10, 9), Child = body };
        }

        private void BeginRename()
        {
            nameInput.Text = owner.Nickname;
            nameHint.Text = "最多 16 个字符，留空恢复毛色名。";
            nameHint.Foreground = OnlineInk;
            nameEditor.Visibility = Visibility.Visible;
            nameInput.Focus();
            nameInput.SelectAll();
            nameInput.BringIntoView();
        }

        private void CancelRename()
        {
            nameEditor.Visibility = Visibility.Collapsed;
        }

        private void SaveNickname()
        {
            string error;
            if (owner.TrySetNickname(nameInput.Text, out error))
            {
                CancelRename();
                RefreshActivity();
            }
            else
            {
                nameHint.Text = error;
                nameHint.Foreground = Danger;
                nameInput.Focus();
            }
        }

        private static StackPanel Metric(string name, TextBlock value)
        {
            TextBlock caption = Label(10, OnlineInk);
            caption.Text = name;
            value.FontWeight = FontWeights.SemiBold;
            return new StackPanel { Children = { caption, value } };
        }

        internal void RefreshActivity()
        {
            if (networkPage == null || networkPage.Visibility != Visibility.Visible) return;
            LocalPeers network = owner.Network;
            bool inRoom = network.RoomCode != null;
            bool inLan = network.Enabled && !inRoom;
            lobby.Visibility = create.Visibility = network.Enabled ? Visibility.Collapsed : Visibility.Visible;
            leave.Visibility = network.Enabled ? Visibility.Visible : Visibility.Collapsed;
            string leaveLabel = inLan ? "关闭发现" : "退出房间";
            ((TextBlock)leave.Content).Text = leaveLabel;
            AutomationProperties.SetName(leave, leaveLabel);
            invitation.Visibility = inRoom ? Visibility.Visible : Visibility.Collapsed;
            networkTitle.Text = inLan ? "局域网发现" : "好友房间";
            if (inRoom)
                invitationCode.Text = string.Join("-", Enumerable.Range(0, 4).Select(i => network.RoomCode.Substring(i * 5, 5)));
            int count = network.Members.Count;
            connection.Text = inRoom ? network.RoomStatus : inLan ? "正在发现同一网络中的伙伴" : "创建房间，把房间码发给异地的朋友。";
            connection.Foreground = inLan || network.RoomConnected ? OnlineInk : Muted;
            statusDot.Fill = connection.Foreground;
            statusDot.Visibility = network.Enabled ? Visibility.Visible : Visibility.Collapsed;
            notice.Text = formMessage ?? network.Problem;
            notice.Visibility = string.IsNullOrEmpty(notice.Text) ? Visibility.Collapsed : Visibility.Visible;
            selfName.Text = string.IsNullOrEmpty(owner.Nickname) ? "我的" + owner.ActiveSkin.Name : owner.Nickname;
            selfName.ToolTip = selfName.Text + " · " + owner.ActiveSkin.Name;
            selfPortrait.Source = Portrait(owner.ActiveSkin);
            SetCount(selfKeys, owner.KeyCount);
            SetCount(selfClicks, owner.ClickCount);
            rosterTitle.Text = count == 0 ? "在线伙伴" : "在线伙伴 · " + count;
            rosterHeader.Visibility = network.Enabled ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 1; i < rosterHeader.Children.Count; i++)
                rosterHeader.Children[i].Visibility = count == 0 ? Visibility.Hidden : Visibility.Visible;
            emptyHint.Visibility = (inLan || network.RoomConnected) && count == 0 && string.IsNullOrEmpty(network.Problem)
                ? Visibility.Visible : Visibility.Collapsed;
            emptyHint.Text = inRoom ? "把上方房间码发给朋友，等对方加入后，小猪和键盘就会出现在桌面。" : "让朋友连上同一局域网，再打开局域网发现。";
            foreach (string id in partnerRows.Keys.Where(id => !network.Members.ContainsKey(id)).ToArray())
            {
                roster.Children.Remove(partnerRows[id]);
                partnerRows.Remove(id);
            }
            int index = 0;
            foreach (var entry in network.Members)
            {
                PartnerRow row;
                if (!partnerRows.TryGetValue(entry.Key, out row))
                {
                    row = new PartnerRow(entry.Key);
                    partnerRows.Add(entry.Key, row);
                    roster.Children.Insert(index, row);
                }
                row.Update(entry.Value);
                index++;
            }
        }

        private static void SetCount(TextBlock label, long count)
        {
            if (Equals(label.Tag, count)) return;
            label.Tag = count;
            string exact = count.ToString("N0", CultureInfo.CurrentCulture);
            if (count < 10000) label.Text = exact;
            else if (count < 100000000) label.Text = (count / 1e4).ToString("0.#") + "万";
            else if (count < 1000000000000) label.Text = (count / 1e8).ToString("0.#") + "亿";
            else if (count < 10000000000000000) label.Text = (count / 1e12).ToString("0.#") + "万亿";
            else label.Text = count.ToString("0.##E+0", CultureInfo.InvariantCulture);
            label.ToolTip = exact;
            AutomationProperties.SetName(label, exact);
        }

        private static ImageSource Portrait(PetSkin skin)
        {
            return PixelPetScene.Portrait(skin);
        }

        private static Grid RosterGrid()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            return grid;
        }

        private sealed class PartnerRow : Border
        {
            private readonly Image portrait = new Image { Width = 36, Height = 25, Margin = new Thickness(0, 0, 7, 0) };
            private readonly TextBlock name = Label(12, Ink), keys = Label(12, Ink), clicks = Label(12, Ink);
            private readonly TextBlock detail = Label(10, Muted);
            private readonly string shortId;

            public PartnerRow(string id)
            {
                BorderBrush = Line;
                BorderThickness = new Thickness(0, 1, 0, 0);
                Padding = new Thickness(3, 4, 3, 4);
                Grid row = RosterGrid();
                Child = row;
                DockPanel identity = new DockPanel();
                DockPanel.SetDock(portrait, Dock.Left);
                identity.Children.Add(portrait);
                shortId = "#" + id.Substring(0, 4);
                detail.ToolTip = id;
                name.FontWeight = FontWeights.SemiBold;
                name.TextTrimming = detail.TextTrimming = TextTrimming.CharacterEllipsis;
                identity.Children.Add(new StackPanel { Children = { name, detail } });
                row.Children.Add(identity);
                keys.TextAlignment = clicks.TextAlignment = TextAlignment.Right;
                Grid.SetColumn(keys, 1);
                Grid.SetColumn(clicks, 2);
                row.Children.Add(keys);
                row.Children.Add(clicks);
            }

            public void Update(LocalPeers.Peer peer)
            {
                portrait.Source = Portrait(peer.Skin);
                bool named = !string.IsNullOrEmpty(peer.Nickname);
                name.Text = named ? peer.Nickname : peer.Skin.Name;
                name.ToolTip = name.Text;
                detail.Text = named ? peer.Skin.Name + " · " + shortId : shortId;
                if (!peer.SupportsActivity) detail.Text += " · 旧版";
                else if (!peer.SharingActivity) detail.Text += " · 已暂停动作";
                SetCount(keys, peer.Keys);
                SetCount(clicks, peer.Clicks);
            }
        }
    }
}
