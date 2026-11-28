using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

internal static class SleepAnimationSmokeTest
{
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("Failed: " + name);
    }
    private static DrawingGroup Render(Type type, double now, bool pixel)
    {
        return Render(Activator.CreateInstance(type, true), now, pixel);
    }
    private static DrawingGroup Render(object instance, double now, bool pixel)
    {
        DrawingGroup group = new DrawingGroup();
        using (DrawingContext dc = group.Open())
            instance.GetType().GetMethod("Draw").Invoke(instance, new object[] { dc, new Point(250, 90), now, pixel });
        return group;
    }
    private static bool Contains<T>(Drawing drawing) where T : Drawing
    {
        if (drawing is T) return true;
        DrawingGroup group = drawing as DrawingGroup;
        if (group != null) foreach (Drawing child in group.Children) if (Contains<T>(child)) return true;
        return false;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error.GetBaseException().Message); return 1; }
    }
    private static int Run(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: SleepAnimationSmokeTest.exe WheekKeys-Sleep.exe"); return 2; }
        Assembly assembly = Assembly.LoadFile(Path.GetFullPath(args[0]));
        Type type = assembly.GetType("WheekKeys.IdleSleepAnimation", true);
        object animation = Activator.CreateInstance(type, true);
        var delay = type.GetMethod("SetDelay", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (int seconds in new[] { 15, 60, 0 })
        {
            delay.Invoke(animation, new object[] { seconds, 0.0 });
            double when = seconds == 0 ? 600000 : seconds * 1000 + 701;
            Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { when }) == (seconds == 0 ? 0 : 3),
                "configured sleep delay " + seconds);
        }
        delay.Invoke(animation, new object[] { 30, 0.0 });
        Check(Convert.ToDouble(type.GetField("WakeMilliseconds").GetValue(null)) == 420, "wake duration");
        Check(Convert.ToDouble(type.GetField("MotionWakeMilliseconds").GetValue(null)) == 1600, "motion wake duration");
        Check(Math.Abs(Convert.ToDouble(type.GetMethod("MotionReadiness").Invoke(animation, new object[] { 0.0 })) - 1.0) < 0.0001, "never-slept motion readiness");
        Vector freshStartle = (Vector)type.GetMethod("StartleOffset").Invoke(animation, new object[] { 0.0 });
        Check(freshStartle.Length == 0, "never-slept startle is neutral");
        Check(Math.Abs(Convert.ToDouble(type.GetMethod("EyeScale").Invoke(animation, new object[] { 0.0 })) - 1.0) < 0.0001, "never-slept eye scale is neutral");
        Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { 29999.9 }) == 0, "not asleep before delay");
        Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { 30000.0 }) == 0, "delay boundary");
        Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { 30000.1 }) == 1, "closing starts");
        Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { 30245.0 }) == 2, "middle closing stage");
        Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { 30700.0 }) == 3, "fully closed boundary");
        type.GetMethod("RecordActivity").Invoke(animation, new object[] { 31000.0 });
        Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { 31000.0 }) == 0, "activity wakes immediately");
        Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { 60999.0 }) == 0, "activity restarts delay");
        Check((int)type.GetMethod("EyeStage").Invoke(animation, new object[] { 61700.0 }) == 3, "reaches closed after restarted delay");
        Check(Math.Abs(Convert.ToDouble(type.GetMethod("Progress").Invoke(animation, new object[] { 61700.0 })) - 1.0) < 0.0001, "sleep progress reaches one");
        Check(Math.Abs(Convert.ToDouble(type.GetMethod("Expression").Invoke(animation, new object[] { 61700.0 })) - 1.0) < 0.0001, "sleep expression reaches one");
        Check(!(bool)type.GetMethod("IsWaking").Invoke(animation, new object[] { 61700.0 }), "not waking before activity");
        type.GetMethod("RecordActivity").Invoke(animation, new object[] { 61800.0 });
        Check(Convert.ToDouble(type.GetMethod("Progress").Invoke(animation, new object[] { 61800.0 })) == 0, "activity resets sleep progress immediately");
        Check((bool)type.GetMethod("IsWaking").Invoke(animation, new object[] { 61800.0 }), "activity starts waking");
        Check(Convert.ToDouble(type.GetMethod("MotionReadiness").Invoke(animation, new object[] { 61800.0 })) == 0, "motion starts unready");
        Check(Convert.ToDouble(type.GetMethod("MotionReadiness").Invoke(animation, new object[] { 61950.0 })) == 0, "motion remains unready for first 150ms");
        Vector wakeStartle = (Vector)type.GetMethod("StartleOffset").Invoke(animation, new object[] { 61865.0 });
        Check(wakeStartle.Y < 0 && Math.Abs(wakeStartle.X) <= 2.1 && Math.Abs(wakeStartle.Y) <= 7.1, "wake startle lifts and jitters within bounds");
        Check(Convert.ToDouble(type.GetMethod("EyeScale").Invoke(animation, new object[] { 61880.0 })) > 1.0, "wake startle briefly widens eyes");
        double wakeStartExpression = Convert.ToDouble(type.GetMethod("Expression").Invoke(animation, new object[] { 61800.0 }));
        Check(wakeStartExpression > 0 && wakeStartExpression <= 1, "waking starts from sleepy expression");
        Check(!Render(animation, 61800, false).Bounds.IsEmpty, "zzz remain at wake start");
        type.GetMethod("RecordActivity").Invoke(animation, new object[] { 61900.0 });
        Check(Convert.ToDouble(type.GetMethod("Expression").Invoke(animation, new object[] { 61900.0 })) <= wakeStartExpression, "repeated activity does not restart wake transition");
        double midMotionReadiness = Convert.ToDouble(type.GetMethod("MotionReadiness").Invoke(animation, new object[] { 62600.0 }));
        Check(midMotionReadiness > 0 && midMotionReadiness < 1, "motion readiness eases through midpoint");
        Check(Math.Abs(Convert.ToDouble(type.GetMethod("MotionReadiness").Invoke(animation, new object[] { 63400.0 })) - 1.0) < 0.0001, "repeated activity does not restart motion recovery");
        Vector repeatedStartle = (Vector)type.GetMethod("StartleOffset").Invoke(animation, new object[] { 62220.0 });
        Check(repeatedStartle.Length == 0, "repeated activity does not retrigger startle");
        Check(Math.Abs(Convert.ToDouble(type.GetMethod("EyeScale").Invoke(animation, new object[] { 62220.0 })) - 1.0) < 0.0001, "eye scale returns to neutral after wake");
        Check((bool)type.GetMethod("IsWaking").Invoke(animation, new object[] { 61900.0 }), "repeated activity keeps wake transition");
        Check(!Render(animation, 62219, false).Bounds.IsEmpty, "zzz fade during wake transition");
        Check(Render(animation, 62220, false).Bounds.IsEmpty, "zzz fade ends after wake duration");
        Check(Convert.ToDouble(type.GetMethod("Expression").Invoke(animation, new object[] { 62220.0 })) == 0, "wake ends at open expression");
        Check(!(bool)type.GetMethod("IsWaking").Invoke(animation, new object[] { 62220.0 }), "wake transition completes");
        object partialSleep = Activator.CreateInstance(type, true);
        type.GetMethod("RecordActivity").Invoke(partialSleep, new object[] { 0.0 });
        type.GetMethod("RecordActivity").Invoke(partialSleep, new object[] { 30245.0 });
        Check(Render(partialSleep, 30245, false).Bounds.IsEmpty, "partially sleepy state has no zzz");
        type.GetMethod("RecordActivity").Invoke(partialSleep, new object[] { 30246.0 });
        Check(Render(partialSleep, 30246, false).Bounds.IsEmpty, "waking before fully asleep does not show zzz");
        foreach (bool pixel in new[] { false, true })
        {
            DrawingGroup awake = Render(type, 20000, pixel);
            Check(awake.Bounds.IsEmpty, "awake has no sleep marks");
            DrawingGroup asleep = Render(type, 34000, pixel);
            Rect bounds = asleep.Bounds;
            Check(!bounds.IsEmpty && bounds.Left >= 250 && bounds.Right <= 286.1 && bounds.Bottom <= 90 && bounds.Top >= 50, "marks stay right and above anchor");
            Check(bounds.Left >= 250 && bounds.Top < 90, "bounds include anchor-relative region");
            DrawingGroup later = Render(type, 34500, pixel);
            Check(!later.Bounds.IsEmpty && (Math.Abs(later.Bounds.Top - bounds.Top) > 0.01 || Math.Abs(later.Bounds.Left - bounds.Left) > 0.01), "marks move with time");
            DrawingGroup otherStyle = Render(type, 34000, !pixel);
            Check(asleep.Children.Count > 0 && otherStyle.Children.Count > 0, "both render styles draw marks");
            Check(pixel ? Contains<GeometryDrawing>(asleep) && !Contains<GlyphRunDrawing>(asleep)
                : Contains<GlyphRunDrawing>(asleep), "pixel and original shapes differ");
        }
        Console.WriteLine("Sleep animation smoke test passed.");
        return 0;
    }
}
