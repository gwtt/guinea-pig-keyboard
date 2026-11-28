using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WheekKeys
{
    // Only observes digitizer pen collections; does not suppress mouse input or retain a path.
    internal sealed class PenInput : IDisposable
    {
        internal struct Sample
        {
            public double Dx, Dy;
            public bool InRange, TipDown;
            public double? Pressure;
        }
        private sealed class Device
        {
            public byte[] Data;
            public ValueCaps X, Y, Pressure;
            public bool HasPressure;
            public MotionState Motion;
        }
        private const uint Input = 0x10000003, PreparsedData = 0x20000005;
        private const int Success = 0x00110000;
        private readonly HwndSource source;
        private readonly Action<Sample> changed;
        private readonly HwndSourceHook hook;
        private readonly Dictionary<IntPtr, Device> devices = new Dictionary<IntPtr, Device>();
        private bool registered, disposed;
        private Device current;

        internal PenInput(HwndSource source, Action<Sample> changed)
        {
            if (source == null || source.IsDisposed || source.Handle == IntPtr.Zero) throw new ArgumentException("A live window is required.");
            if (changed == null) throw new ArgumentNullException("changed");
            this.source = source; this.changed = changed;
            hook = WindowProc;
            source.AddHook(hook);
            RawDevice[] registrations = Registrations(0x100 | 0x2000, source.Handle);
            registered = RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf(typeof(RawDevice)));
            if (!registered) source.RemoveHook(hook);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (registered)
            {
                RawDevice[] registrations = Registrations(1, IntPtr.Zero);
                RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf(typeof(RawDevice)));
                if (!source.IsDisposed) source.RemoveHook(hook);
            }
            devices.Clear();
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (disposed) return IntPtr.Zero;
            if (message == 0x00FE && wParam.ToInt32() == 2)
            {
                Device removed;
                if (devices.TryGetValue(lParam, out removed) && removed != null && removed.Motion.Active && current == removed)
                {
                    current = null;
                    changed(new Sample());
                }
                devices.Remove(lParam);
            }
            if (message != 0x00FF) return IntPtr.Zero;
            uint length = 0, headerSize = (uint)Marshal.SizeOf(typeof(RawHeader));
            RawHeader incoming;
            uint headerLength = headerSize;
            if (GetRawInputHeader(lParam, 0x10000005, out incoming, ref headerLength, headerSize) != headerSize || incoming.Type != 2) return IntPtr.Zero;
            if (GetRawInputData(lParam, Input, IntPtr.Zero, ref length, headerSize) != 0 || length < headerSize + 8 || length > 1024 * 1024) return IntPtr.Zero;
            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (GetRawInputData(lParam, Input, buffer, ref length, headerSize) != length) return IntPtr.Zero;
                RawHeader header = (RawHeader)Marshal.PtrToStructure(buffer, typeof(RawHeader));
                if (header.Type != 2 || header.Device == IntPtr.Zero) return IntPtr.Zero;
                Device device;
                if (!devices.TryGetValue(header.Device, out device))
                {
                    device = OpenDevice(header.Device);
                    devices[header.Device] = device;
                }
                if (device == null) return IntPtr.Zero;
                int offset = (int)headerSize;
                uint reportLength = unchecked((uint)Marshal.ReadInt32(buffer, offset));
                uint count = unchecked((uint)Marshal.ReadInt32(buffer, offset + 4));
                if (reportLength == 0 || count == 0 || reportLength > 65535 || (ulong)reportLength * count > length - headerSize - 8) return IntPtr.Zero;
                byte[] report = new byte[reportLength];
                for (uint i = 0; i < count; i++)
                {
                    Marshal.Copy(IntPtr.Add(buffer, offset + 8 + (int)(i * reportLength)), report, 0, report.Length);
                    Process(device, report);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return IntPtr.Zero;
        }

        private void Process(Device device, byte[] report)
        {
            ushort[] usages = new ushort[128];
            uint count = (uint)usages.Length;
            if (HidP_GetUsages(0, 0x0d, 0, usages, ref count, device.Data, report, (uint)report.Length) != Success) return;
            bool range = false, tip = false;
            for (int i = 0; i < count; i++) { range |= usages[i] == 0x32; tip |= usages[i] == 0x42; }
            if (!range)
            {
                bool wasActive = device.Motion.Active;
                Advance(ref device.Motion, false, false, 0, 0, null, 1, 1);
                if (wasActive && current == device) { current = null; changed(new Sample()); }
                return;
            }
            double x, y;
            if (!Value(device, device.X, 1, 0x30, report, out x) || !Value(device, device.Y, 1, 0x31, report, out y)) return;
            double width = Math.Max(1, GetSystemMetrics(78) - 1), height = Math.Max(1, GetSystemMetrics(79) - 1);
            double pressure;
            double? pressureValue = device.HasPressure && Value(device, device.Pressure, 0x0d, 0x30, report, out pressure) ? (double?)pressure : null;
            Sample sample = Advance(ref device.Motion, true, tip, x, y, pressureValue, width, height);
            current = device;
            changed(sample);
        }

        internal struct MotionState
        {
            public bool Active;
            public double X, Y;
        }
        internal static Sample Advance(ref MotionState state, bool inRange, bool tip, double x, double y, double? pressure, double width, double height)
        {
            Sample sample = new Sample { InRange = inRange, TipDown = inRange && tip };
            if (!inRange) { state.Active = false; return sample; }
            if (state.Active) { sample.Dx = (x - state.X) * width; sample.Dy = (y - state.Y) * height; }
            state.Active = true; state.X = x; state.Y = y;
            sample.Pressure = pressure;
            return sample;
        }

        private static bool Value(Device device, ValueCaps cap, ushort page, ushort usage, byte[] report, out double value)
        {
            value = 0;
            uint raw;
            if (HidP_GetUsageValue(0, page, cap.LinkCollection, usage, out raw, device.Data, report, (uint)report.Length) != Success) return false;
            long number = raw;
            if (cap.LogicalMin < 0 && cap.BitSize > 0 && cap.BitSize < 32 && (raw & (1u << (cap.BitSize - 1))) != 0) number = (long)raw - (1L << cap.BitSize);
            else if (cap.LogicalMin < 0 && cap.BitSize == 32) number = unchecked((int)raw);
            value = Math.Max(0, Math.Min(1, (number - (double)cap.LogicalMin) / (cap.LogicalMax - (double)cap.LogicalMin)));
            return true;
        }

        private static Device OpenDevice(IntPtr handle)
        {
            uint size = 0;
            if (GetRawInputDeviceInfo(handle, PreparsedData, IntPtr.Zero, ref size) != 0 || size == 0 || size > 1024 * 1024) return null;
            IntPtr ptr = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputDeviceInfo(handle, PreparsedData, ptr, ref size) == uint.MaxValue) return null;
                byte[] data = new byte[size]; Marshal.Copy(ptr, data, 0, data.Length);
                Caps caps;
                if (HidP_GetCaps(data, out caps) != Success || caps.UsagePage != 0x0d || (caps.Usage != 1 && caps.Usage != 2) || caps.ValueCount == 0 || caps.ValueCount > 1024) return null;
                if (caps.ButtonCount == 0 || caps.ButtonCount > 1024) return null;
                ushort buttonCount = caps.ButtonCount;
                ValueCaps[] buttons = new ValueCaps[buttonCount];
                if (HidP_GetButtonCaps(0, buttons, ref buttonCount, data) != Success) return null;
                bool tip = false, range = false;
                for (int i = 0; i < buttonCount; i++) { tip |= Contains(buttons[i], 13, 0x42); range |= Contains(buttons[i], 13, 0x32); }
                if (!tip || !range) return null;
                ushort count = caps.ValueCount;
                ValueCaps[] values = new ValueCaps[count];
                if (HidP_GetValueCaps(0, values, ref count, data) != Success) return null;
                Device result = new Device { Data = data };
                bool x = false, y = false;
                for (int i = 0; i < count; i++)
                {
                    ValueCaps cap = values[i];
                    if (cap.Absolute == 0 || cap.LogicalMax <= cap.LogicalMin) continue;
                    if (Contains(cap, 1, 0x30)) { result.X = cap; x = true; }
                    if (Contains(cap, 1, 0x31)) { result.Y = cap; y = true; }
                    if (Contains(cap, 0x0d, 0x30)) { result.Pressure = cap; result.HasPressure = true; }
                }
                return x && y ? result : null;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        private static bool Contains(ValueCaps cap, ushort page, ushort usage)
        { return cap.UsagePage == page && (cap.Range != 0 ? cap.UsageMin <= usage && usage <= cap.UsageMax : cap.UsageMin == usage); }
        private static RawDevice[] Registrations(uint flags, IntPtr target)
        { return new[] { new RawDevice { Page = 13, Usage = 1, Flags = flags, Target = target }, new RawDevice { Page = 13, Usage = 2, Flags = flags, Target = target } }; }

        [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type, Size; public IntPtr Device, WParam; }
        [StructLayout(LayoutKind.Explicit, Size = 64)] private struct Caps
        { [FieldOffset(0)] public ushort Usage; [FieldOffset(2)] public ushort UsagePage; [FieldOffset(46)] public ushort ButtonCount; [FieldOffset(48)] public ushort ValueCount; }
        [StructLayout(LayoutKind.Explicit, Size = 72)] private struct ValueCaps
        {
            [FieldOffset(0)] public ushort UsagePage; [FieldOffset(6)] public ushort LinkCollection;
            [FieldOffset(12)] public byte Range; [FieldOffset(15)] public byte Absolute; [FieldOffset(18)] public ushort BitSize;
            [FieldOffset(40)] public int LogicalMin; [FieldOffset(44)] public int LogicalMax;
            [FieldOffset(56)] public ushort UsageMin; [FieldOffset(58)] public ushort UsageMax;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", EntryPoint = "GetRawInputData", SetLastError = true)] private static extern uint GetRawInputHeader(IntPtr input, uint command, out RawHeader data, ref uint size, uint headerSize);
        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(byte[] data, out Caps caps);
        [DllImport("hid.dll")] private static extern int HidP_GetButtonCaps(int type, [Out] ValueCaps[] caps, ref ushort count, byte[] data);
        [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, [Out] ValueCaps[] caps, ref ushort count, byte[] data);
        [DllImport("hid.dll")] private static extern int HidP_GetUsages(int type, ushort page, ushort collection, [Out] ushort[] usages, ref uint count, byte[] data, byte[] report, uint length);
        [DllImport("hid.dll")] private static extern int HidP_GetUsageValue(int type, ushort page, ushort collection, ushort usage, out uint value, byte[] data, byte[] report, uint length);
    }
}
