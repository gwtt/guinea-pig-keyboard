import Foundation

final class LocalPeers {
    enum Action: Equatable {
        case key(UInt16)
        case anonymousKey
        case click(Bool)
    }

    private static let anonymousKeyScan: UInt64 = 511

    struct Peer {
        var skin: String
        var keys: UInt64
        var clicks: UInt64
        var seen: TimeInterval
        var nickname: String = ""
        var supportsActivity = false
        var sharingActivity = false
        fileprivate var hasAppearance = false
        fileprivate var actionSequence: UInt64 = 0
        fileprivate var actions: [(action: Action, time: TimeInterval)] = []
        private var nextActionAt: TimeInterval = 0

        init(skin: String, keys: UInt64, clicks: UInt64, seen: TimeInterval) {
            self.skin = skin
            self.keys = keys
            self.clicks = clicks
            self.seen = seen
        }

        mutating func takeAction(now: TimeInterval) -> Action? {
            guard sharingActivity, now >= nextActionAt else { return nil }
            actions.removeAll { now - $0.time > LocalPeers.actionLifetime }
            guard !actions.isEmpty else { return nil }
            let action = actions.removeFirst().action
            // 给握鼠和点击动画留时间，避免被紧随的按键打断。
            if case .click = action { nextActionAt = now + 0.18 }
            else { nextActionAt = now + 0.055 }
            return action
        }

        fileprivate mutating func clearActions() {
            actions.removeAll(keepingCapacity: true)
            nextActionAt = 0
        }
    }

    private static let actionLifetime: TimeInterval = 0.8
    private static let actionBatchLimit = 12
    private static let actionQueueLimit = 64
    private(set) var peers: [String: Peer] = [:]
    private(set) var status = ""
    private(set) var roomCode: String?
    var roomConnected: Bool { room?.connected == true && membership?.joined == true }
    var roomPublic: Bool { membership?.isPublic ?? false }
    var shareActivityAvailable: Bool { membership?.visibilityKnown ?? true }
    private var room: RoomTransport?
    private var membership: RoomMembership?
    private var directory: RoomDirectory?
    private var timer: Timer?
    private var session = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
    var payload: (() -> (String, UInt64, UInt64))?
    var nickname: (() -> String)?
    var onUpdate: (() -> Void)?
    private var sharePrivateActivity = true
    private var sharePublicActivity = false
    var shareActivity: Bool {
        get { shareActivityAvailable && (roomPublic ? sharePublicActivity : sharePrivateActivity) }
        set {
            guard shareActivityAvailable, newValue != shareActivity else { return }
            if roomPublic { sharePublicActivity = newValue }
            else { sharePrivateActivity = newValue }
            pendingActions.removeAll(keepingCapacity: true)
            refreshState()
        }
    }
    private var lastStateSend: TimeInterval = 0
    private var actionSequence: UInt64 = 0
    private var pendingActions: [(token: String, time: TimeInterval)] = []

    func recordTyping() { recordAction("k\(Self.anonymousKeyScan)") }

    func recordClick(right: Bool) { recordAction(right ? "r" : "l") }

    private func recordAction(_ token: String) {
        guard shareActivity, roomConnected else { return }
        if pendingActions.count == Self.actionQueueLimit { pendingActions.removeFirst() }
        pendingActions.append((token, ProcessInfo.processInfo.systemUptime))
    }

    func refreshState() { lastStateSend = 0 }

    func takeAction(for id: String, visible: Bool) -> Action? {
        guard visible else { peers[id]?.clearActions(); return nil }
        return peers[id]?.takeAction(now: ProcessInfo.processInfo.systemUptime)
    }


    func startRoom(_ code: String, hosting: Bool = false, isPublic: Bool = false, expectedHost: String? = nil) -> Bool {
        guard let normalized = RoomTransport.normalizeCode(code) else { return false }
        stop()
        session = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        roomCode = normalized
        membership = RoomMembership(ownID: session, hosting: hosting, isPublic: isPublic, expectedHost: expectedHost)
        if hosting && isPublic { directory = RoomDirectory() }
        let transport = RoomTransport(code: normalized)
        room = transport
        transport.start()
        startTimer()
        return true
    }

    private func startTimer() {
        let timer = Timer(timeInterval: 0.03, repeats: true) { [weak self] _ in self?.tick() }
        self.timer = timer
        RunLoop.main.add(timer, forMode: .common)
        tick()
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        room?.stop()
        room = nil
        membership = nil
        directory = nil
        sharePublicActivity = false
        roomCode = nil
        status = ""
        clearPeers()
    }

    private func clearPeers() {
        pendingActions.removeAll(keepingCapacity: true)
        refreshState()
        guard !peers.isEmpty else { return }
        peers.removeAll()
        onUpdate?()
    }

    private func tick() {
        guard let room = room, let membership = membership, let code = roomCode else { return }
        guard room.connected else {
            status = room.status
            membership.disconnected()
            clearPeers()
            return
        }
        let now = ProcessInfo.processInfo.systemUptime
        var changed = false
        membership.expire(now: now)
        for message in room.drain() {
            let fields = message.split(separator: "|", omittingEmptySubsequences: false)
            if fields.first == "WJ2" || fields.first == "WM2" {
                membership.receive(fields, now: now)
            } else if fields.count >= 2, membership.contains(String(fields[1])),
                      accept(Array(message.utf8), now: now) { changed = true }
        }
        if let control = membership.nextPacket(now: now) { room.publish(control) }
        if let directory = directory {
            let name = nickname?() ?? ""
            let ownerName = name.isEmpty ? PetSkin.named(payload?().0).name : name
            directory.advertise(now: now, code: code, host: session, count: membership.count, nickname: ownerName)
        }
        guard membership.joined else {
            status = membership.count == RoomMembership.capacity ? "房间已满，等待空位" : "等待房主确认"
            clearPeers()
            return
        }
        status = (membership.isPublic ? "公开房间" : "私密房间") + " · \(membership.count)/\(RoomMembership.capacity)"

        if lastStateSend == 0 || now - lastStateSend >= 5 {
            lastStateSend = now
            for message in statePackets() { room.publish(message) }
        }
        if let packet = actionPacket(now: now) { room.publish(packet) }

        let remaining = Self.unexpired(peers, now: now).filter { membership.contains($0.key) }
        if remaining.count != peers.count {
            peers = remaining
            changed = true
        }
        if changed || peers.values.contains(where: { !$0.actions.isEmpty }) { onUpdate?() }
    }

    private func accept(_ bytes: [UInt8], now: TimeInterval) -> Bool {
        let count = peers.count
        let accepted = Self.apply(bytes: bytes, ownID: session, now: now, peers: &peers)
        if peers.count > count { refreshState() }
        return accepted
    }

    static func normalizedNickname(_ input: String) -> String? {
        let value = input.trimmingCharacters(in: .whitespacesAndNewlines)
        for scalar in value.unicodeScalars {
            let code = scalar.value
            if scalar.properties.generalCategory == .control || code == 0x2028 || code == 0x2029 ||
                code == 0x061C || code == 0x200E || code == 0x200F ||
                (0x202A...0x202E).contains(code) || (0x2066...0x2069).contains(code) { return nil }
        }
        guard value.unicodeScalars.count <= 16, value.utf8.count <= 64 else { return nil }
        return value
    }

    static func decodedNickname(_ input: Substring) -> String? {
        guard input.utf8.count <= 88, let data = Data(base64Encoded: String(input)),
              data.base64EncodedString() == input, let text = String(data: data, encoding: .utf8),
              normalizedNickname(text) == text else { return nil }
        return text
    }

    @discardableResult
    static func apply(bytes: [UInt8], ownID: String, now: TimeInterval, peers: inout [String: Peer]) -> Bool {
        guard bytes.count <= 128, let message = String(bytes: bytes, encoding: .ascii) else { return false }
        let fields = message.split(separator: "|", omittingEmptySubsequences: false)
        guard fields.count >= 2, fields[1] != ownID, validID(fields[1]) else { return false }
        let id = String(fields[1])
        if fields[0] == "WK1" {
            guard let state = parseState(fields, now: now), peers[id] != nil || peers.count < 64 else { return false }
            var peer = peers[id] ?? state
            if !peer.hasAppearance { peer.skin = state.skin }
            peer.keys = state.keys
            peer.clicks = state.clicks
            peer.seen = now
            peers[id] = peer
            return true
        }
        guard var peer = peers[id] else { return false }
        switch fields[0] {
        case "WN1":
            guard fields.count == 3, let value = decodedNickname(fields[2]) else { return false }
            peer.nickname = value
        case "WS1":
            guard fields.count == 4, fields[3] == "0" || fields[3] == "1" else { return false }
            if let skin = PetSkin.find(String(fields[2])), !skin.isCustom {
                peer.skin = skin.id
                peer.hasAppearance = true
            }
            peer.supportsActivity = true
            peer.sharingActivity = fields[3] == "1"
            if !peer.sharingActivity { peer.clearActions() }
            peer.seen = now
        case "WA1":
            guard fields.count == 4, peer.sharingActivity, let sequence = count(fields[2]),
                  sequence > peer.actionSequence, let actions = parseActions(fields[3]) else { return false }
            peer.actionSequence = sequence
            peer.seen = now
            peer.actions.append(contentsOf: actions.map { ($0, now) })
            if peer.actions.count > actionQueueLimit {
                peer.actions.removeFirst(peer.actions.count - actionQueueLimit)
            }
        default:
            return false
        }
        peers[id] = peer
        return true
    }

    private func nicknamePacket() -> String {
        let value = Self.normalizedNickname(nickname?() ?? "") ?? ""
        return "WN1|\(session)|\(Data(value.utf8).base64EncodedString())"
    }

    private func statePackets() -> [String] {
        guard let (skinID, keys, clicks) = payload?() else { return [] }
        let skin = PetSkin.named(skinID)
        return ["WK1|\(session)|\(skin.networkID)|\(keys)|\(clicks)",
                "WS1|\(session)|\(skin.appearanceNetworkID)|\(shareActivity ? "1" : "0")", nicknamePacket()]
    }

    private func actionPacket(now: TimeInterval) -> String? {
        guard !pendingActions.isEmpty, actionSequence < UInt64(Int64.max) else { return nil }
        pendingActions.removeAll { now - $0.time > Self.actionLifetime }
        let batch = pendingActions.prefix(Self.actionBatchLimit)
        guard !batch.isEmpty else { return nil }
        let tokens = batch.map { $0.token }.joined(separator: ",")
        pendingActions.removeFirst(batch.count)
        actionSequence += 1
        return "WA1|\(session)|\(actionSequence)|\(tokens)"
    }

    static func parse(bytes: [UInt8], ownID: String, now: TimeInterval) -> (String, Peer)? {
        guard bytes.count <= 128, let message = String(bytes: bytes, encoding: .ascii) else { return nil }
        let fields = message.split(separator: "|", omittingEmptySubsequences: false)
        guard fields.count == 5, fields[1] != ownID, validID(fields[1]),
              let peer = parseState(fields, now: now) else { return nil }
        return (String(fields[1]), peer)
    }

    private static func parseState(_ fields: [Substring], now: TimeInterval) -> Peer? {
        guard fields.count == 5, fields[0] == "WK1", let skin = PetSkin.find(String(fields[2])), !skin.isCustom,
              let keys = count(fields[3]), let clicks = count(fields[4]) else { return nil }
        return Peer(skin: skin.id, keys: keys, clicks: clicks, seen: now)
    }

    private static func validID(_ value: Substring) -> Bool {
        RoomMembership.validID(value)
    }

    static func count(_ value: Substring) -> UInt64? {
        guard !value.isEmpty, value.utf8.allSatisfy({ (48...57).contains($0) }),
              let number = UInt64(value), number <= UInt64(Int64.max) else { return nil }
        return number
    }

    private static func parseActions(_ value: Substring) -> [Action]? {
        let tokens = value.split(separator: ",", omittingEmptySubsequences: false)
        guard !tokens.isEmpty, tokens.count <= actionBatchLimit else { return nil }
        var actions: [Action] = []
        for token in tokens {
            if token == "l" || token == "r" {
                actions.append(.click(token == "r"))
            } else {
                guard token.first == "k", let scan = count(token.dropFirst()), (1...anonymousKeyScan).contains(scan) else { return nil }
                actions.append(scan == anonymousKeyScan ? .anonymousKey : .key(NetworkKeyCodes.keyCode(for: UInt16(scan)) ?? UInt16.max))
            }
        }
        return actions
    }

    static func unexpired(_ peers: [String: Peer], now: TimeInterval) -> [String: Peer] {
        peers.filter { now - $0.value.seen < 20 }
    }
}
