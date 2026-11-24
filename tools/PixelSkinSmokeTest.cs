using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;

internal static class PixelSkinSmokeTest
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: PixelSkinSmokeTest.exe WheekKeys.exe"); return 2; }
        string exe = Path.GetFullPath(args[0]);
        string profile = Path.Combine(Path.GetTempPath(), "wheek-014-ui-check-" + Guid.NewGuid().ToString("N") + ".txt");
        Assembly assembly = Assembly.LoadFile(exe);
        Type windowType = assembly.GetType("WheekKeys.PetWindow", true);
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        object owner = Activator.CreateInstance(windowType, true);
        Window settings = null;
        try
        {
            FieldInfo pathField = windowType.GetField("settingsPath", BindingFlags.Instance | BindingFlags.NonPublic);
            pathField.SetValue(owner, profile);
            File.WriteAllLines(profile, new[] { "111", "222", "330", "False", "True", "gold-white", "False", "12345", "6789", "测试豚鼠" });
            MethodInfo restore = windowType.GetMethod("RestoreSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            restore.Invoke(owner, null);
            Check(Convert.ToDouble(windowType.GetProperty("Left").GetValue(owner, null)) == 111, "left");
            Check(Convert.ToDouble(windowType.GetProperty("Top").GetValue(owner, null)) == 222, "top");
            Check(Convert.ToDouble(windowType.GetProperty("PetWidth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 330, "width");
            Check(!(bool)windowType.GetProperty("LabelsVisible", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null), "labels");
            Check((bool)windowType.GetProperty("MouseClickThrough", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null), "through");
            Check(Convert.ToInt64(windowType.GetProperty("KeyCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 12345, "key count");
            Check(Convert.ToInt64(windowType.GetProperty("ClickCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 6789, "click count");
            Check((string)windowType.GetProperty("Nickname", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null) == "测试豚鼠", "nickname");

            FieldInfo ready = windowType.GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic);
            ready.SetValue(owner, true);
            settings = (Window)Activator.CreateInstance(assembly.GetType("WheekKeys.PetSettings", true), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { owner }, null);
            CheckSettingsLabels(settings, assembly);
            FieldInfo buttonsField = settings.GetType().GetField("skinButtons", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary buttons = (IDictionary)buttonsField.GetValue(settings);
            object restoredSkin = windowType.GetProperty("ActiveSkin", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner, null);
            Check((string)skinType.GetField("Id").GetValue(restoredSkin) == "pixel-golden-white", "legacy profile migrated");
            Check(buttons.Keys.Cast<string>().Count(id => id.StartsWith("pixel-", StringComparison.Ordinal)) == 14, "pixel skin button count");
            Check(buttons.Keys.Cast<string>().Count(id => id.StartsWith("drawn-", StringComparison.Ordinal)) == 6, "drawn skin button count");
            foreach (DictionaryEntry entry in buttons)
            {
                Button button = (Button)entry.Value;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                object active = windowType.GetProperty("ActiveSkin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null);
                string activeId = (string)skinType.GetField("Id").GetValue(active);
                Check(activeId == (string)entry.Key, "clicked skin " + entry.Key);
                string[] lines = File.ReadAllLines(profile);
                Check(lines.Length >= 10 && lines[5] == activeId, "saved skin " + entry.Key);
                Check(lines[0] == "111" && lines[1] == "222" && lines[2] == "330" && lines[3] == "False" && lines[4] == "True" && lines[6] == "False" && lines[7] == "12345" && lines[8] == "6789" && lines[9] == "测试豚鼠", "preserved profile " + entry.Key);
            }
            MethodInfo fromId = skinType.GetMethod("FromId", BindingFlags.Static | BindingFlags.Public);
            object oldAlias = fromId.Invoke(null, new object[] { "longhair-buff" });
            Check((string)skinType.GetField("Id").GetValue(oldAlias) == "pixel-longhair-buff", "old alias");
            object fallback = fromId.Invoke(null, new object[] { "unknown-skin-id" });
            Check((string)skinType.GetField("Id").GetValue(fallback) == "pixel-tricolor", "unknown fallback");
            Check((string)skinType.GetField("Id").GetValue(fromId.Invoke(null, new object[] { null })) == "pixel-tricolor", "first launch selects pixel tricolor");
            ((Button)buttons["drawn-tricolor"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            object scene = Field(owner, "scene");
            scene.GetType().GetProperty("Skin").SetValue(scene, fallback, null);
            ready.SetValue(owner, false);
            restore.Invoke(owner, null);
            object restored = scene.GetType().GetProperty("Skin").GetValue(scene, null);
            Check((string)skinType.GetField("Id").GetValue(restored) == "drawn-tricolor", "saved drawn appearance restores");
            CheckRenderLifecycle(assembly);
            CheckNetworkCompatibility(assembly);
            Console.WriteLine("PASS: settings labels and roundtrips, 14 pixel and 6 drawn skin buttons, wheel animation, render lifecycle, legacy aliases, peer names and counters");
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
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Check failed: " + label);
    }

    private static void CheckSettingsLabels(Window settings, Assembly app)
    {
        var layout = (Grid)((Border)settings.Content).Child;
        var header = (Grid)((StackPanel)layout.Children[0]).Children[0];
        var subtitle = (TextBlock)((StackPanel)header.Children[1]).Children[1];
        string version = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        Run[] labels = subtitle.Inlines.OfType<Run>().ToArray();
        Check(labels.Length == 2 && labels[0].Text == "桌宠设置", "settings header text");
        Run versionLabel = labels[1];
        Check(versionLabel.Text.Trim() == "v" + version, "settings label uses the product version");
        Check(versionLabel.FontSize < subtitle.FontSize && ((SolidColorBrush)versionLabel.Foreground).Color == Colors.Gray,
            "settings version label appears in smaller gray text");
        var network = (DockPanel)Field(settings, "networkPage");
        var controls = (StackPanel)network.Children[0];
        Check(!controls.Children.OfType<TextBlock>().Any(label => label.Text.Contains("联机时会共享具体按键")), "removed network paragraph");
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
            CheckDrawnDesk(scene, skinType);
            host.RootVisual = null;
            Pump();
            Check(!((DispatcherTimer)Field(scene, "gaze")).IsEnabled
                && !((DispatcherTimer)Field(scene, "animation")).IsEnabled, "detached timers stopped");
        }
    }

    private static void CheckDrawnDesk(FrameworkElement scene, Type skinType)
    {
        Type type = scene.GetType();
        var skin = type.GetProperty("Skin");
        object pixel = skin.GetValue(scene, null);
        skin.SetValue(scene, skinType.GetMethod("FromId").Invoke(null, new object[] { "drawn-silver-agouti" }), null);
        const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
        var poseAt = type.GetMethod("DrawnPose", instance);
        object rest = poseAt.Invoke(scene, new object[] { 0.0 });
        Type pose = rest.GetType();
        Rect body = (Rect)pose.GetField("Body").GetValue(rest);
        Check(body == new Rect(135, 123, 129, 85), "手绘身体恢复原尺寸与位置");
        type.GetField("mouseGrip", instance).SetValue(scene, 1.0);
        object held = poseAt.Invoke(scene, new object[] { 0.0 });
        Rect leaning = (Rect)pose.GetField("Body").GetValue(held);
        Point near = (Point)pose.GetField("LeftTip").GetValue(held);
        Point far = (Point)pose.GetField("RightTip").GetValue(held);
        Check(leaning.Left == 66 && near == new Point(79.5, 236.5) && far == new Point(153, 227),
            "握鼠时身体靠近鼠标，另一只前爪撑在键盘边");
        type.GetMethod("PressKey").Invoke(scene, new object[] { 0x38 });
        object hand = Field(scene, "right");
        Point alt = (Point)hand.GetType().GetField("Target").GetValue(hand);
        Check(alt == new Point(281.742, 209.608), "左 Alt 落在手绘键帽中心");
        skin.SetValue(scene, pixel, null);
        Check((Point)Field(scene, "mousePosition") == new Point(31, 243) && (double)Field(scene, "mouseGrip") == 0,
            "切回像素外观后使用对应的鼠标位置");
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
            object peerSkin = member.GetType().GetField("Skin").GetValue(member);
            Check((string)skinType.GetField("Id").GetValue(peerSkin) == "pixel-longhair-gold-white", "legacy peer skin mapped");
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
