import AppKit
import ApplicationServices
import ServiceManagement

func initialPetScale(_ savedScale: Double) -> Double {
    savedScale > 0 ? min(2.0, max(0.5, savedScale)) : 0.58
}

private enum LoginAtStartup {
    private static let label = "io.github.gwtt.WheekKeys.login"
    private static var plist: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/LaunchAgents/\(label).plist")
    }
    private static func legacy() throws -> String? {
        guard FileManager.default.fileExists(atPath: plist.path) else { return nil }
        let data = try Data(contentsOf: plist)
        guard let value = try PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any],
              value["Label"] as? String == label,
              let args = value["ProgramArguments"] as? [String], args.count == 3,
              args[0] == "/usr/bin/open", args[1] == "-a" else {
            throw NSError(domain: "LoginAtStartup", code: 1, userInfo: [NSLocalizedDescriptionKey: "登录启动文件格式异常，请检查 ~/Library/LaunchAgents/\(label).plist"])
        }
        return args[2]
    }
    static func state() throws -> (enabled: Bool, pending: Bool, message: String) {
        if let path = try legacy() {
            return (true, false, path == Bundle.main.bundleURL.path ? "" : "应用位置已变，请关闭后重新开启")
        }
        if #available(macOS 13.0, *) {
            switch SMAppService.mainApp.status {
            case .enabled: return (true, false, "")
            case .notRegistered: return (false, false, "")
            case .requiresApproval: return (false, true, "待批准，请在系统设置的登录项中允许")
            default: throw NSError(domain: "LoginAtStartup", code: 2, userInfo: [NSLocalizedDescriptionKey: "无法读取系统登录项状态，请确认应用已安装。"])
            }
        }
        return (false, false, "")
    }
    static func setEnabled(_ enabled: Bool) throws {
        let hasLegacy = try legacy() != nil
        if !enabled && hasLegacy { try FileManager.default.removeItem(at: plist) }
        if #available(macOS 13.0, *) {
            let status = SMAppService.mainApp.status
            if enabled && !hasLegacy && status != .enabled && status != .requiresApproval { try SMAppService.mainApp.register() }
            if !enabled && (status == .enabled || status == .requiresApproval) { try SMAppService.mainApp.unregister() }
        } else if enabled && !hasLegacy {
            let app = Bundle.main.bundleURL
            guard app.pathExtension == "app" else {
                throw NSError(domain: "LoginAtStartup", code: 3, userInfo: [NSLocalizedDescriptionKey: "请从应用包启动后再开启开机自启。"])
            }
            let value: [String: Any] = ["Label": label, "ProgramArguments": ["/usr/bin/open", "-a", app.path], "RunAtLoad": true]
            let data = try PropertyListSerialization.data(fromPropertyList: value, format: .xml, options: 0)
            try FileManager.default.createDirectory(at: plist.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: plist, options: .atomic)
        }
    }
}

private final class PetPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSMenuDelegate {
    private let defaults = UserDefaults.standard
    private var window: PetPanel!
    private var pet: PetView!
    private var statusItem: NSStatusItem!
    private var settingsPopover: NSPopover?
    private var settingsController: PetSettingsViewController?
    private var eventTap: CFMachPort?
    private var eventSource: CFRunLoopSource?
    private var skinID = PetSkin.all[0].id
    private var nickname = ""
    private var scale = 0.58
    private var clickThrough = false
    private var showLabels = true
    private let anonymousInput = AnonymousKeyboardInput()
    private var manuallyHidden = false
    private var inputTimer: Timer?
    private var heldModifiers = Set<UInt16>()
    private var countsDirty = false
    private var opacity = 1.0
    private var deskSkin = "meadow"
    private var packDirectory: URL { FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/WheekKeys/skins", isDirectory: true) }
    private var keyCount: UInt64 = 0
    private var clickCount: UInt64 = 0
    private let updater = PetUpdateChecker()
    private let peers = LocalPeers()
    private let friends = FriendDesktop()
    private let baseSize = NSSize(width: 310, height: 238.7)

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        checkForUpdate()
        WheekPackLoader.loadInstalled(directory: packDirectory)
        opacity = defaults.object(forKey: "opacity") == nil ? 1 : min(1, max(0.2, defaults.double(forKey: "opacity")))
        deskSkin = defaults.string(forKey: "deskSkin") ?? "meadow"
        if !["meadow", "peach", "mocha"].contains(deskSkin) { deskSkin = "meadow" }
        skinID = PetSkin.named(defaults.string(forKey: "skin")).id
        nickname = LocalPeers.normalizedNickname(defaults.string(forKey: "nickname") ?? "") ?? ""
        let savedScale = defaults.double(forKey: "scale")
        scale = initialPetScale(savedScale)
        clickThrough = defaults.bool(forKey: "clickThrough")
        showLabels = defaults.object(forKey: "showKeyLabels") as? Bool ?? true
        keyCount = min(UInt64(Int64.max), UInt64(defaults.string(forKey: "keyCount") ?? "0") ?? 0)
        clickCount = min(UInt64(Int64.max), UInt64(defaults.string(forKey: "clickCount") ?? "0") ?? 0)

        let size = NSSize(width: baseSize.width * scale, height: baseSize.height * scale)
        let visible = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        var origin = NSPoint(x: visible.maxX - size.width - 20, y: visible.minY + 20)
        if defaults.object(forKey: "windowX") != nil, defaults.object(forKey: "windowY") != nil {
            let saved = NSRect(x: defaults.double(forKey: "windowX"), y: defaults.double(forKey: "windowY"), width: size.width, height: size.height)
            if NSScreen.screens.contains(where: { $0.visibleFrame.intersects(saved) }) { origin = saved.origin }
        }
        window = PetPanel(contentRect: NSRect(origin: origin, size: size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.alphaValue = opacity
        window.hidesOnDeactivate = false
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        window.isMovableByWindowBackground = true
        window.ignoresMouseEvents = clickThrough
        window.delegate = self
        pet = PetView(frame: NSRect(origin: .zero, size: size))
        pet.autoresizingMask = [.width, .height]
        pet.selectSkin(skinID)
        pet.showKeyLabels = showLabels
        pet.deskSkinID = deskSkin
        pet.onOpenSettings = { [weak self] in self?.showSettings() }
        pet.onPetHead = { [weak self] in self?.pet.celebrate() }
        pet.onDropPack = { [weak self] in self?.installSkin(from: $0) }
        anonymousInput.onPulse = { [weak self] in
            guard let self else { return }
            self.pet.pressAnonymousKey()
        }
        anonymousInput.refresh()
        window.contentView = pet
        updateVisibility()
        let inputTimer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            self.anonymousInput.refresh()
            self.refreshEventTap()
            self.flushCounts()
            if self.settingsPopover?.isShown == true { self.refreshSettings() }
        }
        self.inputTimer = inputTimer
        RunLoop.main.add(inputTimer, forMode: .common)
        peers.payload = { [weak self] in
            guard let self = self else { return (PetSkin.all[0].id, 0, 0) }
            return (self.skinID, self.keyCount, self.clickCount)
        }

        peers.shareActivity = defaults.object(forKey: "shareActivity") as? Bool ?? true
        peers.onUpdate = { [weak self] in self?.updateFriends() }
        peers.nickname = { [weak self] in self?.nickname ?? "" }

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let url = Bundle.main.url(forResource: "avatar", withExtension: "png"),
           let icon = NSImage(contentsOf: url) {
            icon.size = NSSize(width: 20, height: 20)
            statusItem.button?.image = icon
        } else {
            statusItem.button?.title = "桌宠"
        }
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
        if CGPreflightListenEventAccess() { startEventTap() }

    }

    func applicationWillTerminate(_ notification: Notification) {
        savePosition()
        flushCounts()
        stopEventTap()
        anonymousInput.stop()
        inputTimer?.invalidate()
        peers.stop()
        friends.close()
    }

    func windowDidMove(_ notification: Notification) { savePosition(); updateFriends() }

    private func savePosition() {
        guard let window = window else { return }
        defaults.set(Double(window.frame.origin.x), forKey: "windowX")
        defaults.set(Double(window.frame.origin.y), forKey: "windowY")
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        if eventTap == nil && CGPreflightListenEventAccess() { startEventTap() }
        menu.removeAllItems()
        populate(menu)
    }

    private func add(_ title: String, _ action: Selector, to menu: NSMenu, state: Bool = false, represented: Any? = nil) {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        item.state = state ? .on : .off
        item.representedObject = represented
        menu.addItem(item)
    }

    private func populate(_ menu: NSMenu) {
        add("桌宠设置…", #selector(showSettingsFromMenu(_:)), to: menu)
        menu.addItem(.separator())
        let skins = NSMenu()
        for (index, title) in ["短毛", "长毛", "趴趴猪", "自定义"].enumerated() {
            let members = PetSkin.all.filter { $0.settingsGroup == index }
            guard !members.isEmpty else { continue }
            let group = NSMenuItem(title: title, action: nil, keyEquivalent: "")
            let choices = NSMenu()
            for skin in members {
                add(skin.name, #selector(selectSkin(_:)), to: choices, state: skin.id == skinID, represented: skin.id)
            }
            group.submenu = choices
            skins.addItem(group)
        }
        let skinItem = NSMenuItem(title: "切换外观", action: nil, keyEquivalent: "")
        skinItem.submenu = skins
        menu.addItem(skinItem)
        add("放大", #selector(enlarge), to: menu)
        add("缩小", #selector(shrink), to: menu)
        add("显示按键文字", #selector(toggleLabels), to: menu, state: showLabels)
        add(clickThrough ? "关闭鼠标穿透" : "开启鼠标穿透", #selector(toggleClickThrough), to: menu, state: clickThrough)
        add(manuallyHidden ? "显示桌宠" : "隐藏桌宠", #selector(toggleVisible), to: menu)
        menu.addItem(.separator())
        if CGPreflightListenEventAccess() {
            let item = NSMenuItem(title: eventTap == nil ? "输入监听未启动，请检查权限或重启应用" : "输入监控已授权", action: nil, keyEquivalent: "")
            item.isEnabled = false
            menu.addItem(item)
        } else {
            add("启用输入监控权限…", #selector(requestInputPermission), to: menu)
        }
        add("打开输入监控设置…", #selector(openInputSettings), to: menu)
        menu.addItem(.separator())
        add("退出", #selector(quit), to: menu)
    }

    @objc private func selectSkin(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        applySkin(id)
    }
    private func applySkin(_ id: String) {
        skinID = PetSkin.named(id).id
        defaults.set(skinID, forKey: "skin")
        pet.selectSkin(skinID)
        peers.refreshState()
        refreshSettings()
    }
    @objc private func enlarge() { resize(to: min(2.0, scale + 0.1)) }
    @objc private func shrink() { resize(to: max(0.5, scale - 0.1)) }
    private func resize(to value: Double) {
        let oldFrame = window.frame
        scale = value
        defaults.set(scale, forKey: "scale")
        let size = NSSize(width: baseSize.width * scale, height: baseSize.height * scale)
        window.setFrame(NSRect(x: oldFrame.maxX - size.width, y: oldFrame.minY, width: size.width, height: size.height), display: true)
        savePosition()
        updateFriends()
        refreshSettings()
    }
    @objc private func toggleLabels() {
        showLabels.toggle()
        pet.showKeyLabels = showLabels
        defaults.set(showLabels, forKey: "showKeyLabels")
        updateFriends()
        refreshSettings()
    }
    @objc private func toggleClickThrough() {
        clickThrough.toggle()
        window.ignoresMouseEvents = clickThrough
        defaults.set(clickThrough, forKey: "clickThrough")
        updateFriends()
        refreshSettings()
    }
    @objc private func showSettingsFromMenu(_ sender: NSMenuItem) {
        showSettings(anchor: statusItem.button)
    }
    private func showSettings(anchor: NSView? = nil) {
        if settingsPopover?.isShown == true { return }
        if settingsController == nil {
            let created = PetSettingsViewController()
            created.onDropPack = { [weak self] in self?.installSkin(from: $0) }
            created.onInstallSkin = { [weak self] in self?.installSkin() }
            created.onDeleteSkin = { [weak self] in self?.deleteSkin() }
            created.onOpacityChange = { [weak self] value in
                guard let self else { return }
                self.opacity = value
                self.window.alphaValue = value
                self.defaults.set(value, forKey: "opacity")
                self.updateFriends()
            }
            created.onDeskSkinChange = { [weak self] value in
                guard let self else { return }
                self.deskSkin = value
                self.defaults.set(value, forKey: "deskSkin")
                self.pet.deskSkinID = value
            }
            created.onDownloadUpdate = { [weak self] in self?.downloadUpdate() }
            created.onSelectSkin = { [weak self] in self?.applySkin($0) }
            created.onScaleChange = { [weak self] in self?.resize(to: $0) }
            created.onShowLabelsChange = { [weak self] value in
                guard let self, self.showLabels != value else { return }
                self.toggleLabels()
            }
            created.onClickThroughChange = { [weak self] value in
                guard let self, self.clickThrough != value else { return }
                self.toggleClickThrough()
            }
            created.onLoginAtStartupChange = { [weak self] value in
                do { try LoginAtStartup.setEnabled(value) }
                catch {
                    let alert = NSAlert(error: error)
                    alert.messageText = "无法更改开机自启"
                    alert.informativeText = error.localizedDescription
                    alert.runModal()
                }
                self?.refreshLoginAtStartup()
            }
            created.onShareActivityChange = { [weak self] value in
                guard let self = self else { return }
                self.peers.shareActivity = value
                if !self.peers.roomPublic { self.defaults.set(value, forKey: "shareActivity") }
                self.refreshSettings()
            }
            created.onSaveNickname = { [weak self] value in
                guard let self = self, let normalized = LocalPeers.normalizedNickname(value) else { return false }
                self.nickname = normalized
                self.defaults.set(normalized, forKey: "nickname")
                self.peers.refreshState()
                self.refreshSettings()
                return true
            }
            created.onCreateRoom = { [weak self] isPublic in
                guard let self = self else { return }
                _ = self.peers.startRoom(RoomTransport.createCode(), hosting: true, isPublic: isPublic)
                self.refreshSettings()
            }
            created.onJoinRoom = { [weak self] code in
                guard let self = self else { return }
                if !self.peers.startRoom(code) { self.settingsController?.setNetworkError("房间码无效，请检查后重试") }
                self.refreshSettings()
            }
            created.onJoinPublicRoom = { [weak self] entry in
                guard let self = self else { return }
                _ = self.peers.startRoom(entry.code, isPublic: true, expectedHost: entry.host)
                self.refreshSettings()
            }
            created.onExitRoom = { [weak self] in self?.peers.stop(); self?.refreshSettings() }
            created.onQuit = { [weak self] in self?.quit() }
            created.onClose = { [weak self] in self?.settingsPopover?.performClose(nil) }
            settingsController = created
            let popover = NSPopover()
            popover.behavior = .transient
            popover.animates = true
            popover.contentViewController = created
            popover.contentSize = created.view.frame.size
            settingsPopover = popover
        }
        guard let controller = settingsController else { return }
        checkForUpdate()
        controller.setUpdateAvailable(updater.downloadURL != nil)
        refreshLoginAtStartup()
        refreshSettings()
        let source: NSView = anchor ?? pet
        settingsPopover?.show(relativeTo: source.bounds, of: source, preferredEdge: anchor == nil ? .maxY : .minY)
        if let popoverWindow = settingsPopover?.contentViewController?.view.window {
            popoverWindow.makeFirstResponder(controller.view)
        }
    }
    private func installSkin() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = false; panel.canChooseFiles = true; panel.allowsMultipleSelection = false
        panel.allowedFileTypes = ["wheek"]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        installSkin(from: url)
    }
    private func installSkin(from url: URL) {
        do { let skin = try WheekPackLoader.install(url: url, directory: packDirectory); settingsController?.reloadSkins(); applySkin(skin.id) }
        catch { NSAlert(error: error).runModal() }
    }
    private func deleteSkin() {
        guard PetSkin.find(skinID)?.isCustom == true else { NSAlert(error: WheekPackError.invalid("请选择已安装的自定义外观。")).runModal(); return }
        let alert = NSAlert()
        alert.messageText = "删除当前自定义外观？"
        alert.informativeText = "删除后可重新安装外观包。"
        alert.addButton(withTitle: "删除"); alert.addButton(withTitle: "取消")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        do {
            guard try WheekPackLoader.uninstall(id: skinID, directory: packDirectory) else { throw WheekPackError.invalid("请选择已安装的自定义外观。") }
            settingsController?.reloadSkins(); applySkin(PetSkin.all[0].id)
        }
        catch { NSAlert(error: error).runModal() }
    }
    private func checkForUpdate() {
        updater.check { [weak self] in
            guard let self = self else { return }
            self.settingsController?.setUpdateAvailable(self.updater.downloadURL != nil)
        }
    }
    private func downloadUpdate() {
        guard let url = updater.downloadURL else { return }
        NSWorkspace.shared.open(url)
    }
    private func refreshLoginAtStartup() {
        do {
            let state = try LoginAtStartup.state()
            settingsController?.setLoginAtStartup(enabled: state.enabled, pending: state.pending, message: state.message)
        } catch {
            settingsController?.setLoginAtStartup(enabled: false, pending: false, message: "无法读取开机自启状态")
        }
    }
    private func refreshSettings() {
        let rows = peers.peers.sorted { $0.key < $1.key }.map { ($0.key, $0.value) }
        settingsController?.refresh(skinID: skinID, scale: scale, showLabels: showLabels, clickThrough: clickThrough,
                                    keys: keyCount, clicks: clickCount, peers: rows, networkStatus: peers.status,
                                    roomCode: peers.roomCode, roomConnected: peers.roomConnected, nickname: nickname,
                                    shareActivity: peers.shareActivity, shareActivityAvailable: peers.shareActivityAvailable,
                                    roomPublic: peers.roomPublic)
        settingsController?.refreshOptions(opacity: opacity, desk: deskSkin)
    }
    private func updateFriends() {
        guard let window = window else { return }
        friends.update(owner: window, members: peers.peers, clickThrough: clickThrough, showLabels: showLabels) {
            peers.takeAction(for: $0, visible: window.isVisible)
        }
    }
    @objc private func toggleVisible() {
        manuallyHidden.toggle()
        updateVisibility()
    }
    private func updateVisibility() {
        let shouldShow = !manuallyHidden
        guard shouldShow != window.isVisible else { return }
        if shouldShow { window.orderFrontRegardless() } else { window.orderOut(nil) }
        updateFriends()
    }
    @objc private func requestInputPermission() {
        if CGRequestListenEventAccess() { refreshEventTap(); anonymousInput.refresh() } else { openInputSettings() }
    }
    @objc private func openInputSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent") {
            NSWorkspace.shared.open(url)
        }
    }
    @objc private func quit() { NSApp.terminate(nil) }

    private func refreshEventTap() {
        guard CGPreflightListenEventAccess() else { stopEventTap(); heldModifiers.removeAll(); return }
        if let tap = eventTap {
            if !CFMachPortIsValid(tap) { stopEventTap(); heldModifiers.removeAll() }
            else if !CGEvent.tapIsEnabled(tap: tap) {
                heldModifiers.removeAll()
                CGEvent.tapEnable(tap: tap, enable: true)
                if !CGEvent.tapIsEnabled(tap: tap) { stopEventTap() }
            }
        }
        if eventTap == nil { startEventTap() }
    }
    private func startEventTap() {
        guard eventTap == nil, CGPreflightListenEventAccess() else { return }
        let types: [CGEventType] = [.keyDown, .flagsChanged, .mouseMoved, .leftMouseDragged, .rightMouseDragged,
                                   .otherMouseDragged, .leftMouseDown, .rightMouseDown, .scrollWheel]
        let mask = types.reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }
        let callback: CGEventTapCallBack = { _, type, event, info in
            guard let info = info else { return Unmanaged.passUnretained(event) }
            let delegate = Unmanaged<AppDelegate>.fromOpaque(info).takeUnretainedValue()
            guard CGPreflightListenEventAccess() else { delegate.anonymousInput.stop(); return Unmanaged.passUnretained(event) }
            if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
                delegate.heldModifiers.removeAll()
                if let tap = delegate.eventTap, CFMachPortIsValid(tap) { CGEvent.tapEnable(tap: tap, enable: true) }
            } else {
                delegate.handleEvent(type, event)
            }
            return Unmanaged.passUnretained(event)
        }
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .listenOnly, eventsOfInterest: mask, callback: callback, userInfo: Unmanaged.passUnretained(self).toOpaque()) else { return }
        guard let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0) else { CFMachPortInvalidate(tap); return }
        eventTap = tap
        eventSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }
    private func stopEventTap() {
        if let source = eventSource { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap = eventTap { CFMachPortInvalidate(tap) }
        eventSource = nil
        eventTap = nil
    }
    private func flushCounts() {
        guard countsDirty else { return }
        defaults.set(String(keyCount), forKey: "keyCount")
        defaults.set(String(clickCount), forKey: "clickCount")
        countsDirty = false
    }
    private func incrementKeyCount() {
        keyCount = min(UInt64(Int64.max), keyCount + 1)
        countsDirty = true
    }
    private func handleEvent(_ type: CGEventType, _ event: CGEvent) {
        if (type == .keyDown || type == .flagsChanged) && !AnonymousKeyboardInput.permitted() {
            heldModifiers.removeAll()
            anonymousInput.stop()
            return
        }
        switch type {
        case .keyDown:
            let code = UInt16(truncatingIfNeeded: event.getIntegerValueField(.keyboardEventKeycode))
            if event.getIntegerValueField(.keyboardEventAutorepeat) == 0 {
                anonymousInput.notePrecise()
                incrementKeyCount()
                pet.localKeyDown(code)
                pet.pressKey(code)
                peers.recordTyping()
            } else { pet.pressKey(code) }
        case .flagsChanged:
            pet.noteInput(ProcessInfo.processInfo.systemUptime)
            let code = UInt16(truncatingIfNeeded: event.getIntegerValueField(.keyboardEventKeycode))
            if code == 57 {
                anonymousInput.notePrecise()
                incrementKeyCount()
                pet.localKeyDown(code)
                pet.pressKey(code)
                peers.recordTyping()
            } else if [54, 55, 56, 58, 59, 60, 61, 62, 63].contains(code) {
                if CGEventSource.keyState(.combinedSessionState, key: CGKeyCode(code)) {
                    if heldModifiers.insert(code).inserted {
                        anonymousInput.notePrecise()
                        incrementKeyCount()
                        pet.localKeyDown(code)
                        pet.pressKey(code)
                        peers.recordTyping()
                    }
                } else { heldModifiers.remove(code) }
            }
        case .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged:
            let dx = Double(event.getIntegerValueField(.mouseEventDeltaX))
            let dy = Double(event.getIntegerValueField(.mouseEventDeltaY))
            pet.localMouseMove(dx: dx, dy: dy)
        case .leftMouseDown, .rightMouseDown:
            clickCount = min(UInt64(Int64.max), clickCount + 1)
            countsDirty = true
            pet.clickMouse(right: type == .rightMouseDown)
            peers.recordClick(right: type == .rightMouseDown)
        case .scrollWheel:
            let points = event.getIntegerValueField(.scrollWheelEventPointDeltaAxis1)
            pet.scrollMouse(points == 0 ? event.getIntegerValueField(.scrollWheelEventDeltaAxis1) : points)
        default: break
        }
    }
}

@main
struct WheekKeysApp {
    @MainActor static func main() {
        if CommandLine.arguments.contains("--smoke-test") {
            runSmokeTest()
            return
        }
        let application = NSApplication.shared
        let delegate = AppDelegate()
        application.delegate = delegate
        withExtendedLifetime(delegate) { application.run() }
    }
}
