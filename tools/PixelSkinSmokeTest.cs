using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

internal static class PixelSkinSmokeTest
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: PixelSkinSmokeTest.exe WheekKeys.exe"); return 2; }
        string exe = Path.GetFullPath(args[0]);
        string profileDirectory = Path.Combine(Path.GetTempPath(), "wheek-ui-check-" + Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(profileDirectory, "settings.txt");
        Assembly assembly = Assembly.LoadFile(exe);
        Type windowType = assembly.GetType("WheekKeys.PetWindow", true);
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        windowType.GetField("DataDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, profileDirectory);
        object owner = Activator.CreateInstance(windowType, true);
        Window settings = null;
        try
        {
            Check(((Window)owner).Width == 186 && ((Window)owner).Opacity == 1, "fresh profile starts at 60 percent size and 100 percent opacity");
            Directory.CreateDirectory(profileDirectory);
            File.WriteAllLines(profile, new[] { "111", "222", "330", "False", "True", "gold-white", "True", "12345", "6789", "测试豚鼠" });
            MethodInfo restore = windowType.GetMethod("RestoreSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            restore.Invoke(owner, null);
            Check(Convert.ToDouble(windowType.GetProperty("Left").GetValue(owner, null)) == 111, "left");
            Check(Convert.ToDouble(windowType.GetProperty("Top").GetValue(owner, null)) == 222, "top");
            Check(((Window)owner).Width == 310, "legacy size is capped at 100 percent");
            Check(!(bool)windowType.GetProperty("LabelsVisible", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null), "labels");
            Check((bool)windowType.GetProperty("MouseClickThrough", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null), "through");
            Check(Convert.ToInt64(windowType.GetProperty("KeyCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 12345, "key count");
            Check(Convert.ToInt64(windowType.GetProperty("ClickCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 6789, "click count");
            Check((string)windowType.GetProperty("Nickname", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null) == "测试豚鼠", "nickname");

            FieldInfo ready = windowType.GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic);
            ready.SetValue(owner, true);
            settings = (Window)Activator.CreateInstance(assembly.GetType("WheekKeys.PetSettings", true), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { owner }, null);
            CheckSettingsLabels(settings, assembly, owner);
            CheckStartupRegistration(assembly, owner);
            FieldInfo buttonsField = settings.GetType().GetField("skinButtons", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary buttons = (IDictionary)buttonsField.GetValue(settings);
            object restoredSkin = windowType.GetProperty("ActiveSkin", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner, null);
            Check((string)skinType.GetField("Id").GetValue(restoredSkin) == "pixel-golden-white", "legacy profile migrated");
            Check(buttons.Keys.Cast<string>().Count(id => id.StartsWith("pixel-", StringComparison.Ordinal)) == 14, "pixel skin button count");
            Check(buttons.Count == 22 && buttons.Keys.Cast<string>().Count(id => id.StartsWith("prone-")) == 8, "eight prone skin buttons");
            foreach (DictionaryEntry entry in buttons)
            {
                Button button = (Button)entry.Value;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                object active = windowType.GetProperty("ActiveSkin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null);
                string activeId = (string)skinType.GetField("Id").GetValue(active);
                Check(activeId == (string)entry.Key, "clicked skin " + entry.Key);
                string label = (string)skinType.GetField("Name").GetValue(active);
                Check(label.Length > 0 && !label.StartsWith("像素") && System.Windows.Automation.AutomationProperties.GetName(button) == label, "coat label " + activeId);
                string[] lines = File.ReadAllLines(profile);
                Check(lines.Length >= 10 && lines[5] == activeId, "saved skin " + entry.Key);
                Check(lines[0] == "111" && lines[1] == "222" && lines[2] == "310" && lines[3] == "False" && lines[4] == "True" && lines[6] == "False" && lines[7] == "12345" && lines[8] == "6789" && lines[9] == "测试豚鼠", "preserved profile " + entry.Key);
            }
            IDictionary desks = (IDictionary)Field(settings, "deskSkinButtons");
            Check(desks.Count == 3, "three pixel desk skins");
            object network = windowType.GetProperty("Network", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner, null);
            network.GetType().GetMethod("ReadPeer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(network,
                new object[] { "WK1|11111111111111111111111111111111|gold-white|1|2", 1.0 });
            object friendDesktop = Field(owner, "friends");
            MethodInfo updateFriends = friendDesktop.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            ((Window)owner).Opacity = 0;
            ((Window)owner).Show();
            updateFriends.Invoke(friendDesktop, new object[] { network, 1.0, false, true, false });
            object friend = ((IDictionary)Field(friendDesktop, "windows")).Values.Cast<object>().Single();
            object friendScene = Field(friend, "scene");
            PropertyInfo friendDesk = friendScene.GetType().GetProperty("Desk");
            object originalFriendDesk = friendDesk.GetValue(friendScene, null);
            foreach (string id in new[] { "meadow", "peach", "mocha" })
            {
                Check(desks.Contains(id), "desk button " + id);
                ((Button)desks[id]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                object activeDesk = windowType.GetProperty("ActiveDeskSkin", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner, null);
                Check((string)activeDesk.GetType().GetField("Id").GetValue(activeDesk) == id, "selected desk " + id);
                Check(File.ReadAllLines(profile)[11] == id, "saved desk " + id);
                Check(ReferenceEquals(friendDesk.GetValue(friendScene, null), originalFriendDesk), "local desk selection preserves the friend's desk");
                updateFriends.Invoke(friendDesktop, new object[] { network, 1.03, false, true, false });
                Check(ReferenceEquals(friendDesk.GetValue(friendScene, null), originalFriendDesk), "network refresh preserves the friend's desk");
            }
            MethodInfo fromId = skinType.GetMethod("FromId", BindingFlags.Static | BindingFlags.Public);
            object oldAlias = fromId.Invoke(null, new object[] { "longhair-buff" });
            Check((string)skinType.GetField("Id").GetValue(oldAlias) == "pixel-longhair-buff", "old alias");
            object fallback = fromId.Invoke(null, new object[] { "unknown-skin-id" });
            Check((string)skinType.GetField("Id").GetValue(fallback) == "pixel-tricolor", "unknown fallback");
            Check((string)skinType.GetField("Id").GetValue(fromId.Invoke(null, new object[] { null })) == "pixel-tricolor", "first launch selects pixel tricolor");
            string[] legacyIds = { "tricolor", "californian", "gold-white", "teddy", "silver-agouti", "longhair" };
            foreach (string legacyId in legacyIds)
            {
                string[] lines = File.ReadAllLines(profile);
                lines[5] = "drawn-" + legacyId;
                lines[6] = "True";
                File.WriteAllLines(profile, lines);
                ready.SetValue(owner, false);
                restore.Invoke(owner, null);
                object restored = windowType.GetProperty("ActiveSkin", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner, null);
                object expected = fromId.Invoke(null, new object[] { legacyId });
                Check((string)skinType.GetField("Id").GetValue(restored) == (string)skinType.GetField("Id").GetValue(expected), "old drawn profile " + legacyId);
                Check(Convert.ToInt64(windowType.GetProperty("KeyCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 12345
                    && Convert.ToInt64(windowType.GetProperty("ClickCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 6789
                    && (string)windowType.GetProperty("Nickname", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null) == "测试豚鼠", "legacy placeholder keeps subsequent fields aligned");
            }
            CheckPacks(assembly, owner);
            CheckRenderLifecycle(assembly);
            CheckNetworkCompatibility(assembly);
            CheckReleaseSelection(assembly);
            Console.WriteLine("PASS: startup registration, settings labels and roundtrips, 22 skin buttons, wheel animation, render lifecycle, legacy aliases, peer names and counters");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            if (settings != null) settings.Close();
            FieldInfo ready = windowType.GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic);
            ready.SetValue(owner, false);
            ((Window)owner).Close();
            if (File.Exists(profile)) File.Delete(profile);
            if (Directory.Exists(profileDirectory)) Directory.Delete(profileDirectory);
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Check failed: " + label);
    }

    private static void CheckReleaseSelection(Assembly app)
    {
        Type updater = app.GetType("WheekKeys.AppUpdater", true);
        const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
        const string page = "https://github.com/gwtt/guinea-pig-keyboard/releases/tag/v0.1.6";
        MethodInfo newer = updater.GetMethod("IsNewer", methods);
        object fallback = updater.GetMethod("ParseReleasePage", methods).Invoke(null, new object[] { new Uri(page) });
        Check((bool)Field(fallback, "OpenReleasePage") && ((Uri)Field(fallback, "Download")).AbsoluteUri == page,
            "fallback opens the official release page");
        string version = (string)Field(fallback, "Version");
        Check((bool)newer.Invoke(null, new object[] { version, "0.1.5" }) &&
            !(bool)newer.Invoke(null, new object[] { version, "0.1.6" }), "release page does not update to the same version");
        object manifest = updater.GetMethod("ParseRelease", methods).Invoke(null, new object[] {
            "{\"tag_name\":\"v0.1.6\",\"assets\":[{\"name\":\"test.exe\",\"browser_download_url\":\"https://example.test/test.exe\"}," +
            "{\"name\":\"WheekKeys.exe\",\"browser_download_url\":\"https://example.test/WheekKeys.exe\",\"digest\":\"sha256:" + new string('a', 64) + "\"}]}" });
        Check(!((bool)Field(manifest, "OpenReleasePage")) && ((Uri)Field(manifest, "Download")).AbsolutePath == "/WheekKeys.exe" &&
            (string)Field(manifest, "Sha256") == new string('a', 64), "updater selects the Windows asset and its checksum");
    }

    private static void CheckSettingsLabels(Window settings, Assembly app, object owner)
    {
        TextBlock[] texts = LogicalElements(settings).OfType<TextBlock>().ToArray();
        TextBlock title = texts.Single(element => element.Text == "豚鼠键盘");
        string version = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        TextBlock versionLabel = texts.Single(element => element.Text == "v" + version);
        Check(!texts.Any(element => element.Text.Contains("桌宠设置")), "header omits the settings subtitle");
        Check(versionLabel.FontSize < title.FontSize && !Equals(versionLabel.Foreground, title.Foreground),
            "settings version label appears in smaller muted text");
        Button update = LogicalElements(settings).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "install-update");
        Check(update.Visibility == Visibility.Collapsed, "update button hidden initially");
        Check(Object.ReferenceEquals(LogicalTreeHelper.GetParent(title), LogicalTreeHelper.GetParent(update)), "update button is on the title row");
        Check(!Object.ReferenceEquals(LogicalTreeHelper.GetParent(versionLabel), LogicalTreeHelper.GetParent(update)), "version has its own row");
        Check(ButtonText(update) == "更新到最新版本", "fixed update button text");
        Check(!LogicalElements(settings).OfType<Button>().Any(button => ButtonText(button) == "检查更新"), "no manual update check button");
        Check(LogicalElements(settings).OfType<Button>().Any(button => ButtonText(button) == "反馈问题"), "settings feedback entry");
        Check(!LogicalElements(settings).OfType<Button>().Any(button => ButtonText(button) == "回到右下角"), "no move-to-corner setting");
        var sliders = LogicalElements(settings).OfType<Slider>().ToArray();
        Slider size = sliders.Single(slider => System.Windows.Automation.AutomationProperties.GetAutomationId(slider) == "pet-size");
        Slider opacity = sliders.Single(slider => System.Windows.Automation.AutomationProperties.GetAutomationId(slider) == "pet-opacity");
        Check(size.Minimum == 0 && size.Maximum == 100 && opacity.Minimum == 0 && opacity.Maximum == 100, "both display sliders span zero to 100 percent");
        size.Value = 42;
        Check(((Window)owner).Width == 130.2, "size slider applies a percentage of the base width");
        opacity.Value = 64;
        Check(((Window)owner).Opacity == 0.64, "opacity preview");
        MethodInfo save = owner.GetType().GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic);
        save.Invoke(owner, null);
        var profile = File.ReadAllLines((string)Field(owner, "settingsPath"));
        Check(profile[13] == "64", "appearance preferences are persisted");
        size.Value = 0;
        opacity.Value = 0;
        var view = (UIElement)((Window)owner).Content;
        Check(view.Visibility == Visibility.Collapsed, "zero size hides the pet and its overlays");
        save.Invoke(owner, null);
        owner.GetType().GetMethod("SetSizePercent", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, new object[] { 60.0 });
        ((Window)owner).Opacity = 1;
        owner.GetType().GetMethod("RestoreSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
        Check(view.Visibility == Visibility.Collapsed && ((Window)owner).Opacity == 0, "zero size and opacity survive saving and loading");
        size.Value = 100;
        opacity.Value = 100;
        Check(((Window)owner).Width == 310 && ((Window)owner).Opacity == 1 && view.Visibility == Visibility.Visible, "both appearance sliders recover from zero");
        var grid = (UniformGrid)Field(settings, "skinGrid");
        var groupButtons = (IDictionary)Field(settings, "groupButtons");
        foreach (string group in new[] { "短毛", "长毛", "趴趴猪" })
        {
            ((Button)groupButtons[group]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var visible = grid.Children.OfType<FrameworkElement>().Where(tile => tile.Visibility == Visibility.Visible).ToArray();
            Check(visible.Length == (group == "趴趴猪" ? 8 : 7) && visible.All(tile => Equals(tile.Tag, group)), "filter " + group);
        }
        Check(LogicalText(settings).Contains("前往爱发电支持 →"), "support entry");
        object updater = owner.GetType().GetProperty("Updater", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner, null);
        Type updaterType = updater.GetType();
        Type releaseType = updaterType.GetNestedType("Release", BindingFlags.NonPublic);
        object release = Activator.CreateInstance(releaseType, true);
        releaseType.GetField("Version", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(release, "99.12.34");
        PropertyInfo available = updaterType.GetProperty("Available", BindingFlags.Instance | BindingFlags.NonPublic);
        Action changed = (Action)updaterType.GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(updater);
        available.SetValue(updater, release, null);
        changed();
        Check(update.Visibility == Visibility.Visible && ButtonText(update) == "更新到最新版本" && Convert.ToString(update.ToolTip).Contains("99.12.34"), "available release shows button and target version");
        available.SetValue(updater, null, null);
        changed();
        Check(update.Visibility == Visibility.Collapsed, "update button hidden after release removed");
        Check(!LogicalText(settings).Any(text => text.Contains("搜狗")), "no Sogou hiding setting");
        var network = (DockPanel)Field(settings, "networkPage");
        var labels = LogicalElements(network).OfType<Button>().Select(button =>
            System.Windows.Automation.AutomationProperties.GetName(button)).ToArray();
        Check(labels.Contains("创建私密房间") && labels.Contains("创建公开房间") && labels.Contains("凭码加入"), "direct room actions");
        Check(!LogicalElements(network).OfType<Button>().Any(button => ButtonText(button).Contains("局域网")), "no LAN discovery entry");
        Check(LogicalText(network).Contains("公开房间"), "public room list in lobby");
        CheckLobbyActions(settings, app, owner);
    }

    private static void CheckLobbyActions(Window settings, Assembly app, object owner)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        const string brokerKey = "WHEEKKEYS_ROOM_BROKER";
        string originalBroker = Environment.GetEnvironmentVariable(brokerKey);
        var tabs = LogicalElements(settings).OfType<RadioButton>().ToArray();
        var network = owner.GetType().GetProperty("Network", flags).GetValue(owner, null);
        var roomPublic = network.GetType().GetProperty("RoomPublic");
        var roomCode = network.GetType().GetProperty("RoomCode");
        var buttons = LogicalElements((DockPanel)Field(settings, "networkPage")).OfType<Button>().ToArray();
        Action<string> click = name => buttons.Single(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Environment.SetEnvironmentVariable(brokerKey, "invalid");
            tabs.Single(tab => Equals(tab.Content, "联机")).IsChecked = true;
            Check(Field(settings, "roomDirectory") != null, "opening lobby starts discovery");
            foreach (bool isPublic in new[] { false, true })
            {
                click(isPublic ? "创建公开房间" : "创建私密房间");
                Check(roomCode.GetValue(network, null) != null && (bool)roomPublic.GetValue(network, null) == isPublic,
                    "room created with the chosen visibility");
                Check(Field(settings, "roomDirectory") == null, "in-room view closes browsing connection");
                click("退出房间");
            }
            const string code = "ABCDEFGHIJKLMNOP47UL";
            Type directory = app.GetType("WheekKeys.RoomDirectory", true);
            MethodInfo parse = directory.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
            Array entries = Array.CreateInstance(parse.ReturnType, 1);
            foreach (int count in new[] { 16, 2 })
            {
                string encodedName = count == 16 ? "" : "6Iqx55Sf";
                entries.SetValue(parse.Invoke(null, new object[] { "WD2|" + code + "|" + new string('a', 32) + "|" + count + "|" + encodedName, 1.0 }), 0);
                settings.GetType().GetMethod("ShowPublicRooms", flags).Invoke(settings, new object[] { entries });
                var list = (StackPanel)Field(settings, "publicRooms");
                Check(LogicalText(list).Contains(count == 16 ? "小豚鼠的公开房间" : "花生的公开房间"), "public room title uses the owner's name");
                var join = LogicalElements(list).OfType<Button>().Single();
                Check(join.IsEnabled == (count < 16), "full room disables row action");
                if (join.IsEnabled) join.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Check(Equals(roomCode.GetValue(network, null), code) && (bool)roomPublic.GetValue(network, null), "row joins listed room");
            click("退出房间");
            tabs.Single(tab => Equals(tab.Content, "外观")).IsChecked = true;
            Check(Field(settings, "roomDirectory") == null, "leaving lobby stops discovery");
        }
        finally
        {
            Environment.SetEnvironmentVariable(brokerKey, originalBroker);
            ((IDisposable)network).Dispose();
        }
    }

    private static void CheckStartupRegistration(Assembly app, object owner)
    {
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approvalPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        string sandboxPath = @"Software\WheekKeys-StartupTest-" + Guid.NewGuid().ToString("N");
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type settingsType = app.GetType("WheekKeys.PetSettings", true);
        Type registrationType = app.GetType("WheekKeys.StartupRegistration", true);
        Window panel = null;
        Func<RegistryKey, ToggleButton> open = delegate(RegistryKey key) {
            if (panel != null) panel.Close();
            object registration = Activator.CreateInstance(registrationType, flags, null, new object[] { key }, null);
            panel = (Window)Activator.CreateInstance(settingsType, flags, null, new[] { owner, registration }, null);
            return (ToggleButton)Field(panel, "startupSwitch");
        };
        Action<ToggleButton> click = button => typeof(ToggleButton).GetMethod("OnClick", flags).Invoke(button, null);
        using (RegistryKey sandbox = Registry.CurrentUser.CreateSubKey(sandboxPath))
        try
        {
            ToggleButton toggle = open(sandbox);
            Check(toggle.IsChecked == false && sandbox.OpenSubKey(runPath) == null, "opening settings creates no startup item");
            click(toggle);
            using (RegistryKey run = sandbox.OpenSubKey(runPath, true))
            using (RegistryKey approval = sandbox.CreateSubKey(approvalPath))
            {
                string command = "\"" + app.Location + "\"";
                Check(toggle.IsChecked == true && (string)run.GetValue("WheekKeys") == command, "enable registers quoted executable");
                run.SetValue("OtherApp", "keep");
                toggle = open(sandbox);
                Check(toggle.IsChecked == true, "reopened startup switch reads system state");
                click(toggle);
                Check(open(sandbox).IsChecked == false && run.GetValue("WheekKeys") == null && (string)run.GetValue("OtherApp") == "keep", "disable removes only this startup item");
                using (RegistryKey readOnly = Registry.CurrentUser.OpenSubKey(sandboxPath))
                {
                    toggle = open(readOnly);
                    click(toggle);
                    Check(toggle.IsChecked == false && run.GetValue("WheekKeys") == null && ((TextBlock)Field(panel, "startupStatus")).Text.Length > 0, "failed startup write restores switch and shows error");
                }
                run.SetValue("WheekKeys", @"C:\old\WheekKeys.exe");
                toggle = open(sandbox);
                Check(toggle.IsChecked == false, "moved app detects stale startup command");
                click(toggle);
                Check((string)run.GetValue("WheekKeys") == command, "enable repairs stale executable path");
                approval.SetValue("WheekKeys", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
                toggle = open(sandbox);
                Check(toggle.IsChecked == false && !toggle.IsEnabled && (string)run.GetValue("WheekKeys") == command, "system-disabled startup remains disabled");
                approval.DeleteValue("WheekKeys");
                Check(open(sandbox).IsChecked == true, "system re-enable is reflected");
            }
        }
        finally
        {
            if (panel != null) panel.Close();
            Registry.CurrentUser.DeleteSubKeyTree(sandboxPath);
        }
    }

    private static string ButtonText(Button button)
    {
        TextBlock content = button.Content as TextBlock;
        return content == null ? Convert.ToString(button.Content) : content.Text;
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> LogicalElements(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            DependencyObject element = child as DependencyObject;
            if (element != null)
                foreach (DependencyObject value in LogicalElements(element)) yield return value;
        }
    }

    private static System.Collections.Generic.IEnumerable<string> LogicalText(DependencyObject root)
    {
        TextBlock text = root as TextBlock;
        if (text != null) yield return text.Text ?? "";
        string name = System.Windows.Automation.AutomationProperties.GetName(root);
        if (!String.IsNullOrEmpty(name)) yield return name;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            DependencyObject element = child as DependencyObject;
            if (element != null)
                foreach (string value in LogicalText(element)) yield return value;
        }
    }

    private static object Field(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
    }

    private static void CheckPacks(Assembly assembly, object owner)
    {
        MethodInfo read = assembly.GetType("WheekKeys.WheekPackLoader", true).GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (string mode in new[] { "pixel", "drawn" })
        {
            byte[] data;
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                    writer.Write("{\"id\":\"smoke\",\"name\":\"Smoke\",\"mode\":\"" + mode + "\"}");
                data = stream.ToArray();
            }
            if (mode == "pixel")
            {
                object skin = read.Invoke(null, new object[] { data });
                Check((string)skin.GetType().GetField("Id").GetValue(skin) == "wheek-smoke", "pixel pack loads");
            }
            else
            {
                bool rejected = false;
                try { read.Invoke(null, new object[] { data }); }
                catch (TargetInvocationException error) { rejected = error.InnerException is InvalidDataException; }
                Check(rejected, "drawn pack rejected");
            }
        }
        Type loader = assembly.GetType("WheekKeys.WheekPackLoader", true);
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        MethodInfo install = loader.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo uninstall = loader.GetMethod("Uninstall", BindingFlags.Static | BindingFlags.NonPublic);
        string root = Path.Combine(Path.GetTempPath(), "wheek-pack-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source.wheek"), directory = Path.Combine(root, "installed");
            Action<string> write = delegate(string name) {
                using (var file = File.Create(source))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                    writer.Write("{\"id\":\"smoke\",\"name\":\"" + name + "\"}");
            };
            write("First");
            object first = install.Invoke(null, new object[] { source, directory });
            Check((string)skinType.GetProperty("Group").GetValue(first, null) == "自定义", "custom pack group");
            write("Second");
            object second = install.Invoke(null, new object[] { source, directory });
            Check((string)skinType.GetField("Name").GetValue(second) == "Second", "pack replacement");
            string canonical = Path.Combine(directory, "smoke.wheek");
            string foreign = Path.Combine(directory, "other.wheek");
            File.Copy(canonical, foreign);
            Check(!(bool)uninstall.Invoke(null, new object[] { "pixel-tricolor", directory }), "built-in pack protected");
            Check((bool)uninstall.Invoke(null, new object[] { "wheek-smoke", directory }), "custom pack removed");
            loader.GetMethod("LoadInstalled", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { directory });
            Check(!File.Exists(canonical) && File.Exists(foreign) && skinType.GetMethod("Find").Invoke(null, new object[] { "wheek-smoke" }) == null, "noncanonical pack ignored");
            install.Invoke(null, new object[] { source, directory });
            File.Delete(canonical);
            Directory.CreateDirectory(canonical);
            File.WriteAllText(Path.Combine(canonical, "keep"), "keep");
            bool rejectedDirectory = false;
            try { uninstall.Invoke(null, new object[] { "wheek-smoke", directory }); }
            catch (TargetInvocationException e) { rejectedDirectory = e.InnerException is IOException || e.InnerException is UnauthorizedAccessException; }
            Check(rejectedDirectory && File.Exists(Path.Combine(canonical, "keep")) && skinType.GetMethod("Find").Invoke(null, new object[] { "wheek-smoke" }) != null, "pack directory protected");
            Directory.Delete(canonical, true);
            uninstall.Invoke(null, new object[] { "wheek-smoke", directory });
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var ownerType = owner.GetType();
            ownerType.GetField("ready", instance).SetValue(owner, true);
            ownerType.GetMethod("ImportSkin", instance).Invoke(owner, new object[] { source });
            string settingsPath = (string)Field(owner, "settingsPath");
            Check(File.ReadAllLines(settingsPath)[5] == "wheek-smoke", "import selects and saves the custom skin");
            object selected = ownerType.GetProperty("ActiveSkin", instance).GetValue(owner, null);
            ownerType.GetMethod("UninstallSkin", instance).Invoke(owner, new[] { selected });
            Check(File.ReadAllLines(settingsPath)[5] == "pixel-tricolor" && File.Exists(source), "removing the active skin saves fallback and preserves source");
            Directory.Delete(Path.Combine(Path.GetDirectoryName(settingsPath), "skins"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckRenderLifecycle(Assembly assembly)
    {
        Type sceneType = assembly.GetType("WheekKeys.PixelPetScene", true);
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        var scene = (FrameworkElement)Activator.CreateInstance(sceneType);
        var options = new HwndSourceParameters("Pixel skin lifecycle test")
            { WindowStyle = unchecked((int)0x80000000), Width = 400, Height = 308 };
        using (var host = new HwndSource(options))
        {
            host.RootVisual = scene;
            scene.Measure(new Size(400, 308));
            scene.Arrange(new Rect(0, 0, 400, 308));
            scene.UpdateLayout();
            Pump();
            object pixel = skinType.GetMethod("FromId").Invoke(null, new object[] { "pixel-longhair-tricolor" });
            sceneType.GetProperty("Skin").SetValue(scene, pixel, null);
            Pump();
            Check(scene.IsLoaded && ((DispatcherTimer)Field(scene, "gaze")).IsEnabled, "pixel gaze started");
            CheckDozing(scene);
            CheckWheelInput(assembly, host, scene);
            sceneType.GetMethod("PressKey").Invoke(scene, new object[] { 47 });
            object left = Field(scene, "left"), right = Field(scene, "right");
            Check((bool)left.GetType().GetField("Active").GetValue(left)
                || (bool)right.GetType().GetField("Active").GetValue(right), "pixel key forwarding");
            sceneType.GetMethod("MoveMouse").Invoke(scene, new object[] { 90.0, -30.0 });
            Check((Point)Field(scene, "mouseDestination") != new Point(31, 243), "pixel mouse forwarding");
            sceneType.GetMethod("ClickMouse").Invoke(scene, new object[] { true });
            Check((bool)Field(scene, "rightButton"), "pixel click forwarding");
            sceneType.GetProperty("ShowKeyLabels").SetValue(scene, false, null);
            Check(!(bool)scene.GetType().GetProperty("ShowKeyLabels").GetValue(scene, null), "label forwarding");
            host.RootVisual = null;
            Pump();
            Check(!((DispatcherTimer)Field(scene, "gaze")).IsEnabled
                && !((DispatcherTimer)Field(scene, "animation")).IsEnabled, "detached timers stopped");
        }
    }

    private static void CheckDozing(FrameworkElement scene)
    {
        Type type = scene.GetType();
        var advance = type.GetMethod("AdvanceFace", BindingFlags.NonPublic | BindingFlags.Instance);
        object sleep = Field(scene, "sleep");
        var lastInput = sleep.GetType().GetField("lastActivity", BindingFlags.NonPublic | BindingFlags.Instance);
        var progress = sleep.GetType().GetMethod("Progress");
        var clock = (System.Diagnostics.Stopwatch)Field(scene, "clock");
        double start = (double)lastInput.GetValue(sleep);
        Check((double)progress.Invoke(sleep, new object[] { start + 29999 }) == 0, "awake before idle delay");
        Check((double)progress.Invoke(sleep, new object[] { start + 30700 }) == 1, "settles into sleepy eyes");
        Action asleep = delegate {
            double now = clock.Elapsed.TotalMilliseconds;
            lastInput.SetValue(sleep, now - 32000);
            advance.Invoke(scene, new object[] { now });
            Check((double)progress.Invoke(sleep, new object[] { now }) == 1, "sleeping without input");
        };
        asleep();
        type.GetMethod("MoveMouse").Invoke(scene, new object[] { 0.0, 0.0 });
        type.GetMethod("ScrollMouse").Invoke(scene, new object[] { 0 });
        Check((double)progress.Invoke(sleep, new object[] { clock.Elapsed.TotalMilliseconds }) == 1, "zero mouse deltas preserve sleep");
        foreach (Action input in new Action[] {
            delegate { type.GetMethod("PressKey").Invoke(scene, new object[] { 47 }); },
            delegate { type.GetMethod("MoveMouse").Invoke(scene, new object[] { 2.0, 2.0 }); },
            delegate { type.GetMethod("ClickMouse").Invoke(scene, new object[] { false }); },
            delegate { type.GetMethod("ScrollMouse").Invoke(scene, new object[] { 120 }); }
        }) {
            asleep();
            input();
            Check((double)progress.Invoke(sleep, new object[] { clock.Elapsed.TotalMilliseconds }) == 0
                && (double)lastInput.GetValue(sleep) >= 0, "input wakes dozing pet");
        }
    }

    private static void CheckWheelInput(Assembly app, HwndSource host, FrameworkElement scene)
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        Type sceneType = scene.GetType();
        MethodInfo scroll = sceneType.GetMethod("ScrollMouse");
        MethodInfo mouseHand = sceneType.GetMethod("MouseHand", privateInstance);
        Type inputType = app.GetType("WheekKeys.DesktopInput", true);
        Type packetType = inputType.GetNestedType("RawMouse", BindingFlags.NonPublic);
        MethodInfo process = inputType.GetMethod("ProcessMouse", privateInstance);
        int clicks = 0;
        int wheelEvents = 0;
        Action<int> onWheel = delegate(int delta) { wheelEvents++; scroll.Invoke(scene, new object[] { delta }); };
        using (var input = (IDisposable)Activator.CreateInstance(inputType, new object[] {
            host, new Action<int, bool>(delegate { }), new Action<bool>(delegate { clicks++; }),
            new Action<double, double>(delegate { }), onWheel }))
        {
            object packet = Activator.CreateInstance(packetType);
            packetType.GetField("ButtonFlags").SetValue(packet, (ushort)0x0400);
            FieldInfo deltaField = packetType.GetField("WheelDelta");
            deltaField.SetValue(packet, (short)15);
            process.Invoke(input, new object[] { IntPtr.Zero, packet });
            Check(((DispatcherTimer)Field(scene, "animation")).IsEnabled, "wheel wakes animation");
            sceneType.GetField("mouseGrip", privateInstance).SetValue(scene, 1.0);
            Point start = new Point(183, 201);
            Point up = (Point)mouseHand.Invoke(scene, new object[] { start, (double)Field(scene, "lastScroll") + 100 });
            deltaField.SetValue(packet, (short)-120);
            process.Invoke(input, new object[] { IntPtr.Zero, packet });
            Point down = (Point)mouseHand.Invoke(scene, new object[] { start, (double)Field(scene, "lastScroll") + 100 });
            deltaField.SetValue(packet, (short)0);
            process.Invoke(input, new object[] { IntPtr.Zero, packet });
            Check(up.Y < down.Y && wheelEvents == 2 && clicks == 0, "signed wheel movement without clicks");
        }
    }

    private static void CheckNetworkCompatibility(Assembly assembly)
    {
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        Type peerType = assembly.GetType("WheekKeys.LocalPeers", true);
        object peer = Activator.CreateInstance(peerType);
        try
        {
            MethodInfo read = peerType.GetMethod("ReadPeer", BindingFlags.Instance | BindingFlags.NonPublic);
            string session = "1234567890abcdef1234567890abcdef";
            string[] nativeIds = { "tricolor", "californian", "gold-white", "teddy", "silver-agouti", "longhair" };
            foreach (object skin in (Array)skinType.GetField("All").GetValue(null))
            {
                string id = (string)skinType.GetField("NetworkId").GetValue(skin);
                Check(Array.IndexOf(nativeIds, id) >= 0, "legacy-compatible wire skin " + id);
                read.Invoke(peer, new object[] { "WK1|" + session + "|" + id + "|12345|6789", 10.0 });
            }
            IDictionary members = (IDictionary)Field(peer, "peers");
            Check(members.Count == 1, "peer recognized");
            object member = members[session];
            Check((long)member.GetType().GetField("Keys").GetValue(member) == 12345
                && (long)member.GetType().GetField("Clicks").GetValue(member) == 6789, "peer counters");
            read.Invoke(peer, new object[] { "WK1|" + session + "|longhair|12345|6789", 10.05 });
            object peerSkin = member.GetType().GetField("Skin").GetValue(member);
            Check((string)skinType.GetField("Id").GetValue(peerSkin) == "pixel-longhair-gold-white", "legacy peer skin mapped");
            foreach (string id in new[] { "prone-tricolor", "prone-black-white", "prone-golden-white", "prone-cream", "prone-californian", "prone-teddy", "prone-silver-agouti", "prone-longhair" })
            {
                read.Invoke(peer, new object[] { "WS1|" + session + "|" + id + "|1", 10.1 });
                Check((string)skinType.GetField("Id").GetValue(member.GetType().GetField("Skin").GetValue(member)) == id, "exact prone peer " + id);
            }
            foreach (string legacyId in nativeIds)
            {
                read.Invoke(peer, new object[] { "WS1|" + session + "|drawn-" + legacyId + "|1", 10.1 });
                object actual = member.GetType().GetField("Skin").GetValue(member);
                object expected = skinType.GetMethod("FromId").Invoke(null, new object[] { legacyId });
                Check((string)skinType.GetField("Id").GetValue(actual) == (string)skinType.GetField("Id").GetValue(expected), "old drawn peer " + legacyId);
            }
            object beforeUnknown = member.GetType().GetField("Skin").GetValue(member);
            read.Invoke(peer, new object[] { "WS1|" + session + "|drawn-unknown|1", 10.4 });
            Check(Object.ReferenceEquals(beforeUnknown, member.GetType().GetField("Skin").GetValue(member)), "unknown appearance ignored");
            read.Invoke(peer, new object[] { "WK1|" + session + "|unknown|0|0", 10.5 });
            Check((long)member.GetType().GetField("Keys").GetValue(member) == 12345, "unknown skin packet ignored");
            string encoded = (string)assembly.GetType("WheekKeys.PetName").GetMethod("Encode").Invoke(null, new object[] { "测试豚鼠" });
            read.Invoke(peer, new object[] { "WN1|" + session + "|" + encoded, 11.0 });
            Check((string)member.GetType().GetField("Nickname").GetValue(member) == "测试豚鼠", "peer nickname");
            Check(!(bool)peerType.GetProperty("Enabled").GetValue(peer, null), "no real network started");
        }
        finally { ((IDisposable)peer).Dispose(); }
    }
}
