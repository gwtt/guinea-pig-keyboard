using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class SceneSleepSmokeTest
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<object> created = new List<object>();
    private static object Field(object o, string name) { return o.GetType().GetField(name, Private).GetValue(o); }
    private static object Renderer(object scene)
    {
        return scene;
    }
    private static double Now(object scene) { return ((Stopwatch)Field(scene, "clock")).Elapsed.TotalMilliseconds; }
    private static object Sleep(object scene) { return Field(Renderer(scene), "sleep"); }
    private static double Last(object scene) { return (double)Sleep(scene).GetType().GetField("lastActivity", Private).GetValue(Sleep(scene)); }
    private static void ForceSleep(object scene) { object renderer = Renderer(scene); Sleep(scene).GetType().GetField("lastActivity", Private).SetValue(Sleep(scene), Now(renderer) - 32000); }
    private static double Progress(object scene) { return (double)Sleep(scene).GetType().GetMethod("Progress").Invoke(Sleep(scene), new object[] { Now(Renderer(scene)) }); }
    private static object New(Type type) { object scene = Activator.CreateInstance(type, true); created.Add(scene); return scene; }
    private static void Call(object scene, string method, params object[] args) { scene.GetType().GetMethod(method).Invoke(scene, args); }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); }
    private static void Pump()
    {
        DispatcherFrame frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
    }
    private static int Marks(object scene, bool pixel)
    {
        FrameworkElement element = (FrameworkElement)scene;
        element.InvalidateVisual();
        element.Measure(new Size(400, 308)); element.Arrange(new Rect(0, 0, 400, 308)); element.UpdateLayout();
        var image = new RenderTargetBitmap(400, 308, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        byte[] pixels = new byte[400 * 308 * 4]; image.CopyPixels(pixels, 400 * 4, 0);
        int count = 0, left = pixel ? 314 : 278, top = pixel ? 20 : 78, bottom = pixel ? 80 : 145;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < 350; x++) if (pixels[(y * 400 + x) * 4 + 3] != 0) count++;
        return count;
    }
    private static bool Timer(object scene, string field) { return ((DispatcherTimer)Field(scene, field)).IsEnabled; }
    private static void CheckActivity(object scene)
    {
        ForceSleep(scene); Call(scene, "PressKey", 30); Check(Progress(scene) == 0, "key wakes");
        ForceSleep(scene); Call(scene, "MoveMouse", 2.0, -1.0); Check(Progress(scene) == 0, "movement wakes");
        ForceSleep(scene); Call(scene, "ClickMouse", false); Check(Progress(scene) == 0, "click wakes");
        ForceSleep(scene); double before = Last(scene); Call(scene, "MoveMouse", 0.0, 0.0);
        Check(Last(scene) == before && Progress(scene) == 1, "zero movement does not wake");
    }
    private static void AdvanceMouse(object scene, double now)
    {
        scene.GetType().GetMethod("AdvanceMouse", Private).Invoke(scene, new object[] { now });
    }
    private static double Grip(object scene) { return (double)Field(scene, "mouseGrip"); }
    private static byte[] Pixels(object scene)
    {
        FrameworkElement element = (FrameworkElement)scene;
        element.InvalidateVisual(); element.Measure(new Size(400, 308)); element.Arrange(new Rect(0, 0, 400, 308)); element.UpdateLayout();
        var image = new RenderTargetBitmap(400, 308, 96, 96, PixelFormats.Pbgra32); image.Render(element);
        byte[] pixels = new byte[400 * 308 * 4]; image.CopyPixels(pixels, 400 * 4, 0);
        return pixels;
    }
    private static void CheckStartleRendering(Type type)
    {
        object scene = New(type);
        byte[] resting = Pixels(scene);
        ForceSleep(scene); Call(scene, "MoveMouse", 0.01, 0.01);
        Sleep(scene).GetType().GetField("wakeStarted", Private).SetValue(Sleep(scene), Now(Renderer(scene)) - 65);
        byte[] startled = Pixels(scene);
        bool changed = false;
        for (int i = 0; i < resting.Length; i++) if (resting[i] != startled[i]) { changed = true; break; }
        Check(changed, "startle changes rendered pig");
        for (int y = 260; y < 286; y++)
            for (int x = 100; x < 375; x++)
                for (int channel = 0; channel < 4; channel++)
                {
                    int index = (y * 400 + x) * 4 + channel;
                    Check(resting[index] == startled[index], "startle keeps keyboard fixed");
                }
        Sleep(scene).GetType().GetField("wakeStarted", Private).SetValue(Sleep(scene), Now(Renderer(scene)) - 500);
        byte[] settled = Pixels(scene);
        for (int i = 0; i < resting.Length; i++) Check(resting[i] == settled[i], "startle restores original pose and eye size");
    }
    private static void CheckMouseRecovery(Type type)
    {
        object normal = New(type); Call(normal, "MoveMouse", 30.0, 10.0);
        double start = Now(normal); type.GetField("lastFrame", Private).SetValue(normal, start);
        AdvanceMouse(normal, start + 100);
        double normalGrip = Grip(normal);
        Check(normalGrip > 0.5, "awake grip remains responsive");

        object sleepy = New(type); ForceSleep(sleepy); Call(sleepy, "MoveMouse", 30.0, 10.0);
        start = Now(sleepy); type.GetField("lastFrame", Private).SetValue(sleepy, start);
        AdvanceMouse(sleepy, start + 100);
        Check(Grip(sleepy) < normalGrip * 0.15, "waking pauses before reaching");
        AdvanceMouse(sleepy, start + 300);
        Check((bool)Field(sleepy, "settlingMouse"), "single movement continues beyond activity timeout");
        for (int elapsed = 324; elapsed <= 1900; elapsed += 24)
        {
            AdvanceMouse(sleepy, start + elapsed);
            if (!(bool)Field(sleepy, "settlingMouse")) break;
        }
        Check(!(bool)Field(sleepy, "settlingMouse") && Grip(sleepy) >= 0.9, "slow reach completes without more input");

        object clicked = New(type); ForceSleep(clicked); Call(clicked, "ClickMouse", true);
        start = Now(clicked); type.GetField("lastFrame", Private).SetValue(clicked, start);
        Check((bool)Field(clicked, "pendingMouseClick") && double.IsNegativeInfinity((double)Field(clicked, "lastClick")), "waking click waits for paw");
        AdvanceMouse(clicked, start + 100);
        Check((bool)Field(clicked, "pendingMouseClick") && Grip(clicked) < 0.1, "click cannot snap paw to mouse");
        for (int elapsed = 124; elapsed <= 1900; elapsed += 24)
        {
            AdvanceMouse(clicked, start + elapsed);
            if (!(bool)Field(clicked, "pendingMouseClick")) break;
        }
        Check(!(bool)Field(clicked, "pendingMouseClick") && Grip(clicked) >= 0.9
            && (double)Field(clicked, "lastClick") > start + 400, "click plays after slow grip arrives");
        double arrived = (double)Field(clicked, "lastClick");
        for (int elapsed = 24; elapsed <= 384; elapsed += 24) AdvanceMouse(clicked, arrived + elapsed);
        Check(Grip(clicked) > 0.35, "first waking gesture returns gently");
        for (int elapsed = 408; elapsed <= 2500; elapsed += 24) AdvanceMouse(clicked, arrived + elapsed);
        Check(Grip(clicked) < 0.01 && !(bool)Field(clicked, "pendingMouseClick"), "waking mouse gesture finishes and releases");

        object cancelled = New(type); ForceSleep(cancelled); Call(cancelled, "ClickMouse", false); Call(cancelled, "PressKey", 36);
        Check(!(bool)Field(cancelled, "settlingMouse") && !(bool)Field(cancelled, "pendingMouseClick"), "typing cancels pending mouse reach");

        object recovered = New(type); ForceSleep(recovered); Call(recovered, "MoveMouse", 30.0, 10.0);
        Sleep(recovered).GetType().GetField("wakeStarted", Private).SetValue(Sleep(recovered), Now(recovered) - 1601);
        start = Now(recovered); type.GetField("lastFrame", Private).SetValue(recovered, start);
        AdvanceMouse(recovered, start + 100);
        Check(Math.Abs(Grip(recovered) - normalGrip) < 0.02, "normal grip speed returns after recovery");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        try
        {
            Assembly assembly = Assembly.LoadFile(Path.GetFullPath(args[0]));
            Type pixel = assembly.GetType("WheekKeys.PixelPetScene", true);
            Type skin = assembly.GetType("WheekKeys.PetSkin", true);
            foreach (object coat in (Array)skin.GetField("All").GetValue(null))
            {
                object scene = New(pixel); pixel.GetProperty("Skin").SetValue(scene, coat, null);
                CheckActivity(scene);
            }
            CheckMouseRecovery(pixel);
            CheckStartleRendering(pixel);
            object p = New(pixel); ForceSleep(p); double last = Last(p);
            Call(p, "LookAt", new Point(10, 10)); Check(Last(p) == last, "gaze polling is not activity");
            pixel.GetMethod("AdvanceBlink", Private).Invoke(p, new object[] { Now(p) });
            Check(Last(p) == last, "automatic pixel blink is not activity");
            object friend = New(pixel); pixel.GetProperty("Skin").SetValue(friend, ((Array)skin.GetField("All").GetValue(null)).GetValue(6), null);
            ForceSleep(p); ForceSleep(friend); Call(p, "MoveMouse", 3.0, 2.0);
            Check(Progress(p) == 0 && Progress(friend) == 1, "local mouse does not wake peer scene");
            Call(friend, "PressKey", 30); Check(Progress(friend) == 0, "received peer key wakes peer scene");
            foreach (bool pixelStyle in new[] { true })
            {
                object scene = New(pixel);
                scene.GetType().GetProperty("ShowKeyLabels").SetValue(scene, false, null);
                Check(Marks(scene, pixelStyle) == 0, "awake has no sleep marks");
                ForceSleep(scene); Check(Marks(scene, pixelStyle) > 0, "sleep marks render with bubbles disabled");
                Call(scene, "MoveMouse", 0.1, 0.1);
                Check(Progress(scene) == 0 && Marks(scene, pixelStyle) > 0, "wake starts while sleep marks fade");
                Sleep(scene).GetType().GetField("wakeStarted", Private).SetValue(Sleep(scene), Now(Renderer(scene)) - 421);
                Check(Marks(scene, pixelStyle) == 0, "completed wake removes sleep marks");
            }
            var root = (FrameworkElement)New(pixel);
            using (var host = new HwndSource(new HwndSourceParameters("Sleep lifecycle test") { WindowStyle = unchecked((int)0x80000000), Width = 400, Height = 308 }))
            {
                host.RootVisual = root; root.Measure(new Size(400, 308)); root.Arrange(new Rect(0, 0, 400, 308)); Pump();
                Check(Timer(root, "gaze"), "pixel gaze timer starts");
                Call(root, "MoveMouse", 0.1, 0.1); Check(Timer(root, "animation"), "pixel animation starts on activity");
                host.RootVisual = null; Pump();
                Check(!Timer(root, "gaze") && !Timer(root, "animation"), "unloaded timers stop");
            }
            Console.WriteLine("PASS: pixel and drawn coats sleep/wake, gentle mouse reach/click/recovery, Z rendering, independent peers, loaded/unloaded timers");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            foreach (object scene in created)
                foreach (object target in new[] { scene, Renderer(scene) })
                    foreach (string name in new[] { "animation", "blinkTimer", "sleepTimer", "gaze" })
                    {
                        FieldInfo timer = target.GetType().GetField(name, Private);
                        if (timer != null) ((DispatcherTimer)timer.GetValue(target)).Stop();
                    }
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }
}
