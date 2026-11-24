import AppKit

final class PetSettingsViewController: NSViewController {
    var onSelectSkin: ((String) -> Void)?
    var onSaveNickname: ((String) -> Bool)?
    var onScaleChange: ((Double) -> Void)?
    var onShowLabelsChange: ((Bool) -> Void)?
    var onClickThroughChange: ((Bool) -> Void)?
    var onHideForSogouChange: ((Bool) -> Void)?
    var onNetworkingChange: ((Bool) -> Void)?
    var onCreateRoom: (() -> Void)?
    var onJoinRoom: ((String) -> Void)?
    var onExitRoom: (() -> Void)?
    var onReturnToCorner: (() -> Void)?
    var onQuit: (() -> Void)?
    var onClose: (() -> Void)?

    private let skinButtons = Dictionary(uniqueKeysWithValues: PetSkin.all.map { ($0.id, NSButton()) })
    private let scaleValue = NSTextField(labelWithString: "100%")
    private let labelsSwitch = NSButton(checkboxWithTitle: "显示按键气泡", target: nil, action: nil)
    private let clickThroughSwitch = NSButton(checkboxWithTitle: "鼠标穿透", target: nil, action: nil)

    private let sogouSwitch = NSButton(checkboxWithTitle: "使用搜狗输入法时自动隐藏", target: nil, action: nil)
    private let networkSwitch = NSButton(checkboxWithTitle: "开启局域网联机", target: nil, action: nil)
    private let tabs = NSSegmentedControl(labels: ["桌宠", "联机"], trackingMode: .selectOne, target: nil, action: nil)
    private let petScroll = NSScrollView()
    private let petPage = FlippedView()
    private let lanPage = NSView()
    private let createRoom = NSButton(title: "创建房间", target: nil, action: nil)
    private let joinCode = NSTextField()
    private let joinRoom = NSButton(title: "加入", target: nil, action: nil)
    private let roomCodeLabel = NSTextField(labelWithString: "")
    private let copyRoom = NSButton(title: "复制", target: nil, action: nil)
    private let exitRoom = NSButton(title: "退出房间", target: nil, action: nil)
    private let roomHint = NSTextField(labelWithString: "房间通过加密中转联机，分享房间码邀请伙伴")
    private let networkState = NSTextField(labelWithString: "")
    private let networkMessage = NSTextField(labelWithString: "")
    private let emptyMessage = NSTextField(wrappingLabelWithString: "")
    private let localRow = PeerRow(frame: .zero)
    private let peerScroll = NSScrollView()
    private let peerDocument = FlippedView()
    private var peerRows: [String: PeerRow] = [:]
    private var portraitCache: [String: NSImage] = [:]
    private var compactConstraints: [(NSLayoutConstraint, CGFloat)] = []
    private var networkError = ""

    private let paper = NSColor(calibratedRed: 250 / 255, green: 246 / 255, blue: 235 / 255, alpha: 1)
    private let darkBrown = NSColor(calibratedRed: 77 / 255, green: 54 / 255, blue: 39 / 255, alpha: 1)
    private let mutedBrown = NSColor(calibratedRed: 143 / 255, green: 119 / 255, blue: 94 / 255, alpha: 1)
    private let sage = NSColor(calibratedRed: 224 / 255, green: 235 / 255, blue: 216 / 255, alpha: 1)

    override func loadView() {
        let root = SettingsCardView(frame: NSRect(x: 0, y: 0, width: 316, height: 508))
        root.appearance = NSAppearance(named: .aqua)
        root.wantsLayer = true
        root.layer?.backgroundColor = paper.cgColor
        root.layer?.cornerRadius = 18
        root.layer?.borderWidth = 1
        root.layer?.borderColor = NSColor(calibratedRed: 225 / 255, green: 214 / 255, blue: 194 / 255, alpha: 1).cgColor
        root.closeAction = { [weak self] in self?.onClose?() }
        view = root

        let avatar = NSImageView()
        if let url = Bundle.main.url(forResource: "avatar", withExtension: "png") {
            avatar.image = NSImage(contentsOf: url)
        }
        avatar.imageScaling = .scaleProportionallyUpOrDown
        avatar.wantsLayer = true
        avatar.layer?.cornerRadius = 10
        avatar.layer?.masksToBounds = true
        add(avatar, x: 18, top: 15, width: 42, height: 42, to: root)

        let title = NSTextField(labelWithString: "豚鼠键盘")
        title.font = .systemFont(ofSize: 17, weight: .bold)
        title.textColor = darkBrown
        add(title, x: 70, top: 22, width: 170, height: 24, to: root)

        let closeButton = NSButton(title: "×", target: self, action: #selector(closePanel))
        closeButton.isBordered = false
        closeButton.font = .systemFont(ofSize: 19, weight: .regular)
        closeButton.contentTintColor = mutedBrown
        add(closeButton, trailing: 12, top: 12, width: 30, height: 30, to: root)

        tabs.target = self
        tabs.action = #selector(changeTab)
        tabs.selectedSegment = 0
        add(tabs, x: 18, top: 68, width: 280, height: 27, to: root)
        petScroll.hasVerticalScroller = true
        petScroll.drawsBackground = false
        petScroll.borderType = .noBorder
        petPage.frame = NSRect(x: 0, y: 0, width: 316, height: 420)
        petScroll.documentView = petPage
        add(petScroll, x: 0, top: 100, width: 316, height: 352, to: root)
        add(lanPage, x: 0, top: 100, width: 316, height: 352, to: root)
        lanPage.isHidden = true

        let coatLabel = sectionLabel("毛色")
        add(coatLabel, x: 18, top: 14, width: 200, height: 18, to: petPage)

        let controlOffset = CGFloat(((PetSkin.all.count + 2) / 3 - 2) * 34)
        for (index, skin) in PetSkin.all.enumerated() {
            let button = skinButtons[skin.id]!
            button.target = self
            button.action = #selector(selectSkin(_:))
            button.identifier = NSUserInterfaceItemIdentifier(skin.id)
            button.isBordered = false
            button.wantsLayer = true
            button.layer?.cornerRadius = 8
            button.imagePosition = .imageLeading
            button.imageScaling = .scaleProportionallyUpOrDown
            button.font = .systemFont(ofSize: 12, weight: .medium)
            button.alignment = .center
            button.image = skin.portrait(size: NSSize(width: 16, height: 17))
            button.setButtonType(.momentaryPushIn)
            add(button, x: 18 + CGFloat(index % 3) * 95, top: 37 + CGFloat(index / 3) * 34,
                width: 90, height: 30, to: petPage)
        }

        let sizeLabel = sectionLabel("大小")
        add(sizeLabel, x: 18, top: 120 + controlOffset, width: 100, height: 18, to: petPage)
        scaleValue.font = .systemFont(ofSize: 12, weight: .semibold)
        scaleValue.textColor = mutedBrown
        scaleValue.alignment = .center
        add(scaleValue, trailing: 54, top: 120 + controlOffset, width: 45, height: 18, to: petPage)

        let minus = controlButton("−", action: #selector(decreaseScale))
        add(minus, trailing: 103, top: 114 + controlOffset, width: 30, height: 30, to: petPage)
        let plus = controlButton("+", action: #selector(increaseScale))
        add(plus, trailing: 18, top: 114 + controlOffset, width: 30, height: 30, to: petPage)

        labelsSwitch.target = self
        labelsSwitch.action = #selector(toggleLabels(_:))
        labelsSwitch.font = .systemFont(ofSize: 12)
        labelsSwitch.contentTintColor = darkBrown
        add(labelsSwitch, x: 18, top: 157 + controlOffset, width: 270, height: 22, to: petPage)
        clickThroughSwitch.target = self
        clickThroughSwitch.action = #selector(toggleClickThrough(_:))
        clickThroughSwitch.font = .systemFont(ofSize: 12)
        clickThroughSwitch.contentTintColor = darkBrown
        add(clickThroughSwitch, x: 18, top: 188 + controlOffset, width: 270, height: 22, to: petPage)

        sogouSwitch.target = self
        sogouSwitch.action = #selector(toggleSogou(_:))
        sogouSwitch.font = .systemFont(ofSize: 12)
        add(sogouSwitch, x: 18, top: 219 + controlOffset, width: 280, height: 22, to: petPage)
        createRoom.target = self
        createRoom.action = #selector(createRoomAction)
        add(createRoom, x: 18, top: 10, width: 88, height: 26, to: lanPage)
        joinCode.placeholderString = "输入房间码"
        add(joinCode, x: 112, top: 12, width: 118, height: 24, to: lanPage)
        joinRoom.target = self
        joinRoom.action = #selector(joinRoomAction)
        add(joinRoom, x: 233, top: 10, width: 65, height: 26, to: lanPage)
        roomCodeLabel.font = .monospacedSystemFont(ofSize: 11, weight: .medium)
        add(roomCodeLabel, x: 18, top: 12, width: 163, height: 23, to: lanPage)
        copyRoom.target = self
        copyRoom.action = #selector(copyRoomAction)
        add(copyRoom, x: 180, top: 10, width: 50, height: 26, to: lanPage)
        exitRoom.target = self
        exitRoom.action = #selector(exitRoomAction)
        add(exitRoom, x: 232, top: 10, width: 66, height: 26, to: lanPage)
        roomHint.font = .systemFont(ofSize: 10)
        roomHint.textColor = mutedBrown
        add(roomHint, x: 18, top: 43, width: 280, height: 18, to: lanPage)

        networkSwitch.target = self
        networkSwitch.action = #selector(toggleNetworking(_:))
        networkSwitch.font = .systemFont(ofSize: 12)
        add(networkSwitch, trailing: 18, top: 65, width: 127, height: 22, to: lanPage)
        networkState.font = .systemFont(ofSize: 11, weight: .semibold)
        networkState.alignment = .left
        add(networkState, x: 18, top: 65, width: 150, height: 22, to: lanPage)
        networkMessage.font = .systemFont(ofSize: 11)
        networkMessage.textColor = NSColor(calibratedRed: 0.65, green: 0.30, blue: 0.22, alpha: 1)
        networkMessage.lineBreakMode = .byTruncatingTail
        add(networkMessage, x: 18, top: 89, width: 280, height: 18, to: lanPage)
        let mine = sectionLabel("本机")
        add(mine, x: 18, top: 111, width: 200, height: 18, to: lanPage)
        add(localRow, x: 18, top: 131, width: 280, height: 56, to: lanPage)
        localRow.layer?.backgroundColor = sage.cgColor
        localRow.canRename = true
        localRow.onSaveNickname = { [weak self] value in self?.onSaveNickname?(value) ?? false }
        let online = sectionLabel("联机伙伴")
        add(online, x: 18, top: 198, width: 200, height: 18, to: lanPage)
        peerScroll.hasVerticalScroller = true
        peerScroll.drawsBackground = false
        peerScroll.borderType = .noBorder
        peerScroll.documentView = peerDocument
        add(peerScroll, x: 18, top: 219, width: 280, height: 122, to: lanPage)
        emptyMessage.font = .systemFont(ofSize: 12)
        emptyMessage.textColor = mutedBrown
        add(emptyMessage, x: 26, top: 224, width: 260, height: 48, to: lanPage)
        let compactViews: [NSView] = [mine, localRow, online, peerScroll, emptyMessage]
        compactConstraints = lanPage.constraints.compactMap { constraint in
            guard constraint.firstAttribute == .top, let child = constraint.firstItem as? NSView,
                  compactViews.contains(where: { $0 === child }) else { return nil }
            return (constraint, constraint.constant)
        }

        let divider = NSBox()
        divider.boxType = .separator
        add(divider, x: 18, top: 452, width: 280, height: 1, to: root)

        let corner = footerButton("回到右下角", action: #selector(returnToCorner))
        add(corner, x: 18, top: 465, width: 172, height: 32, to: root)
        let quit = footerButton("退出", action: #selector(quitApp))
        quit.contentTintColor = NSColor(calibratedRed: 155 / 255, green: 79 / 255, blue: 63 / 255, alpha: 1)
        add(quit, trailing: 18, top: 465, width: 96, height: 32, to: root)
    }

    func refresh(skinID: String, scale: Double, showLabels: Bool, clickThrough: Bool,
                 hideForSogou: Bool, networking: Bool, keys: UInt64, clicks: UInt64,
                 peers: [(String, LocalPeers.Peer)], networkStatus: String,
                 roomCode: String?, roomConnected: Bool, nickname: String) {
        sogouSwitch.state = hideForSogou ? .on : .off
        let inRoom = roomCode != nil
        let inLAN = networking && !inRoom
        networkSwitch.state = inLAN ? .on : .off
        networkSwitch.isHidden = inRoom
        createRoom.isHidden = inRoom
        joinCode.isHidden = inRoom
        joinRoom.isHidden = inRoom
        createRoom.isEnabled = !inLAN
        joinCode.isEnabled = !inLAN
        joinRoom.isEnabled = !inLAN
        roomCodeLabel.isHidden = !inRoom
        copyRoom.isHidden = !inRoom
        exitRoom.isHidden = !inRoom
        if let code = roomCode {
            let chars = Array(code)
            if chars.count == 20 {
                roomCodeLabel.stringValue = stride(from: 0, to: 20, by: 5).map { String(chars[$0..<($0 + 5)]) }.joined(separator: " ")
            }
        }
        let error = inRoom ? "" : (networking ? networkStatus : networkError)
        networkMessage.stringValue = error
        networkMessage.toolTip = error
        networkMessage.isHidden = error.isEmpty
        for (constraint, original) in compactConstraints {
            let top = original - (error.isEmpty ? 16 : 0)
            if constraint.constant != top { constraint.constant = top }
        }
        if inRoom {
            networkState.stringValue = roomConnected ? "房间已连接 · 伙伴 \(peers.count)" : "房间：\(networkStatus.isEmpty ? "连接中" : networkStatus)"
            networkState.textColor = roomConnected ? NSColor(calibratedRed: 0.34, green: 0.48, blue: 0.29, alpha: 1) : mutedBrown
        } else if !error.isEmpty {
            networkState.stringValue = "联机异常"
            networkState.textColor = NSColor(calibratedRed: 0.65, green: 0.30, blue: 0.22, alpha: 1)
        } else if !networking {
            networkState.stringValue = "未连接"
            networkState.textColor = mutedBrown
        } else if peers.isEmpty {
            networkState.stringValue = "局域网寻找中"
            networkState.textColor = mutedBrown
        } else {
            networkState.stringValue = "局域网在线 \(peers.count)"
            networkState.textColor = NSColor(calibratedRed: 0.34, green: 0.48, blue: 0.29, alpha: 1)
        }
        let skinName = PetSkin.named(skinID).name
        let localName = nickname.isEmpty ? skinName + " · 我" : nickname + " · " + skinName + " · 我"
        localRow.savedNickname = nickname
        localRow.update(name: localName, image: portrait(skinID), keys: keys, clicks: clicks)
        emptyMessage.isHidden = !peers.isEmpty || !error.isEmpty
        emptyMessage.stringValue = inRoom ? "将房间码发给朋友，对方加入后就会出现在这里。" : (networking ? "让朋友连上同一局域网，再打开局域网开关。" : "加入房间后，朋友就能看到你的小猪和累计次数。")
        updatePeers(peers)
        scaleValue.stringValue = "\(Int((scale * 100).rounded()))%"
        labelsSwitch.state = showLabels ? .on : .off
        clickThroughSwitch.state = clickThrough ? .on : .off
        for skin in PetSkin.all {
            guard let button = skinButtons[skin.id] else { continue }
            let selected = skin.id == skinID
            button.title = skin.isDrawn ? skin.name : String(skin.name.dropFirst(2))
            button.toolTip = selected ? skin.name + "（当前）" : skin.name
            button.layer?.borderWidth = selected ? 1.5 : 0.5
            button.layer?.borderColor = mutedBrown.withAlphaComponent(selected ? 0.8 : 0.3).cgColor
            button.layer?.backgroundColor = (selected ? sage : paper).cgColor
            button.contentTintColor = darkBrown
        }
    }

    private func updatePeers(_ peers: [(String, LocalPeers.Peer)]) {
        let ids = Set(peers.map { $0.0 })
        for id in Array(peerRows.keys) where !ids.contains(id) {
            peerRows.removeValue(forKey: id)?.removeFromSuperview()
        }
        let height = max(peerScroll.contentSize.height, CGFloat(peers.count) * 62)
        if peerDocument.frame.size != NSSize(width: 264, height: height) {
            peerDocument.frame = NSRect(x: 0, y: 0, width: 264, height: height)
        }
        for (index, item) in peers.enumerated() {
            let (id, peer) = item
            let row: PeerRow
            if let existing = peerRows[id] { row = existing }
            else {
                row = PeerRow(frame: .zero)
                peerRows[id] = row
                peerDocument.addSubview(row)
            }
            let frame = NSRect(x: 0, y: CGFloat(index) * 62, width: 264, height: 56)
            if row.frame != frame { row.frame = frame }
            let skinName = PetSkin.named(peer.skin).name
            let displayName = peer.nickname.isEmpty ? skinName : peer.nickname + " · " + skinName
            row.update(name: displayName + " · " + String(id.prefix(4)), image: portrait(peer.skin), keys: peer.keys, clicks: peer.clicks)
        }
    }

    private func portrait(_ id: String) -> NSImage? {
        let skin = PetSkin.named(id)
        if let cached = portraitCache[skin.id] { return cached }
        let image = skin.portrait(size: NSSize(width: 38, height: 40))
        portraitCache[skin.id] = image
        return image
    }

    @objc private func changeTab() {
        petScroll.isHidden = tabs.selectedSegment != 0
        lanPage.isHidden = tabs.selectedSegment != 1
    }

    @objc private func selectSkin(_ sender: NSButton) {
        guard let id = sender.identifier?.rawValue else { return }
        onSelectSkin?(id)
    }
    @objc private func decreaseScale() { onScaleChange?(-0.1) }
    @objc private func increaseScale() { onScaleChange?(0.1) }
    @objc private func toggleLabels(_ sender: NSButton) { onShowLabelsChange?(sender.state == .on) }
    @objc private func toggleClickThrough(_ sender: NSButton) { onClickThroughChange?(sender.state == .on) }
    func setNetworkError(_ message: String) { networkError = message }
    @objc private func toggleSogou(_ sender: NSButton) { onHideForSogouChange?(sender.state == .on) }
    @objc private func createRoomAction() { networkError = ""; onCreateRoom?() }
    @objc private func joinRoomAction() { networkError = ""; onJoinRoom?(joinCode.stringValue) }
    @objc private func exitRoomAction() { onExitRoom?() }
    @objc private func copyRoomAction() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(roomCodeLabel.stringValue, forType: .string)
    }
    @objc private func toggleNetworking(_ sender: NSButton) {
        networkError = ""
        onNetworkingChange?(sender.state == .on)
    }
    @objc private func returnToCorner() { onReturnToCorner?() }
    @objc private func quitApp() { onQuit?() }
    @objc private func closePanel() { onClose?() }

    private func sectionLabel(_ value: String) -> NSTextField {
        let label = NSTextField(labelWithString: value)
        label.font = .systemFont(ofSize: 12, weight: .semibold)
        label.textColor = mutedBrown
        return label
    }

    private func controlButton(_ title: String, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.isBordered = false
        button.wantsLayer = true
        button.layer?.backgroundColor = NSColor(calibratedRed: 239 / 255, green: 231 / 255, blue: 214 / 255, alpha: 1).cgColor
        button.layer?.cornerRadius = 8
        button.font = .systemFont(ofSize: 17, weight: .medium)
        button.contentTintColor = darkBrown
        return button
    }

    private func footerButton(_ title: String, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.isBordered = false
        button.wantsLayer = true
        button.layer?.backgroundColor = sage.cgColor
        button.layer?.cornerRadius = 9
        button.font = .systemFont(ofSize: 12, weight: .semibold)
        button.contentTintColor = darkBrown
        return button
    }

    private func add(_ child: NSView, x: CGFloat, top: CGFloat, width: CGFloat, height: CGFloat, to root: NSView) {
        child.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(child)
        NSLayoutConstraint.activate([
            child.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: x),
            child.topAnchor.constraint(equalTo: root.topAnchor, constant: top),
            child.widthAnchor.constraint(equalToConstant: width),
            child.heightAnchor.constraint(equalToConstant: height)
        ])
    }

    private func add(_ child: NSView, trailing: CGFloat, top: CGFloat, width: CGFloat, height: CGFloat, to root: NSView) {
        child.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(child)
        NSLayoutConstraint.activate([
            child.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -trailing),
            child.topAnchor.constraint(equalTo: root.topAnchor, constant: top),
            child.widthAnchor.constraint(equalToConstant: width),
            child.heightAnchor.constraint(equalToConstant: height)
        ])
    }
}

private final class SettingsCardView: NSView {
    var closeAction: (() -> Void)?
    override var acceptsFirstResponder: Bool { true }
    override func cancelOperation(_ sender: Any?) { closeAction?() }
}

private final class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

private final class PeerRow: NSView {
    private let portrait = NSImageView()
    private let name = NSTextField(labelWithString: "")
    private let keys = NSTextField(labelWithString: "")
    private let clicks = NSTextField(labelWithString: "")
    private let rename = NSButton(title: "改名", target: nil, action: nil)
    private let nameInput = NSTextField()
    private let saveName = NSButton(title: "保存", target: nil, action: nil)
    private let cancelName = NSButton(title: "取消", target: nil, action: nil)
    private let nameError = NSTextField(labelWithString: "")
    var canRename = false { didSet { rename.isHidden = !canRename; needsLayout = true } }
    var savedNickname = ""
    var onSaveNickname: ((String) -> Bool)?
    private var editingName = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor(calibratedRed: 239/255, green: 235/255, blue: 221/255, alpha: 1).cgColor
        layer?.cornerRadius = 9
        portrait.imageScaling = .scaleProportionallyUpOrDown
        addSubview(portrait)
        for label in [name, keys, clicks] {
            label.lineBreakMode = .byTruncatingTail
            label.textColor = NSColor(calibratedRed: 77/255, green: 54/255, blue: 39/255, alpha: 1)
            addSubview(label)
        }
        name.lineBreakMode = .byTruncatingMiddle
        name.font = .systemFont(ofSize: 12, weight: .semibold)
        rename.target = self
        rename.action = #selector(beginRename)
        rename.isHidden = true
        addSubview(rename)
        nameInput.placeholderString = "留空使用毛色名"
        nameInput.target = self
        nameInput.action = #selector(saveRename)
        nameInput.isHidden = true
        addSubview(nameInput)
        saveName.target = self
        saveName.action = #selector(saveRename)
        saveName.isHidden = true
        addSubview(saveName)
        cancelName.target = self
        cancelName.action = #selector(cancelRename)
        cancelName.isHidden = true
        addSubview(cancelName)
        nameError.font = .systemFont(ofSize: 10)
        nameError.textColor = NSColor(calibratedRed: 0.65, green: 0.30, blue: 0.22, alpha: 1)
        nameError.stringValue = "名称无效或超过 16 个字符"
        nameError.isHidden = true
        addSubview(nameError)
        keys.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
        clicks.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
    }
    required init?(coder: NSCoder) { super.init(coder: coder) }
    override func layout() {
        super.layout()
        portrait.frame = NSRect(x: 8, y: 8, width: 48, height: 40)
        name.frame = NSRect(x: 62, y: bounds.height - 24, width: bounds.width - (canRename ? 124 : 70), height: 18)
        rename.frame = NSRect(x: bounds.width - 59, y: bounds.height - 28, width: 54, height: 24)
        nameInput.frame = NSRect(x: 62, y: bounds.height - 28, width: bounds.width - 174, height: 24)
        saveName.frame = NSRect(x: bounds.width - 108, y: bounds.height - 28, width: 50, height: 24)
        cancelName.frame = NSRect(x: bounds.width - 57, y: bounds.height - 28, width: 52, height: 24)
        nameError.frame = NSRect(x: 62, y: 7, width: bounds.width - 70, height: 16)
        let column = (bounds.width - 70) / 2
        keys.frame = NSRect(x: 62, y: 7, width: column - 4, height: 16)
        clicks.frame = NSRect(x: 62 + column, y: 7, width: column - 4, height: 16)
    }
    func update(name value: String, image: NSImage?, keys keyCount: UInt64, clicks clickCount: UInt64) {
        if name.stringValue != value { name.stringValue = value; name.toolTip = value }
        if portrait.image !== image { portrait.image = image }
        let keyText = "按键 \(Self.compact(keyCount))", clickText = "点击 \(Self.compact(clickCount))"
        if keys.stringValue != keyText { keys.stringValue = keyText }
        if clicks.stringValue != clickText { clicks.stringValue = clickText }
        keys.toolTip = "按键 \(keyCount)"
        clicks.toolTip = "点击 \(clickCount)"
    }
    @objc private func beginRename() {
        editingName = true
        nameInput.stringValue = savedNickname
        nameError.isHidden = true
        updateEditing()
        window?.makeFirstResponder(nameInput)
    }
    @objc private func saveRename() {
        guard onSaveNickname?(nameInput.stringValue) == true else {
            nameError.isHidden = false
            keys.isHidden = true
            clicks.isHidden = true
            return
        }
        editingName = false
        updateEditing()
    }
    @objc private func cancelRename() { editingName = false; updateEditing() }
    private func updateEditing() {
        name.isHidden = editingName
        rename.isHidden = editingName || !canRename
        nameInput.isHidden = !editingName
        saveName.isHidden = !editingName
        cancelName.isHidden = !editingName
        nameError.isHidden = true
        keys.isHidden = false
        clicks.isHidden = false
    }
    private static func compact(_ value: UInt64) -> String {
        let unit: UInt64
        let suffix: String
        if value >= 100_000_000 { unit = 100_000_000; suffix = "亿" }
        else if value >= 10_000 { unit = 10_000; suffix = "万" }
        else { return String(value) }
        let whole = value / unit, tenth = (value % unit) / (unit / 10)
        return tenth == 0 ? "\(whole)\(suffix)" : "\(whole).\(tenth)\(suffix)"
    }
}
