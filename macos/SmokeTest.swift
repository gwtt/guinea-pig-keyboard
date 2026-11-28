import AppKit

// Runs against the packaged application on both Mac architectures in CI.
@MainActor func runSmokeTest() {
    func precondition(_ condition: @autoclosure () -> Bool, _ message: @autoclosure () -> String = "Smoke check failed",
                      file: StaticString = #fileID, line: UInt = #line) {
        guard condition() else { fatalError(message(), file: file, line: line) }
    }
    try! runWheekPackSmokeTest()
    precondition(!AnonymousKeyboardInput.canRun(authorized: false, secureInput: false))
    precondition(!AnonymousKeyboardInput.canRun(authorized: true, secureInput: true))
    precondition(AnonymousKeyboardInput.canRun(authorized: true, secureInput: false))
    var gate = InputPulseGate()
    gate.noteHID(at: 1); gate.notePrecise(at: 1.02)
    precondition(gate.takeReady(at: 2) == 0, "HID 优先时不得重复")
    gate.notePrecise(at: 3); gate.noteHID(at: 3.02)
    precondition(gate.takeReady(at: 4) == 0, "精确事件优先时不得重复")
    gate.noteHID(at: 5)
    precondition(gate.takeReady(at: 5.05) == 0 && gate.takeReady(at: 5.2) == 1, "缺失精确事件时仅补一次")
    gate.noteHID(at: 6); gate.reset()
    precondition(gate.takeReady(at: 7) == 0, "撤权或安全输入时必须丢弃待处理脉冲")
    func releaseData(_ tag: String, asset: String = "WheekKeys-macOS.zip", host: String = "github.com", prerelease: Bool = false) -> Data {
        let json: [String: Any] = ["tag_name": tag, "draft": false, "prerelease": prerelease,
            "assets": [["name": asset, "browser_download_url": "https://\(host)/gwtt/guinea-pig-keyboard/releases/download/\(tag)/\(asset)"]]]
        return try! JSONSerialization.data(withJSONObject: json)
    }
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.2.0"), currentVersion: "0.1.5")) != nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.1.5"), currentVersion: "0.1.5")) == nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.1.4"), currentVersion: "0.1.5")) == nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.2.0", asset: "WheekKeys.exe"), currentVersion: "0.1.5")) == nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.2.0", host: "example.com"), currentVersion: "0.1.5")) == nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.2.0", prerelease: true), currentVersion: "0.1.5")) == nil)
    precondition((try? PetUpdateChecker.updateURL(data: releaseData("v0.2.0-beta"), currentVersion: "0.1.5")) == nil)
    let releasePage = URL(string: "https://github.com/gwtt/guinea-pig-keyboard/releases/tag/v0.1.6")!
    precondition((try! PetUpdateChecker.updateURL(releasePage: releasePage, currentVersion: "0.1.5"))?.lastPathComponent == "WheekKeys-macOS.zip")
    precondition((try! PetUpdateChecker.updateURL(releasePage: releasePage, currentVersion: "0.1.6")) == nil)
    let otherReleasePage = URL(string: "https://github.com/example/guinea-pig-keyboard/releases/tag/v0.1.6")!
    precondition((try? PetUpdateChecker.updateURL(releasePage: otherReleasePage, currentVersion: "0.1.5")) == nil)
    precondition(initialPetScale(nil) == 0.6 && initialPetScale(-1) == 0.6)
    precondition(initialPetScale(0) == 0 && initialPetScale(0.58) == 0.58 && initialPetScale(1.25) == 1)
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
    let localCustom = PetSkin.registerCustom(id: "wheek-network-test", name: "测试", baseCoat: "pixel-longhair-cream", author: nil,
                                             furColor: nil, inkColor: nil, eyeColor: nil, body: nil, paw: nil, mouth: nil)
    precondition(localCustom.appearanceNetworkID == "pixel-longhair-cream")
    precondition(parse("WK1|\(remoteID)|wheek-network-test|1|2") == nil)
    PetSkin.unregisterCustom(id: localCustom.id)
    precondition(PetSkin.all.count == 22 && PetSkin.all.filter { $0.isProne }.count == 8)
    precondition(PetSkin.all.allSatisfy { !$0.name.isEmpty && !$0.name.hasPrefix("像素") })
    for (legacy, pixel) in [("tricolor", "tricolor"), ("californian", "californian"),
                            ("gold-white", "golden-white"), ("teddy", "chocolate"),
                            ("silver-agouti", "black-white"), ("longhair", "longhair-gold-white")] {
        precondition(PetSkin.named("drawn-" + legacy).id == "pixel-" + pixel)
        precondition(parse("WK1|\(remoteID)|drawn-\(legacy)|1|2")?.1.skin == "pixel-" + pixel)
    }
    precondition(PetSkin.find("drawn-unknown") == nil)
    precondition(parse("WK1|\(remoteID)|drawn-unknown|1|2") == nil)
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
    print("Verified room state packet validation")
    let invite = "ABCDEFGHIJKLMNOP47UL"
    precondition(RoomTransport.normalizeCode("abcde-fghij-klmno-p47ul") == invite)
    precondition(RoomTransport.normalizeCode("abcde-fghij-klmno-p47um") == nil)
    let hostID = String(repeating: "a", count: 32)
    let waitingID = String(format: "%032x", 17)
    let host = RoomMembership(ownID: hostID, hosting: true, isPublic: true, expectedHost: nil)
    let waiting = RoomMembership(ownID: waitingID, hosting: false, isPublic: true, expectedHost: hostID)
    for id in 2...17 {
        host.receive(["WJ2", Substring(String(format: "%032x", id))], now: 1)
    }
    let full = host.nextPacket(now: 1)!
    precondition(RoomTransport.validPlaintext(full))
    waiting.receive(full.split(separator: "|"), now: 1)
    precondition(host.count == 16 && waiting.count == 16 && !waiting.joined)
    for id in 3...16 {
        host.receive(["WJ2", Substring(String(format: "%032x", id))], now: 7)
    }
    host.expire(now: 11)
    host.receive(["WJ2", Substring(waitingID)], now: 11)
    waiting.receive(host.nextPacket(now: 11)!.split(separator: "|"), now: 11)
    waiting.receive(full.split(separator: "|"), now: 11.1)
    precondition(waiting.joined && waiting.count == 16)
    waiting.expire(now: 21)
    precondition(!waiting.joined)
    let advertisement = "WD2|\(invite)|\(hostID)|16|6Iqx55Sf"
    precondition(RoomDirectory.parse(advertisement, now: 11)?.name == "花生")
    precondition(RoomDirectory.parse(advertisement, now: 11)?.label == "花生的公开房间")
    precondition(RoomDirectory.parse("WD2|\(invite)|\(hostID)|2|", now: 11)?.label == "小豚鼠的公开房间")
    precondition(RoomDirectory.parse(advertisement.replacingOccurrences(of: "|16|", with: "|17|"), now: 11) == nil)
    precondition(LocalPeers.unexpired(peerMap, now: 29.99).count == 1)
    precondition(LocalPeers.unexpired(peerMap, now: 30).isEmpty)
    func apply(_ message: String, now: TimeInterval) -> Bool {
        LocalPeers.apply(bytes: Array(message.utf8), ownID: ownID, now: now, peers: &namedPeers)
    }
    let appearance = "WS1|\(remoteID)|drawn-tricolor|1"
    let action = "WA1|\(remoteID)|1|k30,k328,l,r"
    precondition(RoomTransport.validPlaintext(appearance) && RoomTransport.validPlaintext(action))
    precondition(!RoomTransport.validPlaintext("WA1|\(remoteID)|1|") &&
                 !RoomTransport.validPlaintext(action + "\n"))
    precondition(namedPeers[remoteID]?.supportsActivity == false && !apply(action, now: 16))
    let previousSkin = namedPeers[remoteID]?.skin
    precondition(apply("WS1|\(remoteID)|drawn-unknown|1", now: 16))
    precondition(namedPeers[remoteID]?.skin == previousSkin && namedPeers[remoteID]?.supportsActivity == true &&
                 namedPeers[remoteID]?.sharingActivity == true)
    for skin in PetSkin.all where skin.isProne {
        precondition(apply("WS1|\(remoteID)|\(skin.id)|1", now: 16))
        precondition(namedPeers[remoteID]?.skin == skin.id)
    }
    precondition(apply(appearance, now: 16) && apply(action, now: 16))
    precondition(apply(valid, now: 16.1) && namedPeers[remoteID]?.skin == "pixel-tricolor")
    precondition(!apply(action, now: 16.2) && !apply("WA1|\(remoteID)|2|k0,l", now: 16.2))
    precondition(namedPeers[remoteID]?.takeAction(now: 16.2) == .key(0))
    precondition(namedPeers[remoteID]?.takeAction(now: 16.23) == nil)
    precondition(namedPeers[remoteID]?.takeAction(now: 16.26) == .key(126))
    precondition(namedPeers[remoteID]?.takeAction(now: 16.32) == .click(false))
    precondition(namedPeers[remoteID]?.takeAction(now: 16.45) == nil)
    precondition(namedPeers[remoteID]?.takeAction(now: 16.51) == .click(true))
    precondition(apply("WA1|\(remoteID)|2|k57", now: 17))
    precondition(namedPeers[remoteID]?.takeAction(now: 17.9) == nil)
    precondition(apply("WA1|\(remoteID)|3|r", now: 18))
    precondition(apply("WS1|\(remoteID)|drawn-tricolor|0", now: 18))
    precondition(namedPeers[remoteID]?.supportsActivity == true && namedPeers[remoteID]?.sharingActivity == false)
    precondition(!apply("WA1|\(remoteID)|4|l", now: 18) && namedPeers[remoteID]?.takeAction(now: 18) == nil)
    precondition(apply("WS1|\(remoteID)|drawn-tricolor|1", now: 18.1))
    precondition(apply("WA1|\(remoteID)|5|r,k349", now: 18.1))
    precondition(namedPeers[remoteID]?.takeAction(now: 18.1) == .click(true))
    precondition(namedPeers[remoteID]?.takeAction(now: 18.2) == nil)
    precondition(namedPeers[remoteID]?.takeAction(now: 18.3) == .key(110))
    precondition(!apply("WS1|\(ownID)|drawn-tricolor|1", now: 18) && namedPeers.count == 1)
    precondition(apply("WA1|\(remoteID)|6|k511", now: 19))
    precondition(namedPeers[remoteID]?.takeAction(now: 19) == .anonymousKey)
    precondition(NetworkKeyCodes.scan(for: 0) == 30 && NetworkKeyCodes.scan(for: 49) == 57 &&
                 NetworkKeyCodes.scan(for: 62) == 285 && NetworkKeyCodes.scan(for: 76) == 284 &&
                 NetworkKeyCodes.scan(for: 110) == 349)
    print("Verified cross-platform activity packets, key codes, pause and expiry")
    _ = NSApplication.shared
    let settings = PetSettingsViewController()
    let settingsView = settings.view
    func descendants(_ view: NSView) -> [NSView] {
        [view] + view.subviews.flatMap { descendants($0) }
    }
    guard let tabs = descendants(settingsView).compactMap({ $0 as? NSSegmentedControl }).first(where: { $0.segmentCount == 4 && $0.label(forSegment: 0) == "外观" }) else {
        fatalError("Settings tabs missing")
    }
    precondition(tabs.selectedSegment == 0, "Settings must start on pet tab")
    func updateButton() -> NSButton? {
        descendants(settingsView).compactMap { $0 as? NSButton }.first { $0.title == "更新到最新版本" }
    }
    precondition(updateButton()?.isHiddenOrHasHiddenAncestor ?? true)
    settings.setUpdateAvailable(true)
    settingsView.layoutSubtreeIfNeeded()
    guard let availableUpdateButton = updateButton() else {
        fatalError("Update button missing")
    }
    precondition(!availableUpdateButton.isHidden)
    let version = "v" + (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "未知")
    guard let versionLabel = descendants(settingsView).compactMap({ $0 as? NSTextField })
        .first(where: { $0.stringValue == version }) else { fatalError("Version label missing") }
    let versionFrame = settingsView.convert(versionLabel.bounds, from: versionLabel)
    let updateFrame = settingsView.convert(availableUpdateButton.bounds, from: availableUpdateButton)
    precondition(abs(versionFrame.midY - updateFrame.midY) >= 15)
    settings.setUpdateAvailable(false)
    precondition(availableUpdateButton.isHidden)
    settingsView.layoutSubtreeIfNeeded()
    let controls = descendants(settingsView).compactMap { $0 as? NSButton }
    precondition(controls.contains { $0.title == "反馈问题" && !$0.isHiddenOrHasHiddenAncestor })
    precondition(!controls.contains { $0.title == "回到右下角" || $0.title.contains("局域网") })
    guard let login = controls.first(where: { $0.title == "开机自启" }) else { fatalError("Login switch missing") }
    settings.setLoginAtStartup(enabled: true, pending: false, message: "请在系统设置中允许")
    precondition(login.state == .on)
    settings.setLoginAtStartup(enabled: false, pending: false, message: "")
    precondition(login.state == .off)
    var loginRequested = false
    settings.onLoginAtStartupChange = { loginRequested = $0 }
    login.performClick(nil)
    precondition(loginRequested, "Login switch did not report the requested state")
    settings.setLoginAtStartup(enabled: false, pending: true, message: "待批准")
    precondition(login.state == .mixed)
    login.performClick(nil)
    precondition(!loginRequested, "Pending login registration should be cancellable")
    guard let scale = descendants(settingsView).compactMap({ $0 as? NSTextField }).first(where: { $0.stringValue == "60%" }) else {
        fatalError("Scale display missing")
    }
    let sliders = descendants(settingsView).compactMap { $0 as? NSSlider }
    precondition(sliders.count == 2 && sliders.allSatisfy { $0.minValue == 0 && $0.maxValue == 1 })
    let sizeControl = sliders.first { $0.doubleValue == 0.6 }!
    let opacityControl = sliders.first { $0.doubleValue == 1 }!
    settings.refreshOptions(opacity: 0.6, desk: "mocha")
    var sizeChanged = -1.0, opacityChanged = -1.0, desk = ""
    settings.onScaleChange = { sizeChanged = $0 }
    settings.onOpacityChange = { opacityChanged = $0 }
    settings.onDeskSkinChange = { desk = $0 }
    sizeControl.doubleValue = 0; sizeControl.sendAction(sizeControl.action, to: sizeControl.target)
    opacityControl.doubleValue = 0; opacityControl.sendAction(opacityControl.action, to: opacityControl.target)
    precondition(sizeChanged == 0 && opacityChanged == 0 && scale.stringValue == "0%")
    opacityControl.doubleValue = 0.7; opacityControl.sendAction(opacityControl.action, to: opacityControl.target)
    let deskControl = controls.first { $0.title == "蜜桃奶盖" }!
    deskControl.sendAction(deskControl.action, to: deskControl.target)
    precondition(abs(opacityChanged - 0.7) < 0.001 && desk == "peach")
    settings.refresh(skinID: "pixel-tricolor", scale: initialPetScale(nil), showLabels: true, clickThrough: false,
                     keys: 0, clicks: 0, peers: [], networkStatus: "", roomCode: nil, roomConnected: false, nickname: "")
    precondition(scale.stringValue == "60%")
    settings.refresh(skinID: "pixel-tricolor", scale: initialPetScale(1.25), showLabels: true, clickThrough: false,
                     keys: 0, clicks: 0, peers: [], networkStatus: "", roomCode: nil, roomConnected: false, nickname: "")
    precondition(scale.stringValue == "100%")
    let skinButtons = descendants(settingsView).compactMap { $0 as? NSButton }.filter {
        let id = $0.identifier?.rawValue ?? ""
        return PetSkin.all.contains { $0.id == id }
    }
    precondition(skinButtons.count == 7 && skinButtons.allSatisfy {
        $0.title == PetSkin.named($0.identifier?.rawValue).name && settingsView.bounds.contains(settingsView.convert($0.bounds, from: $0))
    })
    guard let category = descendants(settingsView).compactMap({ $0 as? NSSegmentedControl }).first(where: { $0.segmentCount == 3 && $0.label(forSegment: 0) == "短毛" }) else { fatalError("Skin categories missing") }
    category.selectedSegment = 1; category.sendAction(category.action, to: category.target)
    precondition(descendants(settingsView).compactMap { $0 as? NSButton }.filter { ($0.identifier?.rawValue ?? "").hasPrefix("pixel-longhair-") }.count == 7)
    category.selectedSegment = 2; category.sendAction(category.action, to: category.target)
    precondition(descendants(settingsView).compactMap { $0 as? NSButton }.filter { ($0.identifier?.rawValue ?? "").hasPrefix("prone-") }.count == 8)
    precondition(tabs.segmentCount == 4 && tabs.label(forSegment: 3) == "关于")
    tabs.selectedSegment = 3
    tabs.sendAction(tabs.action, to: tabs.target)
    let groupCode = descendants(settingsView).compactMap { $0 as? NSImageView }.first { $0.accessibilityLabel() == "QQ 群二维码" }
    precondition(groupCode?.isHiddenOrHasHiddenAncestor == false && groupCode?.image?.isValid == true,
                 "QQ 群页面缺少二维码")
    tabs.selectedSegment = 2
    tabs.sendAction(tabs.action, to: tabs.target)
    precondition(groupCode?.isHiddenOrHasHiddenAncestor == true)
    guard let createPrivate = controls.first(where: { $0.accessibilityLabel() == "创建私密房间" }),
          let createPublic = controls.first(where: { $0.accessibilityLabel() == "创建公开房间" }) else {
        fatalError("Room creation controls missing")
    }
    precondition(!createPrivate.isHiddenOrHasHiddenAncestor && !createPublic.isHiddenOrHasHiddenAncestor)
    precondition(controls.contains { $0.title == "凭码加入" && !$0.isHiddenOrHasHiddenAncestor })
    var created: [Bool] = []
    settings.onCreateRoom = { created.append($0) }
    createPrivate.sendAction(createPrivate.action, to: createPrivate.target)
    createPublic.sendAction(createPublic.action, to: createPublic.target)
    precondition(created == [false, true])
    let firstPeer = LocalPeers.Peer(skin: "pixel-longhair-gold-white", keys: 12_345, clicks: 100_000_000, seen: 10)
    settings.refresh(skinID: "pixel-tricolor", scale: 1, showLabels: true, clickThrough: false,
                     keys: 98_765, clicks: 3,
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
                     keys: 98_766, clicks: 3,
                     peers: [(remoteID, firstPeer)], networkStatus: "连接中", roomCode: invite, roomConnected: false, nickname: "")
    precondition(nameInput.stringValue == "未保存的草稿", "Refresh replaced nickname draft")
    let firstTexts = visibleText()
    precondition(firstTexts.contains { $0.stringValue == "连接中" })
    precondition(firstTexts.contains { $0.stringValue == "按键 1.2万" && $0.toolTip == "按键 12345" })
    precondition(firstTexts.contains { $0.stringValue == "点击 1亿" && $0.toolTip == "点击 100000000" })
    guard let leave = descendants(settingsView).compactMap({ $0 as? NSButton }).first(where: { $0.title == "退出房间" }) else {
        fatalError("Leave room button missing")
    }
    let leaveBounds = settingsView.convert(leave.bounds, from: leave)
    precondition(settingsView.bounds.contains(leaveBounds), "Room controls outside settings view")
    settings.refresh(skinID: "pixel-tricolor", scale: 1, showLabels: true, clickThrough: false,
                     keys: 98_765, clicks: 3,
                     peers: [], networkStatus: "私密房间 · 1/16", roomCode: invite, roomConnected: true, nickname: "")
    let nextTexts = visibleText()
    precondition(nextTexts.contains { $0.stringValue == "私密房间 · 1/16" })
    precondition(!nextTexts.contains { $0.stringValue == "长毛黄白 · bbbb" })
    guard let share = descendants(settingsView).compactMap({ $0 as? NSButton }).first(where: { $0.title == "同步敲击和点击动画" }) else {
        fatalError("Activity sharing switch missing")
    }
    var shared = true
    settings.onShareActivityChange = { shared = $0 }
    share.state = .off
    share.sendAction(share.action, to: share.target)
    precondition(!shared, "Activity sharing switch did not notify owner")
    let workArea = NSRect(x: 0, y: 0, width: 1280, height: 720)
    let corner = NSRect(x: 10, y: 10, width: 310, height: 238.7)
    let friendFrames = FriendDesktop.frames(around: corner, within: workArea, count: 4)
    precondition(friendFrames.count == 4 && friendFrames.allSatisfy { workArea.contains($0) && !$0.intersects(corner) })
    for index in friendFrames.indices {
        precondition(!friendFrames[..<index].contains { $0.intersects(friendFrames[index]) })
    }
    let owner = NSPanel(contentRect: NSRect(x: 200, y: 200, width: 310, height: 238.7), styleMask: [.borderless], backing: .buffered, defer: false)
    owner.orderFrontRegardless()
    let desktop = FriendDesktop()
    var delivered = false
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: true, showLabels: true) { id in
        delivered = id == remoteID
        return .anonymousKey
    }
    guard let friend = desktop.window(for: remoteID) else { fatalError("Friend window missing") }
    precondition(desktop.count == 1 && friend.isVisible && friend.ignoresMouseEvents && delivered)
    precondition(friend.isMovable && friend.isMovableByWindowBackground && friend.contentView?.mouseDownCanMoveWindow == true,
                 "好友窗口未启用背景拖动")
    let initialOrigin = friend.frame.origin
    owner.setFrameOrigin(NSPoint(x: owner.frame.minX, y: owner.frame.minY + 12))
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: false, showLabels: false) { _ in nil }
    precondition(desktop.window(for: remoteID) === friend && !friend.ignoresMouseEvents, "Refresh replaced friend")
    let arrangedOrigin = friend.frame.origin
    precondition(arrangedOrigin != initialOrigin, "未拖动的好友窗口应随本机桌宠重新排布")
    friend.setFrameOrigin(NSPoint(x: arrangedOrigin.x + 12, y: arrangedOrigin.y + 12))
    let movedOrigin = friend.frame.origin
    precondition(movedOrigin != arrangedOrigin)
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: false, showLabels: false) { _ in .key(0) }
    precondition(friend.frame.origin == movedOrigin, "联机刷新重置了好友窗口位置")
    owner.setContentSize(NSSize(width: 290, height: 220))
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: false, showLabels: false) { _ in nil }
    precondition(friend.frame.origin == movedOrigin && friend.frame.size == owner.frame.size,
                 "好友窗口应跟随缩放并保留拖动后的位置")
    owner.orderOut(nil)
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: false, showLabels: true) { _ in nil }
    precondition(!friend.isVisible, "Friend remained visible after owner hid")
    desktop.update(owner: owner, members: [:], clickThrough: false, showLabels: true) { _ in nil }
    precondition(desktop.count == 0, "Leaving did not remove friend")
    desktop.close()
    owner.close()
    print("Verified settings, room state and desktop friend windows")
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

    var idleFrames = Set<Data>()
    let root = Bundle.main.resourceURL!
    for skin in PetSkin.all {
        let files = skin.isProne
            ? ["drawn-\(String(skin.id.dropFirst(6)))-body.png", "drawn-tricolor-feet.png", "drawn-tricolor-eyes.png", "drawn-paw.png"]
            : [skin.body, skin.paw, "keyboard.png", "mouse.png", "mouth-peep.png", "mouth-yawn.png"]
        for file in files {
            precondition(NSImage(contentsOf: root.appendingPathComponent(file)) != nil, "Missing sprite: \(file)")
        }
        view.selectSkin(skin.id)
        let idle = snapshot()
        precondition(idleFrames.insert(idle).inserted, "Identical appearance: \(skin.id)")
        precondition(skin.portrait(size: NSSize(width: 38, height: 40)) != nil, "Missing portrait: \(skin.id)")
        print("Rendered \(skin.id)")
    }
    view.selectSkin("pixel-californian")
    var deskFrames = Set<Data>()
    for desk in ["peach", "mocha", "meadow"] {
        view.deskSkinID = desk
        let frame = snapshot()
        precondition(deskFrames.insert(frame).inserted, "键鼠主题没有改变画面：" + desk)
    }
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
    precondition(PetView.dozePose(idle: 16, delay: 15) != nil)
    precondition(PetView.dozePose(idle: 55, delay: 60) == nil)
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
    let awake = snapshot()
    for _ in 0..<4 { view.localKeyDown(51) }
    precondition(snapshot() != awake, "连续退格应显示慌张表情")
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 1.35))
    for dx in [32.0, -32.0, 32.0, -32.0] { view.localMouseMove(dx: dx, dy: 0) }
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.24))
    precondition(snapshot() != awake, "快速摇动鼠标应触发桌宠动作")
    print("Verified all skins, keyboard/mouse animation, blinking, idle yawn and wake-up")
}
