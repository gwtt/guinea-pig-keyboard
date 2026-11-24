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

internal static class MouthSceneSmokeTest
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object o, string name) { return o.GetType().GetField(name, Private).GetValue(o); }
    private static object Renderer(object scene) { return scene; }
    private static double Now(object scene) { return ((Stopwatch)Field(scene, "clock")).Elapsed.TotalMilliseconds; }
    private static void Call(object o, string method, params object[] args) { o.GetType().GetMethod(method).Invoke(o, args); }
    private static int Frame(object scene) { return (int)Field(Renderer(scene), "mouth").GetType().GetProperty("Frame").GetValue(Field(Renderer(scene), "mouth"), null); }
    private static void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); }
    private static byte[] Render(object scene)
    {
        var element = (FrameworkElement)scene;
        ((FrameworkElement)Renderer(scene)).InvalidateVisual();
        element.InvalidateVisual(); element.Measure(new Size(400, 308)); element.Arrange(new Rect(0, 0, 400, 308)); element.UpdateLayout();
        var image = new RenderTargetBitmap(400, 308, 96, 96, PixelFormats.Pbgra32); image.Render(element);
        byte[] pixels = new byte[400 * 308 * 4]; image.CopyPixels(pixels, 400 * 4, 0);
        return pixels;
    }
    private static void SetMouth(object scene, bool moving)
    {
        object renderer = Renderer(scene), mouth = Field(renderer, "mouth"); double now = Now(renderer);
        if (moving) { Call(mouth, "Trigger", now - 210); Call(mouth, "Advance", now, false); }
        else Call(mouth, "Reset", now);
    }
    private static int Differences(byte[] a, byte[] b)
    {
        int changed = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) changed++; return changed;
    }
    private static int PinkPixels(byte[] pixels, int width, Rect region)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int x = (i / 4) % width, y = i / 4 / width;
            if (region.Contains(new Point(x, y)) && pixels[i + 3] != 0 && pixels[i + 2] == 237 && pixels[i + 1] == 160 && pixels[i] == 140) count++;
        }
        return count;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        try
        {
            Assembly assembly = Assembly.LoadFile(Path.GetFullPath(args[0]));
            Type pet = assembly.GetType("WheekKeys.PixelPetScene", true), skins = assembly.GetType("WheekKeys.PetSkin", true);
            foreach (object skin in (Array)skins.GetField("All").GetValue(null))
            {
                if ((bool)skins.GetProperty("IsDrawn").GetValue(skin, null)) continue;
                object scene = Activator.CreateInstance(pet, true); pet.GetProperty("Skin").SetValue(scene, skin, null);
                object renderer = Renderer(scene); bool pixel = true;
                string id = (string)skins.GetField("Id").GetValue(skin);
                byte[] resting = Render(scene); SetMouth(scene, true); byte[] smacking = Render(scene);
                int changed = Differences(resting, smacking);
                object art = Field(Field(renderer, "Eyes"), "_mouth");
                byte[][] raw = (byte[][])art.GetType().GetField("Pixels").GetValue(art);
                int sourceWidth = ((BitmapSource[])art.GetType().GetField("Frames").GetValue(art))[0].PixelWidth;
                Rect sourceRegion = (Rect)Field(art, "region");
                Check(PinkPixels(raw[0], sourceWidth, sourceRegion) > 0, id + " open mouth has visible pink tongue");
                int restingPink = PinkPixels(raw[0], sourceWidth, sourceRegion);
                bool closes = false;
                for (int frame = 1; frame < raw.Length; frame++) if (PinkPixels(raw[frame], sourceWidth, sourceRegion) < restingPink) closes = true;
                Check(closes, id + " mouth animation closes between frames");
                object eyeArt = Field(renderer, "Eyes");
                BitmapSource[] eyeFrames = (BitmapSource[])eyeArt.GetType().GetField("_eyes", Private).GetValue(eyeArt);
                byte[] eyePixels = new byte[eyeFrames[0].PixelWidth * eyeFrames[0].PixelHeight * 4];
                new FormatConvertedBitmap(eyeFrames[0], PixelFormats.Pbgra32, null, 0).CopyPixels(eyePixels, eyeFrames[0].PixelWidth * 4, 0);
                HashSet<string> eyeColors = new HashSet<string>();
                for (int i = 0; i < eyePixels.Length; i += 4) if (eyePixels[i + 3] != 0) eyeColors.Add(eyePixels[i] + "," + eyePixels[i + 1] + "," + eyePixels[i + 2]);
                Check(eyeColors.Count == 1, id + " open eye uses one visible color");
                Check(Frame(scene) == 3 && changed > 0, id + " mouth visibly changes (frame " + Frame(scene) + ", pixels " + changed + ", source " + Differences(raw[0], raw[3]) + ")");
                Check(Differences(raw[0], raw[3]) > 0, id + " source mouth pixels move");
                for (int i = 0; i < raw[0].Length; i += 4)
                {
                    Check(raw[0][i + 3] == raw[3][i + 3], id + " source alpha preserved");
                    int x = (i / 4) % sourceWidth, y = i / 4 / sourceWidth;
                    if (sourceRegion.Contains(new Point(x, y))) continue;
                    for (int c = 0; c < 4; c++) Check(raw[0][i + c] == raw[3][i + c], id + " source art outside mouth preserved");
                }
                Rect area = new Rect(198, 112, 50, 42);
                for (int y = 0; y < 308; y++) for (int x = 0; x < 400; x++)
                {
                    int index = (y * 400 + x) * 4;
                    Check(resting[index + 3] == smacking[index + 3], id + " alpha silhouette unchanged");
                    if (area.Contains(new Point(x, y))) continue;
                    for (int channel = 0; channel < 4; channel++) Check(resting[index + channel] == smacking[index + channel], id + " pixels outside mouth unchanged at " + x + "," + y);
                }
                SetMouth(scene, false); Check(Differences(resting, Render(scene)) == 0, id + " returns to original drawing");
                if (pixel)
                {
                    renderer.GetType().GetField("headOffset", Private).SetValue(renderer, new Vector(1, -1));
                    byte[] turned = Render(scene); SetMouth(scene, true);
                    Check(Differences(turned, Render(scene)) > 0, id + " mouth follows turned head");
                }
                object sleep = Field(renderer, "sleep");
                sleep.GetType().GetField("lastActivity", Private).SetValue(sleep, Now(renderer) - 32000);
                SetMouth(scene, true);
                Check((double)sleep.GetType().GetMethod("Progress").Invoke(sleep, new object[] { Now(renderer) }) == 1, id + " mouth does not wake pig");
                Call(scene, "MoveMouse", 0.01, 0.01); Check(Frame(scene) == 0, id + " startle uses original mouth");
                foreach (string timerName in new[] { "animation", "blinkTimer", "sleepTimer", "gaze" })
                {
                    FieldInfo timer = renderer.GetType().GetField(timerName, Private);
                    if (timer != null) ((DispatcherTimer)timer.GetValue(renderer)).Stop();
                }
            }
            Console.WriteLine("PASS: all pixel mouths animate, original art/alpha/nose protected, head turns, sleep and startle preserved");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.GetBaseException().Message); return 1; }
        finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
    }
}
