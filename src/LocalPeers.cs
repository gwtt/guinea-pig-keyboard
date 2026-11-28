using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WheekKeys
{
    internal sealed class LocalPeers : IDisposable
    {
        internal const int AnonymousKeyScan = 511;
        private static readonly string AnonymousKeyToken = "k" + AnonymousKeyScan.ToString(CultureInfo.InvariantCulture);
        private readonly SortedDictionary<string, Peer> peers = new SortedDictionary<string, Peer>(StringComparer.Ordinal);
        private readonly Queue<PeerAction> pendingActions = new Queue<PeerAction>();
        private readonly Queue<ReceivedInteraction> receivedInteractions = new Queue<ReceivedInteraction>();
        private readonly Dictionary<string, double> nextInteractionByTarget = new Dictionary<string, double>(StringComparer.Ordinal);
        private RoomTransport room;
        private RoomMembership membership;
        private RoomDirectory directory;
        private string session = Guid.NewGuid().ToString("N");
        private double nextSend;
        private double nextActionSend;
        private long actionSequence;
        private long interactionSequence;
        private bool shareActivity = true;
        private bool sharePublicActivity;

        internal struct PeerAction
        {
            public char Kind;
            public int Scan;
            public double Received;
        }

        internal struct ReceivedInteraction
        {
            public string PeerId;
            public string DisplayName;
            public string Kind;
            public double Received;
        }

        internal sealed class Peer
        {
            public PetSkin Skin;
            public long Keys, Clicks;
            public double Seen;
            public string Nickname = "";
            public bool SupportsActivity, SharingActivity;
            internal bool HasAppearance;
            internal PetSkin NetworkSkin;
            internal long ActionSequence;
            internal long InteractionSequence;
            internal readonly Queue<PeerAction> Actions = new Queue<PeerAction>();
            private double nextActionAt;

            public bool TryTakeAction(double now, out PeerAction action)
            {
                while (SharingActivity && now >= nextActionAt && Actions.Count > 0)
                {
                    action = Actions.Dequeue();
                    if (now - action.Received > 0.8) continue;
                    // 给握鼠和点击动画留时间，避免被紧随的按键打断。
                    nextActionAt = now + (action.Kind == 'k' ? 0.055 : 0.18);
                    return true;
                }
                action = new PeerAction();
                return false;
            }

            internal void ClearActions()
            {
                Actions.Clear();
                nextActionAt = 0;
            }
        }

        public bool Enabled { get { return room != null; } }
        public string RoomCode { get; private set; }
        public bool RoomConnected { get { return room != null && room.Connected && membership.Joined; } }
        public string RoomStatus
        {
            get
            {
                if (room == null || !room.Connected) return room == null ? null : room.Status;
                if (membership.Joined) return (membership.IsPublic ? "公开房间" : "私密房间") + " · " +
                    membership.Count + "/" + RoomMembership.Capacity;
                return membership.Count == RoomMembership.Capacity ? "房间已满，等待空位" : "等待房主确认";
            }
        }
        public bool RoomPublic { get { return membership != null && membership.IsPublic; } }
        public bool ShareActivityAvailable { get { return membership == null || membership.VisibilityKnown; } }
        public IReadOnlyDictionary<string, Peer> Members { get { return peers; } }
        public bool ShareActivity
        {
            get { return ShareActivityAvailable && (RoomPublic ? sharePublicActivity : shareActivity); }
            set
            {
                if (!ShareActivityAvailable || ShareActivity == value) return;
                if (RoomPublic) sharePublicActivity = value;
                else shareActivity = value;
                pendingActions.Clear();
                RefreshState();
            }
        }

        public void RefreshState() { nextSend = 0; }

        public void RecordTyping(double now) { RecordAction('k', now); }

        public void RecordClick(bool right, double now) { RecordAction(right ? 'r' : 'l', now); }

        public bool TrySendInteraction(string target, string kind, double now)
        {
            if (!RoomConnected || target == null ||
                (target == "*" ? peers.Count == 0 : !peers.ContainsKey(target)) ||
                !ValidInteraction(kind) || interactionSequence == long.MaxValue) return false;
            double next;
            if (nextInteractionByTarget.TryGetValue(target, out next) && now < next) return false;
            string message = "WI1|" + session + "|" + target + "|" +
                (interactionSequence + 1).ToString(CultureInfo.InvariantCulture) + "|" + kind;
            room.Publish(message);
            interactionSequence++;
            nextInteractionByTarget[target] = now + 1.2;
            return true;
        }

        public bool TryTakeReceivedInteraction(out ReceivedInteraction interaction)
        {
            if (receivedInteractions.Count != 0)
            {
                interaction = receivedInteractions.Dequeue();
                return true;
            }
            interaction = new ReceivedInteraction();
            return false;
        }

        private void RecordAction(char kind, double now)
        {
            if (!ShareActivity || !RoomConnected) return;
            if (pendingActions.Count >= 64) pendingActions.Dequeue();
            pendingActions.Enqueue(new PeerAction { Kind = kind, Received = now });
        }

        public void EnterRoom(string code, bool hosting, bool isPublic, string expectedHost)
        {
            Dispose();
            session = Guid.NewGuid().ToString("N");
            membership = new RoomMembership(session, hosting, isPublic, expectedHost);
            room = new RoomTransport(code);
            if (hosting && isPublic) directory = new RoomDirectory();
            RoomCode = code;
            nextSend = 0;
        }

        public void Dispose()
        {
            if (room != null) room.Dispose();
            if (directory != null) directory.Dispose();
            room = null;
            directory = null;
            membership = null;
            sharePublicActivity = false;
            RoomCode = null;
            peers.Clear();
            pendingActions.Clear();
            receivedInteractions.Clear();
            nextInteractionByTarget.Clear();
            nextActionSend = 0;
        }

        public void Tick(double now, PetSkin skin, long keys, long clicks, string nickname)
        {
            if (room == null) return;
            TickRoom(now, string.IsNullOrEmpty(nickname) ? skin.Name : nickname);
            foreach (string id in peers.Where(p => now - p.Value.Seen >= 20 ||
                !membership.Contains(p.Key)).Select(p => p.Key).ToArray())
            {
                peers.Remove(id);
                nextInteractionByTarget.Remove(id);
            }
            if (!RoomConnected)
            {
                pendingActions.Clear();
                receivedInteractions.Clear();
                nextInteractionByTarget.Clear();
                nextSend = 0;
                return;
            }
            if (now >= nextSend)
            {
                string message = string.Format(CultureInfo.InvariantCulture, "WK1|{0}|{1}|{2}|{3}", session, skin.NetworkId, keys, clicks);
                room.Publish(message);
                room.Publish("WS1|" + session + "|" + skin.Id + "|" + (ShareActivity ? "1" : "0"));
                string normalized;
                if (PetName.TryNormalize(nickname, out normalized) && normalized == nickname)
                {
                    string nameMessage = "WN1|" + session + "|" + PetName.Encode(nickname);
                    room.Publish(nameMessage);
                }
                nextSend = now + 5;
            }
            FlushActions(now);
        }

        private void TickRoom(double now, string ownerName)
        {
            if (!room.Connected)
            {
                membership.Disconnected();
                peers.Clear();
                return;
            }
            membership.Expire(now);
            string message;
            for (int i = 0; i < 32 && room.TryReceive(out message); i++)
            {
                string[] fields = message.Split('|');
                if (fields[0] == "WJ2" || fields[0] == "WM2") membership.Receive(fields, now);
                else if (fields.Length >= 2 && membership.Contains(fields[1])) ReadPeer(message, now);
            }
            string control = membership.NextPacket(now);
            if (control != null) room.Publish(control);
            if (directory != null) directory.Advertise(now, RoomCode, session, membership.Count, ownerName);
        }

        private void FlushActions(double now)
        {
            if (!ShareActivity || now < nextActionSend || pendingActions.Count == 0 || actionSequence == long.MaxValue) return;
            List<string> actions = new List<string>();
            while (pendingActions.Count > 0 && actions.Count < 12)
            {
                PeerAction action = pendingActions.Dequeue();
                if (now - action.Received > 0.8) continue;
                actions.Add(action.Kind == 'k' ? AnonymousKeyToken : action.Kind.ToString());
            }
            if (actions.Count == 0) return;
            actionSequence++;
            room.Publish("WA1|" + session + "|" + actionSequence.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", actions));
            nextActionSend = now + 0.03;
        }

        private void ReadPeer(string message, double now)
        {
            if (message.Length > 128) return;
            string[] fields = message.Split('|');
            if (fields.Length == 5 && fields[0] == "WI1")
            {
                ReadInteraction(fields, now);
                return;
            }
            if (fields.Length == 4 && (fields[0] == "WS1" || fields[0] == "WA1"))
            {
                Peer existing;
                if (fields[1] == session || !ValidId(fields[1]) || !peers.TryGetValue(fields[1], out existing)) return;
                if (fields[0] == "WS1")
                {
                    if (fields[3] != "0" && fields[3] != "1") return;
                    PetSkin exact = PetSkin.Find(fields[2]);
                    if (exact != null) { existing.Skin = exact; existing.HasAppearance = true; }
                    else if (fields[2].StartsWith("wheek-", StringComparison.Ordinal))
                    { existing.Skin = existing.NetworkSkin; existing.HasAppearance = false; }
                    existing.SupportsActivity = true;
                    existing.SharingActivity = fields[3] == "1";
                    if (!existing.SharingActivity) existing.ClearActions();
                    existing.Seen = now;
                }
                else ReadActions(fields, existing, now);
                return;
            }
            if (fields.Length == 3 && fields[0] == "WN1")
            {
                string nameId = fields[1];
                string name;
                Peer existing;
                if (message.Length <= 125 && nameId != session && ValidId(nameId) &&
                    peers.TryGetValue(nameId, out existing) && PetName.TryDecode(fields[2], out name))
                    existing.Nickname = name;
                return;
            }
            if (fields.Length != 5 || fields[0] != "WK1") return;
            string id = fields[1];
            if (id == session || !ValidId(id)) return;
            PetSkin skin = PetSkin.Find(fields[2]);
            long keys, clicks;
            if (skin == null || skin.IsCustom || !TryCount(fields[3], out keys) || !TryCount(fields[4], out clicks)) return;
            Peer peer;
            if (!peers.TryGetValue(id, out peer))
            {
                if (peers.Count >= 64) return;
                peers.Add(id, peer = new Peer());
                RefreshState();
            }
            peer.Seen = now;
            peer.NetworkSkin = skin;
            if (!peer.HasAppearance) peer.Skin = skin;
            peer.Keys = keys;
            peer.Clicks = clicks;
        }

        private void ReadInteraction(string[] fields, double now)
        {
            string sender = fields[1];
            Peer peer;
            long sequence;
            if (sender == session || (fields[2] != session && fields[2] != "*") ||
                !peers.TryGetValue(sender, out peer) || !ValidInteraction(fields[4]) ||
                !TryCount(fields[3], out sequence) || sequence <= peer.InteractionSequence) return;
            peer.InteractionSequence = sequence;
            if (receivedInteractions.Count >= 32) receivedInteractions.Dequeue();
            receivedInteractions.Enqueue(new ReceivedInteraction {
                PeerId = sender, DisplayName = string.IsNullOrEmpty(peer.Nickname) ? peer.Skin.Name : peer.Nickname,
                Kind = fields[4], Received = now
            });
        }

        private static bool ValidInteraction(string kind) { return kind == "poke" || kind == "feed"; }

        private static void ReadActions(string[] fields, Peer peer, double now)
        {
            long sequence;
            if (!peer.SharingActivity || !TryCount(fields[2], out sequence) || sequence <= peer.ActionSequence) return;
            string[] tokens = fields[3].Split(',');
            if (tokens.Length == 0 || tokens.Length > 12) return;
            List<PeerAction> actions = new List<PeerAction>();
            foreach (string token in tokens)
            {
                long scan;
                if (token == "l" || token == "r") actions.Add(new PeerAction { Kind = token[0], Received = now });
                else if (token.Length >= 2 && token[0] == 'k' && TryCount(token.Substring(1), out scan) && scan > 0 && scan <= AnonymousKeyScan)
                    actions.Add(new PeerAction { Kind = 'k', Scan = (int)scan, Received = now });
                else return;
            }
            peer.ActionSequence = sequence;
            peer.Seen = now;
            foreach (PeerAction action in actions)
            {
                if (peer.Actions.Count >= 64) peer.Actions.Dequeue();
                peer.Actions.Enqueue(action);
            }
        }

        private static bool ValidId(string id)
        {
            return RoomMembership.ValidId(id);
        }

        internal static bool TryCount(string value, out long result)
        {
            result = 0;
            return value.Length > 0 && value.All(c => c >= '0' && c <= '9')
                && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }
    }
}
