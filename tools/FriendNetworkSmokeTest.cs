using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;

internal static class FriendNetworkSmokeTest
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: FriendNetworkSmokeTest.exe WheekKeys.exe"); return 2; }
        Assembly assembly = Assembly.LoadFile(System.IO.Path.GetFullPath(args[0]));
        Type networkType = assembly.GetType("WheekKeys.LocalPeers", true);
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        object a = Activator.CreateInstance(networkType, true), b = Activator.CreateInstance(networkType, true);
        try
        {
            object host = ConnectTestRoom(networkType, a, true);
            object guest = ConnectTestRoom(networkType, b, false);
            object[] skins = (object[])skinType.GetField("All").GetValue(null);
            object skin = null;
            foreach (object candidate in skins) if ((string)skinType.GetField("Id").GetValue(candidate) == "pixel-longhair-chocolate") skin = candidate;
            Check(skin != null, "test skin exists"); string skinId = "pixel-longhair-chocolate";
            MethodInfo tick = networkType.GetMethod("Tick");
            Action<double, long> tickHost = (now, keys) => {
                tick.Invoke(a, new object[] { now, skin, keys, 7L, "花生" });
                Relay(host, guest);
            };
            Action<double> tickGuest = now => {
                tick.Invoke(b, new object[] { now, skin, 0L, 0L, "小豆" });
                Relay(guest, host);
            };
            tickHost(1.0, 31);
            tickGuest(1.1);
            tickHost(1.1, 31);
            tickGuest(1.13);
            tickHost(1.13, 31);
            tickGuest(1.14);
            IDictionary peers = (IDictionary)networkType.GetProperty("Members").GetValue(b, null);
            Check(peers.Count == 1, "guest admission and room peer sync");
            object peer = null; foreach (DictionaryEntry item in peers) peer = item.Value;
            Check((string)skinType.GetField("Id").GetValue(peer.GetType().GetField("Skin").GetValue(peer)) == skinId, "exact skin update");
            Check((long)peer.GetType().GetField("Keys").GetValue(peer) == 31L && (long)peer.GetType().GetField("Clicks").GetValue(peer) == 7L, "legacy WK1 counters");
            Check((string)peer.GetType().GetField("Nickname").GetValue(peer) == "花生", "WN1 nickname");
            MethodInfo recordTyping = networkType.GetMethod("RecordTyping"), recordClick = networkType.GetMethod("RecordClick");
            recordTyping.Invoke(a, new object[] { 1.2 }); recordTyping.Invoke(a, new object[] { 1.2 });
            recordClick.Invoke(a, new object[] { false, 1.2 }); recordClick.Invoke(a, new object[] { true, 1.2 });
            tickHost(1.2, 31);
            tickGuest(1.2);
            MethodInfo take = peer.GetType().GetMethod("TryTakeAction"); string[] expected = { "k511", "k511", "l", "r" };
            Check(Take(take, peer, 1.2) == expected[0] && Take(take, peer, 1.23) == null, "batch leaves key animation visible");
            Check(Take(take, peer, 1.26) == expected[1] && Take(take, peer, 1.32) == expected[2], "keys and left mouse replay in order");
            Check(Take(take, peer, 1.45) == null && Take(take, peer, 1.51) == expected[3], "clicks have separate animation time");
            Check(Take(take, peer, 1.68) == null, "no extra action while click finishes");

            object reversePeer = null;
            foreach (DictionaryEntry item in (IDictionary)networkType.GetProperty("Members").GetValue(a, null)) reversePeer = item.Value;
            Check(reversePeer != null, "both sides discovered after staggered join");
            MethodInfo read = networkType.GetMethod("ReadPeer", PrivateInstance);
            string session = (string)networkType.GetField("session", PrivateInstance).GetValue(a);
            int queued = QueueCount(peer);
            long nextSequence = (long)peer.GetType().GetField("ActionSequence", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(peer) + 1;
            read.Invoke(b, new object[] { "WA1|" + session + "|" + nextSequence + "|k30", 1.3 });
            read.Invoke(b, new object[] { "WA1|" + session + "|" + nextSequence + "|k31", 1.3 });
            read.Invoke(b, new object[] { "WA1|" + session + "|" + (nextSequence - 1) + "|k31", 1.3 });
            Check(QueueCount(peer) == queued + 1 && Take(take, peer, 1.7) == "k30", "duplicate and out-of-order sequences ignored");
            Reject(read, b, "WA1|ffffffffffffffffffffffffffffffff|9|k30", 1.4, peer, "unknown session");
            Reject(read, b, "WA1|" + session + "|" + (nextSequence + 1) + "|k999", 1.4, peer, "invalid scan");
            Reject(read, b, "WA1|" + session + "|" + (nextSequence + 1) + "|l,l,l,l,l,l,l,l,l,l,l,l,l", 1.4, peer, "too many actions");
            Reject(read, b, "WA1|" + session + "|" + (nextSequence + 1) + "|" + new string('x', 130), 1.4, peer, "oversized message");
            read.Invoke(b, new object[] { "WS1|" + session + "|" + skinId + "|0", 1.5 });
            Check(QueueCount(peer) == 0, "sharing disabled clears queue");
            read.Invoke(b, new object[] { "WA1|" + session + "|3|l", 1.5 });
            Check(QueueCount(peer) == 0, "sharing disabled rejects actions");
            read.Invoke(b, new object[] { "WS1|" + session + "|" + skinId + "|1", 1.6 });
            read.Invoke(b, new object[] { "WA1|" + session + "|4|l", 1.6 });
            Check(Take(take, peer, 2.5) == null, "expired action discarded");
            recordClick.Invoke(b, new object[] { true, 2.5 });
            recordTyping.Invoke(b, new object[] { 2.5 });
            tickGuest(2.5);
            tickHost(2.5, 31);
            Check(Take(take, reversePeer, 2.5) == "r" && Take(take, reversePeer, 2.65) == null &&
                Take(take, reversePeer, 2.7) == "k511", "reverse room click then anonymous tap");
            networkType.GetProperty("ShareActivity").SetValue(a, false, null);
            recordTyping.Invoke(a, new object[] { 2.6 });
            Check(PendingCount(a, networkType) == 0, "local sharing disabled does not queue recording");
            tickHost(2.6, 32);
            tickGuest(2.6);
            Check(!(bool)peer.GetType().GetField("SharingActivity").GetValue(peer), "pause propagates to the other peer");
            Set(networkType, a, "actionSequence", 4L);
            networkType.GetProperty("ShareActivity").SetValue(a, true, null);
            recordTyping.Invoke(a, new object[] { 2.8 });
            tickHost(2.8, 33);
            tickGuest(2.8);
            Check((bool)peer.GetType().GetField("SharingActivity").GetValue(peer) && Take(take, peer, 2.8) == "k511", "resume propagates and the first new action arrives");
            ((IDisposable)b).Dispose(); Check(((IDictionary)networkType.GetProperty("Members").GetValue(b, null)).Count == 0 && QueueCount(peer) == 0, "dispose clears peer state and actions");
            CheckRoomAdmission(assembly);
            Console.WriteLine("PASS: room friend sync, guest admission, room capacity, and public discovery records");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
        finally { ((IDisposable)a).Dispose(); ((IDisposable)b).Dispose(); }
    }

    private static void CheckRoomAdmission(Assembly assembly)
    {
        Type type = assembly.GetType("WheekKeys.RoomMembership", true);
        MethodInfo receive = type.GetMethod("Receive");
        MethodInfo next = type.GetMethod("NextPacket");
        MethodInfo expire = type.GetMethod("Expire");
        PropertyInfo count = type.GetProperty("Count");
        PropertyInfo joined = type.GetProperty("Joined");
        string hostId = 1.ToString("x32"), waitingId = 17.ToString("x32");
        object host = Activator.CreateInstance(type, hostId, true, true, null);
        object waiting = Activator.CreateInstance(type, waitingId, false, true, hostId);
        Action<object, string, double> apply = (member, packet, now) =>
            receive.Invoke(member, new object[] { packet.Split('|'), now });
        for (int i = 2; i <= 17; i++) apply(host, "WJ2|" + i.ToString("x32"), 1.0);
        string full = (string)next.Invoke(host, new object[] { 1.0 });
        Check((int)count.GetValue(host, null) == 16, "room reserves at most 15 guest seats");
        apply(waiting, full, 1.0);
        Check((int)count.GetValue(waiting, null) == 16 && !(bool)joined.GetValue(waiting, null), "17th member must wait");
        for (int i = 3; i <= 16; i++) apply(host, "WJ2|" + i.ToString("x32"), 7.0);
        expire.Invoke(host, new object[] { 11.0 });
        apply(host, "WJ2|" + waitingId, 11.0);
        string replacement = (string)next.Invoke(host, new object[] { 11.0 });
        apply(waiting, replacement, 11.0);
        apply(waiting, full, 11.1);
        Check((bool)joined.GetValue(waiting, null) && (int)count.GetValue(waiting, null) == 16,
            "expired seat can be filled and stale rosters do not revoke admission");
        expire.Invoke(waiting, new object[] { 21.0 });
        Check(!(bool)joined.GetValue(waiting, null), "guest loses admission without host heartbeat");
        Type directory = assembly.GetType("WheekKeys.RoomDirectory", true);
        MethodInfo parse = directory.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
        string advertisement = "WD2|ABCDEFGHIJKLMNOP47UL|" + hostId + "|16|6Iqx55Sf";
        Check(parse.Invoke(null, new object[] { advertisement, 11.0 }) != null &&
            parse.Invoke(null, new object[] { advertisement.Replace("|16|", "|17|"), 11.0 }) == null,
            "public discovery rejects invalid occupancy");
    }

    private static object ConnectTestRoom(Type networkType, object network, bool hosting)
    {
        const string brokerKey = "WHEEKKEYS_ROOM_BROKER";
        string original = Environment.GetEnvironmentVariable(brokerKey);
        try
        {
            Environment.SetEnvironmentVariable(brokerKey, "invalid");
            networkType.GetMethod("EnterRoom").Invoke(network, new object[] { "ABCDEFGHIJKLMNOP47UL", hosting, false, null });
        }
        finally { Environment.SetEnvironmentVariable(brokerKey, original); }
        object transport = networkType.GetField("room", PrivateInstance).GetValue(network);
        Set(transport.GetType(), transport, "connected", true);
        return transport;
    }
    private static void Set(Type t, object o, string name, object value) { t.GetField(name, PrivateInstance).SetValue(o, value); }

    private static void Relay(object sender, object recipient)
    {
        Type transportType = sender.GetType();
        var outgoing = (Queue<string>)transportType.GetField("outgoing", PrivateInstance).GetValue(sender);
        var ready = (SemaphoreSlim)transportType.GetField("outgoingReady", PrivateInstance).GetValue(sender);
        var incoming = (Queue<string>)transportType.GetField("incoming", PrivateInstance).GetValue(recipient);
        while (ready.Wait(0)) incoming.Enqueue(outgoing.Dequeue());
    }
    private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
    private static string Take(MethodInfo method, object peer, double now)
    {
        object[] args = { now, null }; if (!(bool)method.Invoke(peer, args)) return null;
        object action = args[1]; return (char)action.GetType().GetField("Kind").GetValue(action) == 'k'
            ? "k" + action.GetType().GetField("Scan").GetValue(action) : action.GetType().GetField("Kind").GetValue(action).ToString();
    }
    private static int QueueCount(object peer) { return ((ICollection)peer.GetType().GetField("Actions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(peer)).Count; }
    private static int PendingCount(object network, Type type) { return ((ICollection)type.GetField("pendingActions", PrivateInstance).GetValue(network)).Count; }
    private static void Reject(MethodInfo read, object network, string message, double now, object peer, string label)
    {
        int count = QueueCount(peer); read.Invoke(network, new object[] { message, now }); Check(QueueCount(peer) == count, label + " rejected");
    }
}
