import AppKit

final class PetUpdateChecker {
    private struct Release: Decodable {
        struct Asset: Decodable { let name: String; let browser_download_url: String }
        let tag_name: String
        let draft: Bool
        let prerelease: Bool
        let assets: [Asset]
    }
    private enum InvalidRelease: Error { case version }
    private let endpoint = URL(string: "https://api.github.com/repos/gwtt/guinea-pig-keyboard/releases/latest")!
    private static let releasePage = URL(string: "https://github.com/gwtt/guinea-pig-keyboard/releases/latest")!
    private var checking = false
    private var lastSuccess: TimeInterval?
    private(set) var downloadURL: URL?

    private static func version(_ value: String) -> [Int]? {
        let value = value.hasPrefix("v") ? String(value.dropFirst()) : value
        let parts = value.split(separator: ".", omittingEmptySubsequences: false)
        guard (2...3).contains(parts.count), parts.allSatisfy({ !$0.isEmpty && $0.allSatisfy { $0.isASCII && $0.isNumber } }),
              parts.allSatisfy({ Int($0) != nil }) else { return nil }
        return parts.map { Int($0)! } + Array(repeating: 0, count: 3 - parts.count)
    }

    static func updateURL(data: Data, currentVersion: String) throws -> URL? {
        let release = try JSONDecoder().decode(Release.self, from: data)
        guard let current = version(currentVersion), let latest = version(release.tag_name) else { throw InvalidRelease.version }
        guard !release.draft, !release.prerelease, current.lexicographicallyPrecedes(latest), let asset = release.assets.first(where: { $0.name == "WheekKeys-macOS.zip" }),
              let url = URL(string: asset.browser_download_url), url.scheme == "https", url.host == "github.com",
              url.user == nil, url.password == nil, url.port == nil, url.query == nil, url.fragment == nil,
              url.path == "/gwtt/guinea-pig-keyboard/releases/download/\(release.tag_name)/WheekKeys-macOS.zip" else { return nil }
        return url
    }

    static func updateURL(releasePage page: URL, currentVersion: String) throws -> URL? {
        let tag = page.lastPathComponent
        guard page.scheme == "https", page.host == releasePage.host, page.user == nil, page.password == nil,
              page.port == nil, page.query == nil, page.fragment == nil, tag.hasPrefix("v"),
              page.path == "/gwtt/guinea-pig-keyboard/releases/tag/\(tag)",
              let latest = version(tag), let current = version(currentVersion) else { throw InvalidRelease.version }
        guard current.lexicographicallyPrecedes(latest) else { return nil }
        return URL(string: "https://github.com/gwtt/guinea-pig-keyboard/releases/download/\(tag)/WheekKeys-macOS.zip")
    }

    func check(completion: @escaping () -> Void) {
        if checking || lastSuccess.map({ ProcessInfo.processInfo.systemUptime - $0 < 300 }) == true { return }
        checking = true
        var request = URLRequest(url: endpoint, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 10)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("WheekKeys-macOS", forHTTPHeaderField: "User-Agent")
        let current = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
        URLSession.shared.dataTask(with: request) { [weak self] data, response, error in
            guard let self = self else { return }
            if let status = (response as? HTTPURLResponse)?.statusCode, status == 403 || status == 429 {
                self.checkReleasePage(current: current, completion: completion)
                return
            }
            let result: Result<URL?, Error>
            if error == nil, (response as? HTTPURLResponse)?.statusCode == 200, let data = data {
                result = Result { try Self.updateURL(data: data, currentVersion: current) }
            } else {
                result = .failure(error ?? URLError(.badServerResponse))
            }
            self.finish(result, completion: completion)
        }.resume()
    }

    private func checkReleasePage(current: String, completion: @escaping () -> Void) {
        var request = URLRequest(url: Self.releasePage, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 10)
        request.httpMethod = "HEAD"
        URLSession.shared.dataTask(with: request) { [weak self] _, response, error in
            guard let self = self else { return }
            guard let response = response as? HTTPURLResponse, response.statusCode == 200, let page = response.url else {
                self.finish(.failure(error ?? URLError(.badServerResponse)), completion: completion)
                return
            }
            self.finish(Result { try Self.updateURL(releasePage: page, currentVersion: current) }, completion: completion)
        }.resume()
    }

    private func finish(_ result: Result<URL?, Error>, completion: @escaping () -> Void) {
        DispatchQueue.main.async {
            self.checking = false
            if case .success(let url) = result {
                self.downloadURL = url
                self.lastSuccess = ProcessInfo.processInfo.systemUptime
            }
            completion()
        }
    }
}

final class PetSettingsViewController: NSViewController {
    var onDropPack: ((URL) -> Void)?
    var onFeedGrass: (() -> Void)?
    var onSelectSkin: ((String) -> Void)?
    var onInstallSkin: (() -> Void)?
    var onDeleteSkin: (() -> Void)?
    var onOpacityChange: ((Double) -> Void)?
    var onFullscreenChange: ((Bool) -> Void)?
    var onSleepChange: ((Int) -> Void)?
    var onDeskSkinChange: ((String) -> Void)?
    var onSaveNickname: ((String) -> Bool)?
    var onScaleChange: ((Double) -> Void)?
    var onKeyboardGlowChange: ((Bool) -> Void)?
    var onShowLabelsChange: ((Bool) -> Void)?
    var onClickThroughChange: ((Bool) -> Void)?
    var onLoginAtStartupChange: ((Bool) -> Void)?
    var onShareActivityChange: ((Bool) -> Void)?
    var onCreateRoom: ((Bool) -> Void)?
    var onJoinRoom: ((String) -> Void)?
    var onJoinPublicRoom: ((RoomDirectory.Entry) -> Void)?
    var onExitRoom: (() -> Void)?
    var onQuit: (() -> Void)?
    var onClose: (() -> Void)?
    var onDownloadUpdate: (() -> Void)?

    private var skinButtons: [String: NSButton] = [:]
    private var activeSkinID: String?
    private lazy var removeSkinButton = controlButton("删除当前外观", action: #selector(deleteSkin))
    private let skinCategory = NSSegmentedControl(labels: ["短毛", "长毛", "趴趴猪", "自定义"], trackingMode: .selectOne, target: nil, action: nil)
    private let skinContainer = FlippedView()
    private let opacitySlider = NSSlider(value: 1, minValue: 0.2, maxValue: 1, target: nil, action: nil)
    private let sizeSlider = NSSlider(value: 0.58, minValue: 0.5, maxValue: 2, target: nil, action: nil)
    private let fullscreenSwitch = NSButton(checkboxWithTitle: "全屏时自动隐藏", target: nil, action: nil)
    private let sleepChoice = NSPopUpButton()
    private let deskChoice = NSPopUpButton()
    private let statsLabel = NSTextField(labelWithString: "")
    private let updateButton = NSButton(title: "更新到最新版本", target: nil, action: nil)
    private let scaleValue = NSTextField(labelWithString: "58%")
    private let glowSwitch = NSButton(checkboxWithTitle: "连击键帽流光", target: nil, action: nil)
    private let labelsSwitch = NSButton(checkboxWithTitle: "显示按键气泡", target: nil, action: nil)
    private let clickThroughSwitch = NSButton(checkboxWithTitle: "鼠标穿透", target: nil, action: nil)
    private let loginSwitch = NSButton(checkboxWithTitle: "开机自启", target: nil, action: nil)
    private let loginMessage = NSTextField(labelWithString: "")
    private var loginRegistered = false

    private let shareSwitch = NSButton(checkboxWithTitle: "同步敲击和点击动画", target: nil, action: nil)
    private let privacyHint = NSTextField(wrappingLabelWithString: "具体键位只在本机处理。进入房间后会分享昵称、外观和累计次数。开启同步后，好友还会看到通用敲击和鼠标点击动画。")
    private let tabs = NSSegmentedControl(labels: ["桌宠", "联机", "QQ 群"], trackingMode: .selectOne, target: nil, action: nil)
    private let petScroll = NSScrollView()
    private let petPage = FlippedView()
    private let roomPage = NSView()
    private let groupPage = NSView()
    private let lobby = NSView()
    private var inRoom = false
    private let partnersHeading = NSTextField(labelWithString: "联机伙伴")
    private let joinCode = NSTextField()
    private let joinRoom = NSButton(title: "凭码加入", target: nil, action: nil)
    private let roomCodeLabel = NSTextField(labelWithString: "")
    private let copyRoom = NSButton(title: "复制", target: nil, action: nil)
    private let exitRoom = NSButton(title: "退出房间", target: nil, action: nil)
    private let roomHint = NSTextField(labelWithString: "")
    private let publicScroll = NSScrollView()
    private let publicDocument = FlippedView()
    private let directoryStatus = NSTextField(labelWithString: "正在连接…")
    private var shareTop: NSLayoutConstraint?
    private var privacyTop: NSLayoutConstraint?
    private var directory: RoomDirectory?
    private var publicEntries: [RoomDirectory.Entry] = []
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
        root.dropAction = { [weak self] in self?.onDropPack?($0) }
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
        add(title, x: 70, top: 12, width: 90, height: 24, to: root)

        let version = NSTextField(labelWithString: "v" + (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "未知"))
        version.font = .systemFont(ofSize: 11)
        version.textColor = mutedBrown
        add(version, x: 70, top: 37, width: 180, height: 18, to: root)
        updateButton.target = self
        updateButton.action = #selector(downloadUpdate)
        updateButton.isBordered = false
        updateButton.font = .systemFont(ofSize: 11)
        updateButton.contentTintColor = darkBrown
        updateButton.toolTip = "在浏览器下载 macOS 版本，需手动安装"
        updateButton.isHidden = true
        add(updateButton, x: 166, top: 12, width: 106, height: 24, to: root)

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
        petPage.frame = NSRect(x: 0, y: 0, width: 316, height: 560)
        petScroll.documentView = petPage
        add(petScroll, x: 0, top: 100, width: 316, height: 352, to: root)
        add(roomPage, x: 0, top: 100, width: 316, height: 352, to: root)
        roomPage.isHidden = true
        add(groupPage, x: 18, top: 100, width: 280, height: 352, to: root)
        groupPage.isHidden = true

        let groupHint = NSTextField(labelWithString: "用手机 QQ 扫码加群")
        groupHint.font = .systemFont(ofSize: 12)
        groupHint.textColor = mutedBrown
        groupHint.alignment = .center
        add(groupHint, x: 0, top: 43, width: 280, height: 18, to: groupPage)
        let groupCode = NSImageView()
        if let url = Bundle.main.url(forResource: "qq-group", withExtension: "png") {
            groupCode.image = NSImage(contentsOf: url)
        }
        groupCode.imageScaling = .scaleProportionallyUpOrDown
        groupCode.setAccessibilityLabel("QQ 群二维码")
        add(groupCode, x: 28, top: 71, width: 224, height: 224, to: groupPage)
        let support = NSButton(title: "爱发电支持我们", target: self, action: #selector(openSupport))
        add(support, x: 75, top: 304, width: 130, height: 26, to: groupPage)

        let coatLabel = sectionLabel("外观")
        add(coatLabel, x: 18, top: 60, width: 200, height: 18, to: petPage)
        skinCategory.target = self
        skinCategory.action = #selector(changeSkinCategory)
        skinCategory.selectedSegment = 0
        add(skinCategory, x: 18, top: 82, width: 270, height: 26, to: petPage)
        let skinScroll = NSScrollView()
        skinScroll.drawsBackground = false; skinScroll.hasVerticalScroller = true; skinScroll.autohidesScrollers = true
        skinContainer.frame = NSRect(x: 0, y: 0, width: 270, height: 112)
        skinScroll.documentView = skinContainer
        add(skinScroll, x: 18, top: 112, width: 270, height: 94, to: petPage)
        let install = controlButton("导入外观 (.wheek)…", action: #selector(installSkin))
        install.font = .systemFont(ofSize: 12)
        removeSkinButton.font = .systemFont(ofSize: 12)
        add(install, x: 18, top: 209, width: 137, height: 26, to: petPage)
        add(removeSkinButton, x: 162, top: 209, width: 117, height: 26, to: petPage)
        rebuildSkinButtons()

        add(sectionLabel("大小"), x: 18, top: 248, width: 40, height: 18, to: petPage)
        sizeSlider.target = self; sizeSlider.action = #selector(changeSize(_:))
        add(sizeSlider, x: 65, top: 246, width: 165, height: 22, to: petPage)
        scaleValue.font = .systemFont(ofSize: 12, weight: .semibold)
        scaleValue.textColor = mutedBrown
        add(scaleValue, x: 235, top: 248, width: 55, height: 18, to: petPage)
        add(sectionLabel("透明度"), x: 18, top: 280, width: 50, height: 18, to: petPage)
        opacitySlider.target = self; opacitySlider.action = #selector(changeOpacity(_:))
        add(opacitySlider, x: 75, top: 278, width: 155, height: 22, to: petPage)
        fullscreenSwitch.target = self; fullscreenSwitch.action = #selector(toggleFullscreen(_:))
        add(fullscreenSwitch, x: 18, top: 310, width: 200, height: 22, to: petPage)
        add(sectionLabel("打瞌睡"), x: 18, top: 342, width: 55, height: 22, to: petPage)
        sleepChoice.addItems(withTitles: ["15 秒", "30 秒", "60 秒", "关闭"])
        sleepChoice.target = self; sleepChoice.action = #selector(changeSleep(_:))
        add(sleepChoice, x: 78, top: 338, width: 110, height: 26, to: petPage)
        add(sectionLabel("键鼠"), x: 18, top: 374, width: 55, height: 22, to: petPage)
        deskChoice.addItems(withTitles: ["牧草奶酪", "蜜桃奶盖", "可可黑金"])
        deskChoice.target = self; deskChoice.action = #selector(changeDesk(_:))
        add(deskChoice, x: 78, top: 370, width: 115, height: 26, to: petPage)
        statsLabel.font = .systemFont(ofSize: 11)
        statsLabel.textColor = mutedBrown
        statsLabel.maximumNumberOfLines = 2
        add(statsLabel, x: 18, top: 10, width: 209, height: 38, to: petPage)
        let feed = NSButton(title: "喂草", target: self, action: #selector(feedGrass))
        add(feed, x: 230, top: 14, width: 58, height: 26, to: petPage)

        labelsSwitch.target = self
        labelsSwitch.action = #selector(toggleLabels(_:))
        labelsSwitch.font = .systemFont(ofSize: 12)
        labelsSwitch.contentTintColor = darkBrown
        add(labelsSwitch, x: 18, top: 420, width: 270, height: 22, to: petPage)
        clickThroughSwitch.target = self
        clickThroughSwitch.action = #selector(toggleClickThrough(_:))
        clickThroughSwitch.font = .systemFont(ofSize: 12)
        clickThroughSwitch.contentTintColor = darkBrown
        add(clickThroughSwitch, x: 18, top: 451, width: 270, height: 22, to: petPage)

        glowSwitch.target = self
        glowSwitch.action = #selector(toggleGlow(_:))
        glowSwitch.font = .systemFont(ofSize: 12)
        glowSwitch.contentTintColor = darkBrown
        add(glowSwitch, x: 18, top: 482, width: 270, height: 22, to: petPage)
        loginSwitch.allowsMixedState = true
        loginSwitch.target = self
        loginSwitch.action = #selector(toggleLoginAtStartup(_:))
        loginSwitch.font = .systemFont(ofSize: 12)
        loginSwitch.contentTintColor = darkBrown
        add(loginSwitch, x: 18, top: 513, width: 270, height: 22, to: petPage)
        loginMessage.font = .systemFont(ofSize: 11)
        loginMessage.textColor = mutedBrown
        add(loginMessage, x: 18, top: 537, width: 280, height: 18, to: petPage)

        add(lobby, x: 18, top: 6, width: 280, height: 194, to: roomPage)
        let lobbyTitle = sectionLabel("公开房间")
        lobbyTitle.font = .systemFont(ofSize: 15, weight: .bold)
        add(lobbyTitle, x: 0, top: 0, width: 280, height: 22, to: lobby)
        let listBackground = NSView()
        listBackground.wantsLayer = true
        listBackground.layer?.backgroundColor = NSColor(calibratedRed: 0.95, green: 0.93, blue: 0.88, alpha: 1).cgColor
        listBackground.layer?.cornerRadius = 8
        add(listBackground, x: 0, top: 26, width: 280, height: 68, to: lobby)
        publicScroll.hasVerticalScroller = true
        publicScroll.autohidesScrollers = true
        publicScroll.drawsBackground = false
        publicScroll.documentView = publicDocument
        add(publicScroll, x: 4, top: 2, width: 272, height: 64, to: listBackground)
        directoryStatus.font = .systemFont(ofSize: 11)
        directoryStatus.textColor = mutedBrown
        directoryStatus.alignment = .center
        add(directoryStatus, x: 5, top: 24, width: 270, height: 18, to: listBackground)
        joinCode.placeholderString = "粘贴朋友的房间码"
        joinCode.target = self
        joinCode.action = #selector(joinRoomAction)
        add(joinCode, x: 0, top: 100, width: 197, height: 26, to: lobby)
        joinRoom.target = self
        joinRoom.action = #selector(joinRoomAction)
        add(joinRoom, x: 204, top: 100, width: 76, height: 26, to: lobby)
        let privateRoom = roomChoice("创建私密房间", hint: "仅凭房间码加入", publicRoom: false)
        let publicRoom = roomChoice("创建公开房间", hint: "所有人都能发现", publicRoom: true)
        add(privateRoom, x: 0, top: 147, width: 136, height: 45, to: lobby)
        add(publicRoom, x: 144, top: 147, width: 136, height: 45, to: lobby)
        roomCodeLabel.font = .monospacedSystemFont(ofSize: 11, weight: .medium)
        add(roomCodeLabel, x: 18, top: 12, width: 163, height: 23, to: roomPage)
        copyRoom.target = self
        copyRoom.action = #selector(copyRoomAction)
        add(copyRoom, x: 180, top: 10, width: 50, height: 26, to: roomPage)
        exitRoom.target = self
        exitRoom.action = #selector(exitRoomAction)
        add(exitRoom, x: 232, top: 10, width: 66, height: 26, to: roomPage)
        roomHint.font = .systemFont(ofSize: 10)
        roomHint.textColor = mutedBrown
        add(roomHint, x: 18, top: 43, width: 280, height: 18, to: roomPage)
        networkState.font = .systemFont(ofSize: 11, weight: .semibold)
        networkState.alignment = .left
        add(networkState, x: 18, top: 65, width: 280, height: 22, to: roomPage)
        shareSwitch.target = self
        shareSwitch.action = #selector(toggleShareActivity(_:))
        shareSwitch.font = .systemFont(ofSize: 12)
        add(shareSwitch, x: 18, top: 204, width: 280, height: 22, to: roomPage)
        shareTop = roomPage.constraints.first { ($0.firstItem as? NSView) === shareSwitch && $0.firstAttribute == .top }
        privacyHint.font = .systemFont(ofSize: 9.5)
        privacyHint.textColor = mutedBrown
        add(privacyHint, x: 18, top: 226, width: 280, height: 38, to: roomPage)
        privacyTop = roomPage.constraints.first { ($0.firstItem as? NSView) === privacyHint && $0.firstAttribute == .top }
        networkMessage.font = .systemFont(ofSize: 11)
        networkMessage.textColor = NSColor(calibratedRed: 0.65, green: 0.30, blue: 0.22, alpha: 1)
        networkMessage.lineBreakMode = .byTruncatingTail
        add(networkMessage, x: 18, top: 133, width: 280, height: 18, to: roomPage)
        let mine = sectionLabel("本机")
        add(mine, x: 18, top: 133, width: 200, height: 18, to: roomPage)
        add(localRow, x: 18, top: 153, width: 280, height: 56, to: roomPage)
        localRow.layer?.backgroundColor = sage.cgColor
        localRow.canRename = true
        localRow.onSaveNickname = { [weak self] value in self?.onSaveNickname?(value) ?? false }
        partnersHeading.font = .systemFont(ofSize: 12, weight: .semibold)
        partnersHeading.textColor = darkBrown
        add(partnersHeading, x: 18, top: 220, width: 200, height: 18, to: roomPage)
        peerScroll.hasVerticalScroller = true
        peerScroll.drawsBackground = false
        peerScroll.borderType = .noBorder
        peerScroll.documentView = peerDocument
        add(peerScroll, x: 18, top: 241, width: 280, height: 90, to: roomPage)
        emptyMessage.font = .systemFont(ofSize: 12)
        emptyMessage.textColor = mutedBrown
        add(emptyMessage, x: 26, top: 246, width: 260, height: 48, to: roomPage)
        let compactViews: [NSView] = [mine, localRow, partnersHeading, peerScroll, emptyMessage]
        compactConstraints = roomPage.constraints.compactMap { constraint in
            guard constraint.firstAttribute == .top, let child = constraint.firstItem as? NSView,
                  compactViews.contains(where: { $0 === child }) else { return nil }
            return (constraint, constraint.constant)
        }

        let divider = NSBox()
        divider.boxType = .separator
        add(divider, x: 18, top: 452, width: 280, height: 1, to: root)

        let feedback = footerButton("反馈问题", action: #selector(openFeedback))
        add(feedback, x: 18, top: 465, width: 136, height: 32, to: root)
        let quit = footerButton("退出", action: #selector(quitApp))
        quit.contentTintColor = NSColor(calibratedRed: 155 / 255, green: 79 / 255, blue: 63 / 255, alpha: 1)
        add(quit, trailing: 18, top: 465, width: 136, height: 32, to: root)
    }

    func setUpdateAvailable(_ available: Bool) {
        updateButton.isHidden = !available
    }

    @objc private func downloadUpdate() { onDownloadUpdate?() }

    func refresh(skinID: String, scale: Double, showLabels: Bool, clickThrough: Bool,
                 keys: UInt64, clicks: UInt64,
                 peers: [(String, LocalPeers.Peer)], networkStatus: String,
                 roomCode: String?, roomConnected: Bool, nickname: String, shareActivity: Bool = true,
                 shareActivityAvailable: Bool = true, roomPublic: Bool = false) {
        shareSwitch.state = shareActivity ? .on : .off
        shareSwitch.isEnabled = shareActivityAvailable
        inRoom = roomCode != nil
        lobby.isHidden = inRoom
        partnersHeading.isHidden = !inRoom
        peerScroll.isHidden = !inRoom
        networkState.isHidden = !inRoom
        shareTop?.constant = inRoom ? 88 : 204
        privacyTop?.constant = inRoom ? 110 : 226
        refreshDirectory()
        roomCodeLabel.isHidden = !roomConnected
        copyRoom.isHidden = !roomConnected
        exitRoom.isHidden = !inRoom
        roomHint.isHidden = !roomConnected
        roomHint.stringValue = roomPublic ? "其他人可以从公开房间列表加入。" : "私密房间通过房间码邀请，每间最多 16 人。"
        if let code = roomCode {
            let chars = Array(code)
            if chars.count == 20 {
                roomCodeLabel.stringValue = stride(from: 0, to: 20, by: 5).map { String(chars[$0..<($0 + 5)]) }.joined(separator: " ")
            }
        }
        let error = inRoom ? "" : networkError
        networkMessage.stringValue = error
        networkMessage.toolTip = error
        networkMessage.isHidden = error.isEmpty
        for (constraint, original) in compactConstraints {
            let top = inRoom ? original + 18 : original + 134
            if constraint.constant != top { constraint.constant = top }
        }
        if inRoom {
            networkState.stringValue = networkStatus.isEmpty ? "连接中" : networkStatus
            networkState.textColor = roomConnected ? NSColor(calibratedRed: 0.34, green: 0.48, blue: 0.29, alpha: 1) : mutedBrown
        }
        let skinName = PetSkin.named(skinID).name
        let localName = nickname.isEmpty ? skinName + " · 我" : nickname + " · " + skinName + " · 我"
        localRow.savedNickname = nickname
        localRow.update(name: localName, image: portrait(skinID), keys: keys, clicks: clicks)
        emptyMessage.isHidden = !roomConnected || !peers.isEmpty
        emptyMessage.stringValue = "伙伴加入后会出现在这里。"
        updatePeers(peers)
        scaleValue.stringValue = "\(Int((scale * 100).rounded()))%"
        sizeSlider.doubleValue = scale
        labelsSwitch.state = showLabels ? .on : .off
        clickThroughSwitch.state = clickThrough ? .on : .off
        if activeSkinID != skinID {
            activeSkinID = skinID
            skinCategory.selectedSegment = PetSkin.named(skinID).settingsGroup
            rebuildSkinButtons()
        }
        refreshSkinSelection()
    }

    private func refreshSkinSelection() {
        removeSkinButton.isHidden = skinCategory.selectedSegment != 3
        removeSkinButton.isEnabled = activeSkinID.flatMap(PetSkin.find)?.isCustom == true
        for skin in PetSkin.all {
            guard let button = skinButtons[skin.id] else { continue }
            let selected = skin.id == activeSkinID
            button.title = skin.name
            button.toolTip = selected ? skin.name + "（当前）" : skin.name
            button.layer?.borderWidth = selected ? 1.5 : 0.5
            button.layer?.borderColor = mutedBrown.withAlphaComponent(selected ? 0.8 : 0.3).cgColor
            button.layer?.backgroundColor = (selected ? sage : paper).cgColor
            button.contentTintColor = darkBrown
        }
    }

    func refreshOptions(opacity: Double, fullscreen: Bool, sleep: Int, desk: String, todayKeys: UInt64, todayClicks: UInt64, totalKeys: UInt64, totalClicks: UInt64, keyboardGlow: Bool = false) {
        glowSwitch.state = keyboardGlow ? .on : .off
        opacitySlider.doubleValue = opacity
        fullscreenSwitch.state = fullscreen ? .on : .off
        sleepChoice.selectItem(at: [15, 30, 60, 0].firstIndex(of: sleep) ?? 1)
        deskChoice.selectItem(at: ["meadow", "peach", "mocha"].firstIndex(of: desk) ?? 0)
        statsLabel.stringValue = "今日按键：\(todayKeys)  点击：\(todayClicks)\n累计按键：\(totalKeys)  点击：\(totalClicks)"
    }

    private func rebuildSkinButtons() {
        skinContainer.subviews.forEach { $0.removeFromSuperview() }
        skinButtons.removeAll()
        let category = skinCategory.selectedSegment
        let skins = PetSkin.all.filter { $0.settingsGroup == category }
        skinContainer.frame.size.height = max(94, CGFloat((skins.count + 2) / 3) * 30)
        if skins.isEmpty {
            add(NSTextField(labelWithString: "还没有自定义外观"), x: 4, top: 8, width: 200, height: 18, to: skinContainer)
        }
        for (index, skin) in skins.enumerated() {
            let button = NSButton(title: skin.name, target: self, action: #selector(selectSkin(_:)))
            button.identifier = NSUserInterfaceItemIdentifier(skin.id)
            button.font = .systemFont(ofSize: 11)
            button.isBordered = false
            button.wantsLayer = true
            button.layer?.cornerRadius = 7
            button.image = skin.portrait(size: NSSize(width: 16, height: 17))
            button.imagePosition = .imageLeading
            add(button, x: CGFloat(index % 3) * 90, top: CGFloat(index / 3) * 30, width: 88, height: 28, to: skinContainer)
            skinButtons[skin.id] = button
        }
        refreshSkinSelection()
    }
    func reloadSkins() { portraitCache.removeAll(); rebuildSkinButtons() }
    @objc private func changeSkinCategory() { rebuildSkinButtons() }
    @objc private func feedGrass() { onFeedGrass?() }
    @objc private func installSkin() { onInstallSkin?() }
    @objc private func deleteSkin() { onDeleteSkin?() }
    @objc private func changeSize(_ sender: NSSlider) { onScaleChange?(sender.doubleValue) }
    @objc private func changeOpacity(_ sender: NSSlider) { onOpacityChange?(sender.doubleValue) }
    @objc private func toggleFullscreen(_ sender: NSButton) { onFullscreenChange?(sender.state == .on) }
    @objc private func changeSleep(_ sender: NSPopUpButton) { onSleepChange?([15, 30, 60, 0][sender.indexOfSelectedItem]) }
    @objc private func changeDesk(_ sender: NSPopUpButton) { onDeskSkinChange?(["meadow", "peach", "mocha"][sender.indexOfSelectedItem]) }
    @objc private func openSupport() { NSWorkspace.shared.open(URL(string: "https://afdian.com/a/guinea_pig_garden")!) }

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
        roomPage.isHidden = tabs.selectedSegment != 1
        groupPage.isHidden = tabs.selectedSegment != 2
        refreshDirectory()
    }

    override func viewDidAppear() {
        super.viewDidAppear()
        refreshDirectory()
    }

    override func viewDidDisappear() {
        super.viewDidDisappear()
        directory = nil
        showPublicRooms([])
    }

    private func refreshDirectory() {
        guard !inRoom, !roomPage.isHidden, view.window?.isVisible == true else {
            directory = nil
            if !publicEntries.isEmpty { showPublicRooms([]) }
            return
        }
        if directory == nil { directory = RoomDirectory() }
        guard let directory = directory else { return }
        let entries = directory.refresh(now: ProcessInfo.processInfo.systemUptime)
        let unchanged = entries.elementsEqual(publicEntries) {
            $0.code == $1.code && $0.host == $1.host && $0.name == $1.name && $0.count == $1.count
        }
        if !unchanged {
            showPublicRooms(entries)
        }
        directoryStatus.isHidden = !entries.isEmpty
        directoryStatus.stringValue = directory.connected ? "暂时没有公开房间" : directory.status
    }

    private func showPublicRooms(_ entries: [RoomDirectory.Entry]) {
        publicEntries = entries
        publicDocument.subviews.forEach { $0.removeFromSuperview() }
        publicDocument.frame = NSRect(x: 0, y: 0, width: 253, height: CGFloat(entries.count) * 32)
        for (i, entry) in entries.enumerated() {
            let top = CGFloat(i) * 32
            let name = NSTextField(labelWithString: entry.label)
            name.font = .systemFont(ofSize: 12, weight: .medium)
            name.lineBreakMode = .byTruncatingTail
            name.toolTip = entry.label
            add(name, x: 5, top: top + 7, width: 146, height: 18, to: publicDocument)
            let seats = NSTextField(labelWithString: "\(entry.count)/\(RoomMembership.capacity)")
            seats.font = .systemFont(ofSize: 11)
            seats.textColor = mutedBrown
            add(seats, x: 154, top: top + 8, width: 40, height: 17, to: publicDocument)
            let full = entry.count >= RoomMembership.capacity
            let join = footerButton(full ? "已满" : "加入", action: #selector(joinListedRoom(_:)))
            join.isEnabled = !full
            join.identifier = NSUserInterfaceItemIdentifier(entry.code)
            add(join, x: 197, top: top + 3, width: 52, height: 26, to: publicDocument)
        }
    }

    @objc private func joinListedRoom(_ sender: NSButton) {
        guard let code = sender.identifier?.rawValue,
              let entry = publicEntries.first(where: { $0.code == code }), entry.count < RoomMembership.capacity else { return }
        networkError = ""
        onJoinPublicRoom?(entry)
    }

    private func roomChoice(_ title: String, hint: String, publicRoom: Bool) -> NSButton {
        let button = footerButton(title, action: #selector(createRoomAction(_:)))
        button.tag = publicRoom ? 1 : 0
        button.setAccessibilityLabel(title)
        button.cell?.wraps = true
        if !publicRoom { button.layer?.backgroundColor = NSColor(calibratedRed: 0.95, green: 0.93, blue: 0.88, alpha: 1).cgColor }
        let text = NSMutableAttributedString(string: title, attributes: [.font: NSFont.systemFont(ofSize: 12, weight: .semibold), .foregroundColor: darkBrown])
        text.append(NSAttributedString(string: "\n" + hint, attributes: [.font: NSFont.systemFont(ofSize: 10), .foregroundColor: mutedBrown]))
        button.attributedTitle = text
        return button
    }

    @objc private func selectSkin(_ sender: NSButton) {
        guard let id = sender.identifier?.rawValue else { return }
        onSelectSkin?(id)
    }
    @objc private func toggleGlow(_ sender: NSButton) { onKeyboardGlowChange?(sender.state == .on) }
    @objc private func toggleLabels(_ sender: NSButton) { onShowLabelsChange?(sender.state == .on) }
    func setLoginAtStartup(enabled: Bool, pending: Bool, message: String) {
        loginRegistered = enabled || pending
        loginSwitch.state = pending ? .mixed : (enabled ? .on : .off)
        loginMessage.stringValue = message
    }
    @objc private func toggleLoginAtStartup(_ sender: NSButton) { onLoginAtStartupChange?(!loginRegistered) }
    @objc private func toggleClickThrough(_ sender: NSButton) { onClickThroughChange?(sender.state == .on) }
    func setNetworkError(_ message: String) { networkError = message }
    @objc private func toggleShareActivity(_ sender: NSButton) { onShareActivityChange?(sender.state == .on) }
    @objc private func createRoomAction(_ sender: NSButton) { networkError = ""; onCreateRoom?(sender.tag == 1) }
    @objc private func joinRoomAction() { networkError = ""; onJoinRoom?(joinCode.stringValue) }
    @objc private func exitRoomAction() { onExitRoom?() }
    @objc private func copyRoomAction() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(roomCodeLabel.stringValue, forType: .string)
    }
    @objc private func openFeedback() {
        if let url = URL(string: "https://github.com/gwtt/guinea-pig-keyboard/issues/new/choose") { NSWorkspace.shared.open(url) }
    }
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

extension PetSkin {
    var settingsGroup: Int {
        if isCustom { return 3 }
        if isProne { return 2 }
        return id.hasPrefix("pixel-longhair-") ? 1 : 0
    }
}

private final class SettingsCardView: NSView {
    var closeAction: (() -> Void)?
    var dropAction: ((URL) -> Void)?
    override init(frame frameRect: NSRect) { super.init(frame: frameRect); registerForDraggedTypes([.fileURL]) }
    required init?(coder: NSCoder) { super.init(coder: coder); registerForDraggedTypes([.fileURL]) }
    private func packURL(_ sender: NSDraggingInfo) -> URL? {
        guard let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL],
              urls.count == 1, urls[0].pathExtension.lowercased() == "wheek" else { return nil }
        return urls[0]
    }
    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { packURL(sender) == nil ? [] : .copy }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        guard let url = packURL(sender), let dropAction = dropAction else { return false }
        dropAction(url); return true
    }
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
