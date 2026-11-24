import AppKit

// Runs against the packaged application on both Mac architectures in CI.
@MainActor func runSmokeTest() {
    let ownID = String(repeating: "a", count: 32)
    let remoteID = String(repeating: "b", count: 32)
    func parse(_ text: String) -> (String, LocalPeers.Peer)? {
        LocalPeers.parse(bytes: Array(text.utf8), ownID: ownID, now: 10)
    }
    let valid = "WK1|\(remoteID)|longhair|123|45"
    guard let parsed = parse(valid) else { fatalError("Valid peer packet rejected") }
    precondition(parsed.0 == remoteID && parsed.1.keys == 123 && parsed.1.clicks == 45)
    precondition(parsed.1.skin == "pixel-longhair-gold-white", "Old peer coat was not mapped")
    precondition(PetSkin.named("gold-white").id == "pixel-golden-white", "Old profile coat was not mapped")
    precondition(PetSkin.all.first?.id == "pixel-tricolor", "Pixel appearance must remain the default")
    precondition(PetSkin.named("drawn-tricolor").name == "手绘三花")
    precondition(PetSkin.named("longhair").id == "pixel-longhair-gold-white", "Legacy network coat was not preserved")
    for invalid in ["WK1|\(ownID)|longhair|1|2", "WK1|\(remoteID)|unknown|1|2",
                    "WK1|\(remoteID)|longhair|-1|2", "WK1|\(remoteID)|longhair|9223372036854775808|2",
                    "WK1|\(remoteID)|longhair|1|2\n", valid + String(repeating: "0", count: 129)] {
        precondition(parse(invalid) == nil, "Invalid peer packet accepted")
    }
    precondition(LocalPeers.normalizedNickname("  小猪  ") == "小猪")
    precondition(LocalPeers.normalizedNickname("") == "")
    precondition(LocalPeers.normalizedNickname("🐹‍🐹") == "🐹‍🐹")
    precondition(LocalPeers.normalizedNickname(String(repeating: "猪", count: 17)) == nil)
    precondition(LocalPeers.normalizedNickname("A\nB") == nil)
    precondition(LocalPeers.normalizedNickname("A\u{202E}B") == nil)
    func namePacket(_ id: String, _ name: String) -> [UInt8] {
        Array("WN1|\(id)|\(Data(name.utf8).base64EncodedString())".utf8)
    }
    var namedPeers: [String: LocalPeers.Peer] = [:]
    LocalPeers.apply(bytes: namePacket(remoteID, "小猪"), ownID: ownID, now: 11, peers: &namedPeers)
    precondition(namedPeers.isEmpty, "Name packet must not create a peer")
    LocalPeers.apply(bytes: Array(valid.utf8), ownID: ownID, now: 12, peers: &namedPeers)
    LocalPeers.apply(bytes: namePacket(remoteID, "小猪"), ownID: ownID, now: 13, peers: &namedPeers)
    precondition(namedPeers[remoteID]?.nickname == "小猪" && namedPeers[remoteID]?.seen == 12)
    LocalPeers.apply(bytes: Array(valid.utf8), ownID: ownID, now: 14, peers: &namedPeers)
    precondition(namedPeers[remoteID]?.nickname == "小猪" && namedPeers[remoteID]?.seen == 14)
    LocalPeers.apply(bytes: namePacket(remoteID, ""), ownID: ownID, now: 15, peers: &namedPeers)
    precondition(namedPeers[remoteID]?.nickname == "" && namedPeers[remoteID]?.seen == 14)
    precondition(parse(String(decoding: namePacket(remoteID, "小猪"), as: UTF8.self)) == nil)
    let peerMap = [parsed.0: parsed.1]
    precondition(LocalPeers.unexpired(peerMap, now: 17.99).count == 1)
    precondition(LocalPeers.unexpired(peerMap, now: 18).isEmpty)
    print("Verified LAN packet validation and expiry")
    let invite = "ABCDEFGHIJKLMNOP47UL"
    precondition(RoomTransport.normalizeCode("abcde-fghij-klmno-p47ul") == invite)
    precondition(RoomTransport.normalizeCode("abcde-fghij-klmno-p47um") == nil)
    precondition(LocalPeers.unexpired(peerMap, now: 29.99, expiry: 20).count == 1)
    precondition(LocalPeers.unexpired(peerMap, now: 30, expiry: 20).isEmpty)
    _ = NSApplication.shared
    let settings = PetSettingsViewController()
    let settingsView = settings.view
    func descendants(_ view: NSView) -> [NSView] {
        [view] + view.subviews.flatMap { descendants($0) }
    }
    guard let tabs = descendants(settingsView).compactMap({ $0 as? NSSegmentedControl }).first else {
        fatalError("Settings tabs missing")
    }
    precondition(tabs.selectedSegment == 0, "Settings must start on pet tab")
    settingsView.layoutSubtreeIfNeeded()
    let skinButtons = descendants(settingsView).compactMap { $0 as? NSButton }.filter {
        let id = $0.identifier?.rawValue ?? ""
        return id.hasPrefix("pixel-") || id.hasPrefix("drawn-")
    }
    precondition(skinButtons.count == 20 && skinButtons.allSatisfy { settingsView.bounds.contains(settingsView.convert($0.bounds, from: $0)) })
    tabs.selectedSegment = 1
    tabs.sendAction(tabs.action, to: tabs.target)
    let firstPeer = LocalPeers.Peer(skin: "pixel-longhair-gold-white", keys: 12_345, clicks: 100_000_000, seen: 10)
    settings.refresh(skinID: "pixel-tricolor", scale: 1, showLabels: true, clickThrough: false,
                     hideForSogou: true, networking: true, keys: 98_765, clicks: 3,
                     peers: [(remoteID, firstPeer)], networkStatus: "连接中", roomCode: invite, roomConnected: false, nickname: "")
    settingsView.layoutSubtreeIfNeeded()
    func visibleText() -> [NSTextField] {
        descendants(settingsView).compactMap { $0 as? NSTextField }.filter { !$0.isHiddenOrHasHiddenAncestor }
    }
    guard let rename = descendants(settingsView).compactMap({ $0 as? NSButton }).first(where: { $0.title == "改名" }) else {
        fatalError("Rename button missing")
    }
    rename.sendAction(rename.action, to: rename.target)
    guard let nameInput = descendants(settingsView).compactMap({ $0 as? NSTextField }).first(where: { $0.placeholderString == "留空使用毛色名" }) else {
        fatalError("Nickname editor missing")
    }
    nameInput.stringValue = "未保存的草稿"
    settings.refresh(skinID: "pixel-tricolor", scale: 1, showLabels: true, clickThrough: false,
                     hideForSogou: true, networking: true, keys: 98_766, clicks: 3,
                     peers: [(remoteID, firstPeer)], networkStatus: "连接中", roomCode: invite, roomConnected: false, nickname: "")
    precondition(nameInput.stringValue == "未保存的草稿", "Refresh replaced nickname draft")
    let firstTexts = visibleText()
    precondition(firstTexts.contains { $0.stringValue == "房间：连接中" })
    precondition(firstTexts.contains { $0.stringValue == "按键 1.2万" && $0.toolTip == "按键 12345" })
    precondition(firstTexts.contains { $0.stringValue == "点击 1亿" && $0.toolTip == "点击 100000000" })
    guard let leave = descendants(settingsView).compactMap({ $0 as? NSButton }).first(where: { $0.title == "退出房间" }) else {
        fatalError("Leave room button missing")
    }
    let leaveBounds = settingsView.convert(leave.bounds, from: leave)
    precondition(settingsView.bounds.contains(leaveBounds), "Room controls outside settings view")
    settings.refresh(skinID: "pixel-tricolor", scale: 1, showLabels: true, clickThrough: false,
                     hideForSogou: true, networking: true, keys: 98_765, clicks: 3,
                     peers: [], networkStatus: "已连接", roomCode: invite, roomConnected: true, nickname: "")
    let nextTexts = visibleText()
    precondition(nextTexts.contains { $0.stringValue == "房间已连接 · 伙伴 0" })
    precondition(!nextTexts.contains { $0.stringValue == "像素长毛黄白 · bbbb" })
    print("Verified settings tab, room state and peer refresh")
    let size = NSRect(x: 0, y: 0, width: 400, height: 308)
    let window = NSWindow(contentRect: size, styleMask: [.borderless], backing: .buffered, defer: false)
    let view = PetView(frame: size)
    window.contentView = view

    func snapshot() -> Data {
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            fatalError("No native bitmap context")
        }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        let visibleBody = (1..<10).contains { column in
            let color = bitmap.colorAt(x: bitmap.pixelsWide * column / 10, y: bitmap.pixelsHigh / 2)
            return (color?.alphaComponent ?? 0) > 0.9
        }
        guard visibleBody, let png = bitmap.representation(using: .png, properties: [:]) else {
            fatalError("Empty desktop pet frame")
        }
        return png
    }

    func regionFingerprint(x: Range<Int>, y: Range<Int>) -> [UInt8] {
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            fatalError("No native bitmap context")
        }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        var result: [UInt8] = []
        let scaleX = CGFloat(bitmap.pixelsWide) / 400
        let scaleY = CGFloat(bitmap.pixelsHigh) / 308
        for row in stride(from: y.lowerBound, to: y.upperBound, by: 2) {
            for column in stride(from: x.lowerBound, to: x.upperBound, by: 2) {
                let pixelX = Int((CGFloat(column) * scaleX).rounded())
                let pixelY = Int((CGFloat(row) * scaleY).rounded())
                guard let color = bitmap.colorAt(x: pixelX, y: pixelY)?.usingColorSpace(.deviceRGB) else {
                    fatalError("Cannot sample desktop artwork")
                }
                var red: CGFloat = 0, green: CGFloat = 0, blue: CGFloat = 0, alpha: CGFloat = 0
                color.getRed(&red, green: &green, blue: &blue, alpha: &alpha)
                result.append(contentsOf: [red, green, blue, alpha].map {
                    UInt8(clamping: Int(($0 * 255).rounded()))
                })
            }
        }
        return result
    }

    var idleFrames = Set<Data>()
    let root = Bundle.main.resourceURL!
    for skin in PetSkin.all {
        var files = [skin.body, skin.paw,
                     skin.isDrawn ? "drawn-keyboard.png" : "keyboard.png",
                     skin.isDrawn ? "drawn-mouse.png" : "mouse.png",
                     "mouth-peep.png", "mouth-yawn.png"]
        if skin.isDrawn { files.append(skin.networkID + "-eyes.png") }
        for file in files {
            precondition(NSImage(contentsOf: root.appendingPathComponent(file)) != nil, "Missing sprite: \(file)")
        }
        view.selectSkin(skin.id)
        let idle = snapshot()
        precondition(idleFrames.insert(idle).inserted, "Identical appearance: \(skin.id)")
        if skin.isDrawn {
            precondition(skin.portrait(size: NSSize(width: 38, height: 40)) != nil, "Missing drawn portrait: \(skin.id)")
        }
        print("Rendered \(skin.id)")
    }
    view.selectSkin("pixel-californian")
    let pixelKeyboard = regionFingerprint(x: 315..<347, y: 0..<308)
    let pixelMouse = regionFingerprint(x: 70..<90, y: 0..<308)
    view.selectSkin("drawn-tricolor")
    let drawnKeyboard = regionFingerprint(x: 315..<347, y: 0..<308)
    let drawnMouse = regionFingerprint(x: 70..<90, y: 0..<308)
    precondition(pixelKeyboard != drawnKeyboard, "Keyboard artwork did not switch with skin")
    precondition(pixelMouse != drawnMouse, "Mouse artwork did not switch with skin")
    view.selectSkin("pixel-californian")
    precondition(regionFingerprint(x: 315..<347, y: 0..<308) == pixelKeyboard, "Keyboard cache crossed skin switch")
    precondition(regionFingerprint(x: 70..<90, y: 0..<308) == pixelMouse, "Mouse cache crossed skin switch")

    view.selectSkin("drawn-tricolor")
    let drawnIdle = snapshot()
    view.pressKey(0)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.075))
    precondition(snapshot() != drawnIdle, "Drawn skin key press did not animate")
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.42))
    let drawnMouseIdle = snapshot()
    view.moveMouse(dx: 140, dy: 160)
    view.clickMouse(right: false)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.085))
    precondition(snapshot() != drawnMouseIdle, "Drawn skin mouse grip did not animate")

    view.selectSkin("pixel-californian")
    let idle = snapshot()
    view.pressKey(0) // ANSI A
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.075))
    let typing = snapshot()
    precondition(typing != idle, "Key press did not animate")
    if let i = CommandLine.arguments.firstIndex(of: "--smoke-test"), i + 1 < CommandLine.arguments.count {
        do {
            try typing.write(to: URL(fileURLWithPath: CommandLine.arguments[i + 1]))
        } catch {
            fatalError("Cannot export test frame: \(error)")
        }
    }
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.42))
    view.moveMouse(dx: 140, dy: 160)
    view.clickMouse(right: false)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.085))
    precondition(snapshot() != idle, "Mouse input did not animate")
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.9))
    let parked = snapshot()
    let deadline = Date(timeIntervalSinceNow: 7)
    var closed = false
    var reopened = false
    while Date() < deadline && !reopened {
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.028))
        if snapshot() != parked { closed = true } else if closed { reopened = true }
    }
    precondition(reopened, "Idle pet did not blink and reopen its eyes")
    let beforeScroll = snapshot()
    view.scrollMouse(-120)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.09))
    precondition(snapshot() != beforeScroll, "Mouse wheel did not animate")
    precondition(PetView.dozePose(idle: 29.99) == nil)
    precondition(PetView.dozePose(idle: 30)?.frame.eyes == 1)
    precondition(PetView.dozePose(idle: 31.5)?.frame.mouth == 2)
    let restingTime = Date()
    view.noteInput(ProcessInfo.processInfo.systemUptime)
    RunLoop.current.run(until: restingTime.addingTimeInterval(1))
    let resting = snapshot()
    RunLoop.current.run(until: restingTime.addingTimeInterval(31.5))
    precondition(snapshot() != resting, "Idle timer did not render the yawn")
    view.pressKey(0)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.65))
    precondition(snapshot() == resting, "Key input did not restore the awake face")
    print("Verified all skins, keyboard/mouse animation, blinking, idle yawn and wake-up")
}
