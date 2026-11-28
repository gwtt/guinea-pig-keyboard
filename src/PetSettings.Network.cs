using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly StackPanel publicRooms = new StackPanel();
        private readonly TextBlock discoveryStatus = Label(11, Muted);
        private readonly DockPanel roomHeading = new DockPanel();
        private RoomDirectory roomDirectory;
        private RoomDirectory.Entry[] publicEntries = new RoomDirectory.Entry[0];
        private double nextDirectoryRefresh;
        private ToggleButton shareSwitch;
        private bool refreshingShareSwitch;
        private Button copy;
        private string formMessage;

        private static TextBlock Label(double size, Brush color)
        {
            return new TextBlock { FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
        }

        private DockPanel BuildNetworkPage()
        {
            DockPanel page = new DockPanel();
            StackPanel controls = new StackPanel { Margin = new Thickness(2, 0, 2, 9) };
            Button leave = ActionButton("退出房间", Paper2, delegate {
                owner.Network.Dispose();
                formMessage = null;
                RefreshActivity();
            });
            DockPanel.SetDock(leave, Dock.Right);
            roomHeading.Children.Add(leave);
            roomHeading.Children.Add(new TextBlock { Text = "好友房间", FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
            controls.Children.Add(roomHeading);
            StackPanel state = new StackPanel { Orientation = Orientation.Horizontal };
            state.Children.Add(statusDot);
            state.Children.Add(connection);
            controls.Children.Add(state);

            lobby.Children.Add(new TextBlock { Text = "公开房间", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ink });
            discoveryStatus.HorizontalAlignment = HorizontalAlignment.Center;
            discoveryStatus.TextWrapping = TextWrapping.Wrap;
            Grid listing = new Grid { MinHeight = 44, Children = {
                new ScrollViewer { Content = publicRooms, MaxHeight = 84, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, discoveryStatus } };
            lobby.Children.Add(new Border { Background = Paper2, CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 4, 0, 0), Child = listing });
            DockPanel join = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            Button joinButton = ActionButton("凭码加入", Paper2, JoinTypedRoom);
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
            UniformGrid choices = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 4) };
            choices.Children.Add(RoomChoice("创建私密房间", "仅凭房间码加入", false));
            choices.Children.Add(RoomChoice("创建公开房间", "所有人都能发现", true));
            lobby.Children.Add(choices);
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

            shareSwitch = MakeSwitch(owner.Network.ShareActivity, delegate(bool value) {
                if (!refreshingShareSwitch) owner.Network.ShareActivity = value;
            });
            controls.Children.Add(BuildToggleRow("同步敲击和点击动画", shareSwitch));
            TextBlock privacyHint = Label(10, Muted);
            privacyHint.Text = "具体键位只在本机处理。进入房间后会分享昵称、外观和累计次数。开启同步后，好友还会看到通用敲击和鼠标点击动画。";
            privacyHint.TextWrapping = TextWrapping.Wrap;
            privacyHint.Margin = new Thickness(1, 2, 1, 0);
            controls.Children.Add(privacyHint);

            StackPanel content = new StackPanel();
            content.Children.Add(controls);
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

        private Button RoomChoice(string title, string description, bool isPublic)
        {
            TextBlock name = Label(12, Ink);
            name.Text = title;
            name.FontWeight = FontWeights.SemiBold;
            TextBlock hint = Label(10.5, Muted);
            hint.Text = description;
            StackPanel text = new StackPanel { Children = { name, hint } };
            Button choice = MakeButton(text, isPublic ? SageSoft : Paper2, Ink, double.NaN, 45);
            choice.Margin = isPublic ? new Thickness(3, 0, 0, 0) : new Thickness(0, 0, 3, 0);
            AutomationProperties.SetName(choice, title);
            choice.Click += delegate { EnterRoom(RoomTransport.CreateCode(), true, isPublic, null); };
            return choice;
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
            EnterRoom(code, false, false, null);
        }

        private void EnterRoom(string code, bool hosting, bool isPublic, string expectedHost)
        {
            StopRoomDiscovery();
            owner.Network.EnterRoom(code, hosting, isPublic, expectedHost);
            roomInput.Clear();
            formMessage = null;
            ((TextBlock)copy.Content).Text = "复制房间码";
            RefreshActivity();
        }

        private void StopRoomDiscovery()
        {
            if (roomDirectory == null) return;
            roomDirectory.Dispose();
            roomDirectory = null;
            nextDirectoryRefresh = 0;
            publicEntries = new RoomDirectory.Entry[0];
            publicRooms.Children.Clear();
        }

        private void RefreshRoomDiscovery()
        {
            if (roomDirectory == null) roomDirectory = new RoomDirectory();
            double now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            if (now < nextDirectoryRefresh) return;
            nextDirectoryRefresh = now + 0.35;
            RoomDirectory.Entry[] entries = roomDirectory.Refresh(now);
            if (entries.Length != publicEntries.Length || !entries.Zip(publicEntries, (a, b) =>
                a.Code == b.Code && a.Host == b.Host && a.Name == b.Name && a.Count == b.Count).All(same => same))
                ShowPublicRooms(entries);
            discoveryStatus.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            discoveryStatus.Text = roomDirectory.Connected ? "暂时没有公开房间" : roomDirectory.Status;
        }

        private void ShowPublicRooms(RoomDirectory.Entry[] entries)
        {
            publicEntries = entries;
            publicRooms.Children.Clear();
            foreach (RoomDirectory.Entry entry in entries)
            {
                RoomDirectory.Entry selected = entry;
                bool full = entry.Count == RoomMembership.Capacity;
                Button join = ActionButton(full ? "已满" : "加入", full ? Paper2 : SageSoft,
                    delegate { EnterRoom(selected.Code, false, true, selected.Host); });
                join.IsEnabled = !full;
                DockPanel row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                DockPanel.SetDock(join, Dock.Right);
                row.Children.Add(join);
                TextBlock seats = Label(11, Muted);
                seats.Margin = new Thickness(6, 0, 8, 0);
                seats.Text = entry.Count + "/" + RoomMembership.Capacity;
                DockPanel.SetDock(seats, Dock.Right);
                row.Children.Add(seats);
                TextBlock title = Label(12, Ink);
                title.Text = entry.Label;
                title.ToolTip = entry.Label;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(title);
                publicRooms.Children.Add(row);
            }
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
            todayActivity.Text = String.Format("今日   按键 {0:N0} · 点击 {1:N0}", owner.Today.Keys, owner.Today.Clicks);
            totalActivity.Text = String.Format("累计   按键 {0:N0} · 点击 {1:N0}", owner.KeyCount, owner.ClickCount);
            if (networkPage == null || networkPage.Visibility != Visibility.Visible) return;
            LocalPeers network = owner.Network;
            bool inRoom = network.Enabled;
            lobby.Visibility = inRoom ? Visibility.Collapsed : Visibility.Visible;
            roomHeading.Visibility = inRoom ? Visibility.Visible : Visibility.Collapsed;
            if (!inRoom) RefreshRoomDiscovery();
            refreshingShareSwitch = true;
            shareSwitch.IsChecked = network.ShareActivity;
            shareSwitch.IsEnabled = network.ShareActivityAvailable;
            refreshingShareSwitch = false;
            invitation.Visibility = network.RoomConnected ? Visibility.Visible : Visibility.Collapsed;
            if (inRoom)
                invitationCode.Text = string.Join("-", Enumerable.Range(0, 4).Select(i => network.RoomCode.Substring(i * 5, 5)));
            int count = network.Members.Count;
            connection.Visibility = roomHeading.Visibility;
            connection.Text = network.RoomStatus;
            connection.Foreground = network.RoomConnected ? OnlineInk : Muted;
            statusDot.Fill = connection.Foreground;
            statusDot.Visibility = inRoom ? Visibility.Visible : Visibility.Collapsed;
            notice.Text = formMessage;
            notice.Visibility = string.IsNullOrEmpty(notice.Text) ? Visibility.Collapsed : Visibility.Visible;
            selfName.Text = string.IsNullOrEmpty(owner.Nickname) ? "我的" + owner.ActiveSkin.Name : owner.Nickname;
            selfName.ToolTip = selfName.Text + " · " + owner.ActiveSkin.Name;
            selfPortrait.Source = Portrait(owner.ActiveSkin);
            SetCount(selfKeys, owner.KeyCount);
            SetCount(selfClicks, owner.ClickCount);
            rosterTitle.Text = count == 0 ? "在线伙伴" : "在线伙伴 · " + count;
            rosterHeader.Visibility = inRoom ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 1; i < rosterHeader.Children.Count; i++)
                rosterHeader.Children[i].Visibility = count == 0 ? Visibility.Hidden : Visibility.Visible;
            emptyHint.Visibility = network.RoomConnected && count == 0
                ? Visibility.Visible : Visibility.Collapsed;
            emptyHint.Text = network.RoomPublic ? "其他人可以从公开房间列表加入。" : "把房间码发给朋友，邀请对方加入。";
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
                    row = new PartnerRow(this, entry.Key);
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

            public PartnerRow(PetSettings settings, string id)
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
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                StackPanel interactions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(43, 1, 0, 0) };
                foreach (string kind in new[] { "poke", "feed" })
                {
                    string action = kind;
                    Button button = settings.ActionButton(action == "poke" ? "戳一戳" : "投喂", Paper2, delegate
                    {
                        bool sent = settings.owner.TrySendInteraction(id, action);
                        settings.formMessage = sent ? "已向 " + name.Text + " " + (action == "poke" ? "打招呼。" : "送出一片草叶。") :
                            "互动暂未发送，请稍后再试。";
                        settings.RefreshActivity();
                    });
                    button.Height = 20;
                    button.Padding = new Thickness(6, 0, 6, 0);
                    button.Margin = new Thickness(0, 0, 4, 0);
                    interactions.Children.Add(button);
                }
                Grid.SetRow(interactions, 1);
                Grid.SetColumnSpan(interactions, 3);
                row.Children.Add(interactions);
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
