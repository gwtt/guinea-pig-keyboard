using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static class FriendDesktopSmokeTest
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    private static object Field(object o, string name) { return o.GetType().GetField(name, Private).GetValue(o); }
    private static object Get(object o, string name) { return o.GetType().GetProperty(name).GetValue(o, null); }
    private static void Call(object o, string method, params object[] args) { o.GetType().GetMethod(method).Invoke(o, args); }
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
    private static void Pump(Window owner) { owner.UpdateLayout(); DispatcherFrame frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; })); Dispatcher.PushFrame(frame); }
    private static object First(IDictionary items) { foreach (DictionaryEntry item in items) return item.Value; return null; }

    private static bool HasText(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing) return true;
        var group = drawing as DrawingGroup;
        if (group == null) return false;
        foreach (Drawing child in group.Children)
            if (HasText(child)) return true;
        return false;
    }

    private static DrawingGroup DrawScene(FrameworkElement scene, object stroke)
    {
        var clock = (Stopwatch)Field(scene, "clock");
        stroke.GetType().GetField("Started").SetValue(stroke, clock.Elapsed.TotalMilliseconds - 100);
        var drawing = new DrawingGroup();
        using (DrawingContext dc = drawing.Open()) scene.GetType().GetMethod("OnRender", Private).Invoke(scene, new object[] { dc });
        return drawing;
    }

    private static void CheckBubbleEdges(Drawing drawing, int width, object desk)
    {
        double scale = width / 400.0;
        int height = (int)Math.Ceiling(308 * scale);
        var visual = new DrawingVisual();
        RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new int[width * height];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var paper = ((SolidColorBrush)desk.GetType().GetField("PixelBubblePaper").GetValue(desk)).Color;
        var outline = ((SolidColorBrush)desk.GetType().GetField("PixelBubbleLine").GetValue(desk)).Color;
        int paperPixel = paper.A << 24 | paper.R << 16 | paper.G << 8 | paper.B;
        int outlinePixel = outline.A << 24 | outline.R << 16 | outline.G << 8 | outline.B;
        int outlinedRows = 0;
        for (int y = (int)(125 * scale); y < (int)(185 * scale); y++)
        {
            int row = y * width;
            int left = Array.IndexOf(pixels, paperPixel, row + 1, width - 2);
            if (left < 0) continue;
            int right = Array.LastIndexOf(pixels, paperPixel, row + width - 2, width - 2);
            if (right - left >= 18 && pixels[left - 1] == outlinePixel && pixels[right + 1] == outlinePixel) outlinedRows++;
        }
        Check(outlinedRows >= 5, "50 percent bubble has opaque left and right edges and room for text");
    }

    private static Rect CheckNameAboveHead(Window window, FrameworkElement scene, TextBlock label)
    {
        Point anchor = (Point)scene.GetType().GetProperty("HeadTop", Private).GetValue(scene, null);
        Point head = scene.TransformToAncestor(window).Transform(anchor);
        Rect bounds = label.TransformToAncestor(window).TransformBounds(new Rect(label.RenderSize));
        Check(Math.Abs(bounds.Left + bounds.Width / 2 - head.X) < 0.5, "friend name centered over head");
        Check(bounds.Top >= 0 && Math.Abs(head.Y - bounds.Bottom - 4) < 0.5, "friend name above head within window");
        return bounds;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Assembly app = Assembly.LoadFile(System.IO.Path.GetFullPath(args[0]));
        object source = Activator.CreateInstance(app.GetType("WheekKeys.PixelPetScene"), true);
        object network = Activator.CreateInstance(app.GetType("WheekKeys.LocalPeers"), true);
        Viewbox viewport = new Viewbox { Child = (UIElement)source, Stretch = Stretch.Uniform };
        Window owner = new Window { Width = 310, Height = 310.0 * 308 / 400, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, Opacity = 0, Left = 700, Top = 400, Content = viewport };
        object stage = Activator.CreateInstance(app.GetType("WheekKeys.FriendDesktop"), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] {owner,viewport}, null);
        MethodInfo read = network.GetType().GetMethod("ReadPeer",Private);
        const string id = "11111111111111111111111111111111";
        try
        {
            owner.Show(); Pump(owner);
            read.Invoke(network,new object[]{"WK1|"+id+"|tricolor|100|20",1.0});
            string nickname = (string)app.GetType("WheekKeys.PetName").GetMethod("Encode").Invoke(null, new object[] { "像素黄白" });
            read.Invoke(network,new object[]{"WN1|"+id+"|"+nickname,1.0});
            read.Invoke(network,new object[]{"WS1|"+id+"|pixel-tricolor|1",1.0});
            Call(stage,"Update",network,1.0,false,true); Pump(owner);
            IDictionary windows = (IDictionary)Field(stage,"windows");
            Check(windows.Count==1,"one peer window created");
            object wrapper = First(windows); Window friend=(Window)Field(wrapper,"window");
            var remote=(FrameworkElement)Field(wrapper,"scene"); Viewbox remoteView=(Viewbox)Field(wrapper,"box");
            var label=(TextBlock)Field(wrapper,"label");
            Check((bool)Field(source,"followCursor") && !(bool)Field(remote,"followCursor"),"only the local pet follows the cursor");
            Check((Vector)Field(remote,"gazeOffset")==new Vector() && ((DispatcherTimer)Field(remote,"gaze")).IsEnabled,
                "friend gaze stays centered and can blink");
            foreach(double width in new[]{180.0,310.0,600.0})
            {
                owner.Width=width; owner.Height=width*308/400; Pump(owner);
                Call(stage,"Update",network,1.1,false,true); friend.UpdateLayout(); Pump(owner);
                Rect ownBounds=((Visual)source).TransformToAncestor(viewport).TransformBounds(new Rect(0,0,400,308));
                Rect remoteBounds=((Visual)remote).TransformToAncestor(remoteView).TransformBounds(new Rect(0,0,400,308));
                Check(Math.Abs(ownBounds.Width-remoteBounds.Width)<0.01 && Math.Abs(ownBounds.Height-remoteBounds.Height)<0.01,"matching rendered size "+width);
                CheckNameAboveHead(friend, remote, label);
                Check(friend.IsVisible,"peer visible "+width);
                friend.Opacity=0;
            }
            Rect restingName = CheckNameAboveHead(friend, remote, label);
            FieldInfo grip = remote.GetType().GetField("mouseGrip", Private);
            grip.SetValue(remote, 1.0); remote.InvalidateVisual(); Pump(friend);
            Check(CheckNameAboveHead(friend, remote, label).Left < restingName.Left - 10, "name moves with the animated head");
            grip.SetValue(remote, 0.0); remote.InvalidateVisual(); Pump(friend);
            read.Invoke(network,new object[]{"WS1|"+id+"|drawn-tricolor|1",1.2});
            Call(stage,"Update",network,1.2,false,true); Pump(owner);
            Check((string)Get(remote,"Skin").GetType().GetField("Id").GetValue(Get(remote,"Skin")) == "pixel-tricolor", "legacy drawn friend appearance maps to pixel");
            CheckNameAboveHead(friend, remote, label);
            read.Invoke(network,new object[]{"WA1|"+id+"|1|k30",1.2});
            Call(stage,"Update",network,1.2,true,false); Pump(owner);
            object left=Field(remote,"left"),right=Field(remote,"right");
            Check((int)left.GetType().GetField("Scan").GetValue(left)==30 || (int)right.GetType().GetField("Scan").GetValue(right)==30,"network action reaches rendered hand");
            Check(!(bool)Get(remote,"ShowKeyLabels"),"bubble toggle propagated");
            Check(label.Visibility==Visibility.Visible && label.Text=="像素黄白","desktop label is just the friend name");
            read.Invoke(network, new object[] { "WA1|" + id + "|2|k511", 1.3 });
            Call(stage, "Update", network, 1.3, true, true);
            object anonymousStroke = (int)left.GetType().GetField("Scan").GetValue(left) == 511 ? left : right;
            Check((int)anonymousStroke.GetType().GetField("Scan").GetValue(anonymousStroke) == 511, "anonymous key reaches rendered hand");
            owner.Width = 155; owner.Height = 155 * 308.0 / 400;
            Pump(owner); Call(stage, "Update", network, 1.3, true, true); Pump(owner);
            var bubbleDrawing = DrawScene(remote, anonymousStroke);
            Check(HasText(bubbleDrawing), "anonymous friend key renders bubble text");
            CheckBubbleEdges(bubbleDrawing, 155, Get(remote, "Desk"));
            Call(stage, "Update", network, 1.3, true, false);
            Check(!HasText(DrawScene(remote, anonymousStroke)), "disabled bubbles suppress anonymous text");
            read.Invoke(network,new object[]{"WS1|"+id+"|pixel-tricolor|0",1.3});
            Call(stage,"Update",network,1.3,true,false);
            Check(label.Text=="像素黄白 · 已暂停动作","paused sharing status remains visible");
            IntPtr handle=((HwndSource)PresentationSource.FromVisual(friend)).Handle;
            Check((GetWindowLong(handle,-20)&0x20)!=0,"click-through follows owner");
            owner.Hide(); Pump(owner); Check(!friend.IsVisible,"peer hides with owner");
            owner.Show(); Pump(owner); Check(friend.IsVisible,"peer returns with owner");
            viewport.Visibility = Visibility.Collapsed;
            Call(stage, "Update", network, 1.3, false, true); Pump(owner);
            Check(!friend.IsVisible, "zero size hides the friend window");
            viewport.Visibility = Visibility.Visible;
            Pump(owner); Call(stage, "Update", network, 1.3, false, true); Pump(owner);
            Check(friend.IsVisible && viewport.ActualWidth > 0 && viewport.ActualHeight > 0, "positive size restores both viewports");
            viewport.Width = 3.1;
            viewport.Height = 3.1 * 308 / 400;
            owner.Width = viewport.Width;
            owner.Height = viewport.Height;
            Pump(owner); Call(stage, "Update", network, 1.3, false, true); Pump(owner);
            Rect tinyFriend = remote.TransformToAncestor(remoteView).TransformBounds(new Rect(0, 0, 400, 308));
            Check(Math.Abs(viewport.ActualWidth - 3.1) < 0.01 && Math.Abs(tinyFriend.Width - 3.1) < 0.01, "one percent renders independent of the native window minimum");
            friend.Close(); Call(stage,"Update",network,1.3,false,true); Pump(owner);
            wrapper=First(windows); friend=(Window)Field(wrapper,"window"); Check(friend.IsVisible,"closed peer recreated without exception"); friend.Opacity=0;
            ((IDisposable)network).Dispose(); Call(stage,"Update",network,1.4,false,true); Pump(owner); Check(windows.Count==0,"leave closes peer window");
            ((IDisposable)stage).Dispose(); owner.Close();
            Console.WriteLine("PASS: friend scale and zero-size recovery, name positioning, anonymous key bubbles, click-through, hide/show, recreate, leave cleanup");
            return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { ((IDisposable)stage).Dispose(); ((IDisposable)network).Dispose(); owner.Close(); }
    }
}
