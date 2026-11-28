import AppKit

private final class FriendPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class FriendDesktop {
    private final class Friend {
        let window: NSPanel
        let pet: PetView
        let name: NSTextField
        var skinID: String?
        private var automaticOrigin: NSPoint?

        init(frame: NSRect) {
            window = FriendPanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
            window.isReleasedWhenClosed = false
            window.isOpaque = false
            window.backgroundColor = .clear
            window.hasShadow = false
            window.hidesOnDeactivate = false
            window.level = .floating
            window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
            window.isMovable = true
            window.isMovableByWindowBackground = true
            pet = PetView(frame: NSRect(origin: .zero, size: frame.size))
            pet.autoresizingMask = [.width, .height]
            name = NSTextField(labelWithString: "")
            name.alignment = .center
            name.font = .systemFont(ofSize: 12, weight: .semibold)
            name.textColor = .white
            name.lineBreakMode = .byTruncatingTail
            name.wantsLayer = true
            name.layer?.backgroundColor = NSColor(calibratedWhite: 0.15, alpha: 0.82).cgColor
            name.layer?.cornerRadius = 6
            pet.addSubview(name)
            window.contentView = pet
            automaticOrigin = window.frame.origin
        }

        func layout(at frame: NSRect) {
            let current = window.frame
            // 移离上次自动排布的位置后，保留窗口当前位置。
            let followsAutomaticLayout = automaticOrigin == current.origin
            let target = NSRect(origin: followsAutomaticLayout ? frame.origin : current.origin, size: frame.size)
            if current != target { window.setFrame(target, display: true) }
            automaticOrigin = followsAutomaticLayout ? window.frame.origin : nil
        }
    }
    private var friends: [String: Friend] = [:]

    func update(owner: NSWindow, members: [String: LocalPeers.Peer], clickThrough: Bool, showLabels: Bool,
                takeAction: (String) -> LocalPeers.Action?) {
        let ids = members.keys.sorted()
        for id in Array(friends.keys) where members[id] == nil {
            friends.removeValue(forKey: id)?.window.close()
        }
        let visible = (owner.screen ?? NSScreen.main ?? NSScreen.screens.first)?.visibleFrame
        let frames = Self.frames(around: owner.frame, within: visible, count: ids.count)
        for (id, frame) in zip(ids, frames) {
            guard let peer = members[id] else { continue }
            let friend = friends[id] ?? Friend(frame: frame)
            friends[id] = friend
            if friend.skinID != peer.skin {
                friend.pet.selectSkin(peer.skin)
                friend.skinID = peer.skin
            }
            if friend.pet.showKeyLabels != showLabels { friend.pet.showKeyLabels = showLabels }
            var label = peer.nickname.isEmpty ? PetSkin.named(peer.skin).name : peer.nickname
            if !peer.supportsActivity { label += " · 仅外观" }
            else if !peer.sharingActivity { label += " · 已暂停动作" }
            if friend.name.stringValue != label { friend.name.stringValue = label }
            friend.window.ignoresMouseEvents = clickThrough
            friend.window.alphaValue = owner.alphaValue
            if let ownerPet = owner.contentView as? PetView, ownerPet.sleepDelay != friend.pet.sleepDelay {
                friend.pet.sleepDelay = ownerPet.sleepDelay
            }
            friend.layout(at: frame)
            let labelWidth = min(frame.width - 12, friend.name.intrinsicContentSize.width + 14)
            friend.name.frame = NSRect(x: (frame.width - labelWidth) / 2, y: 6, width: labelWidth, height: 18)
            let action = takeAction(id)
            guard owner.isVisible else {
                if friend.window.isVisible { friend.window.orderOut(nil) }
                continue
            }
            if !friend.window.isVisible { friend.window.orderFrontRegardless() }
            if let action = action {
                switch action {
                case .key(let code): friend.pet.pressKey(code)
                case .click(let right): friend.pet.clickMouse(right: right)
                }
            }
        }
    }

    static func frames(around owner: NSRect, within visible: NSRect?, count: Int) -> [NSRect] {
        guard count > 0 else { return [] }
        let stepX = owner.width + 8, stepY = owner.height + 8
        guard let visible = visible else {
            return (1...count).map { owner.offsetBy(dx: -CGFloat($0) * stepX, dy: 0) }
        }
        let maxX = max(visible.minX, visible.maxX - owner.width)
        let maxY = max(visible.minY, visible.maxY - owner.height)
        let x = min(max(visible.minX, owner.minX), maxX)
        let y = min(max(visible.minY, owner.minY), maxY)
        let columns = [x] + Array(stride(from: x - stepX, through: visible.minX, by: -stepX))
                          + Array(stride(from: x + stepX, through: maxX, by: stepX))
        let rows = [y] + Array(stride(from: y + stepY, through: maxY, by: stepY))
                       + Array(stride(from: y - stepY, through: visible.minY, by: -stepY))
        var frames: [NSRect] = []
        for row in rows {
            for column in columns {
                let frame = NSRect(x: column, y: row, width: owner.width, height: owner.height)
                guard !frame.intersects(owner) else { continue }
                frames.append(frame)
                if frames.count == count { return frames }
            }
        }
        if frames.isEmpty {
            let left = x - visible.minX, right = maxX - x
            frames.append(NSRect(x: left >= right ? visible.minX : maxX,
                                 y: y - visible.minY >= maxY - y ? visible.minY : maxY,
                                 width: owner.width, height: owner.height))
        }
        let slots = frames.count
        while frames.count < count {
            let base = frames[frames.count % slots]
            let offset = CGFloat(frames.count / slots % 8) * 12
            let dx = base.minX + offset <= maxX ? offset : -offset
            let dy = base.minY + offset <= maxY ? offset : -offset
            frames.append(NSRect(x: max(visible.minX, base.minX + dx), y: max(visible.minY, base.minY + dy),
                                 width: base.width, height: base.height))
        }
        return frames
    }

    var count: Int { friends.count }
    func window(for id: String) -> NSWindow? { friends[id]?.window }

    func close() {
        for friend in friends.values { friend.window.close() }
        friends.removeAll()
    }
}
