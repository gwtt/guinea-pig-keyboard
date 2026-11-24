import AppKit

final class PetView: NSView {
    private static let settleTime: TimeInterval = 0.285
    private static let bubbleTime: TimeInterval = 0.395
    static let idleDelay: TimeInterval = 30
    struct DozeFrame {
        let duration: TimeInterval
        let eyes, mouth, nod: Int
    }
    private static let dozeFrames: [DozeFrame] = {
        guard let url = Bundle.main.url(forResource: "doze", withExtension: "txt"),
              let text = try? String(contentsOf: url, encoding: .utf8) else { fatalError("缺少 doze.txt") }
        return text.split(whereSeparator: \.isNewline).filter { !$0.hasPrefix("#") }.map { line in
            let values = line.split(separator: " ").compactMap { Int($0) }
            precondition(values.count == 4 && values[0] > 0, "无效的瞌睡帧")
            return DozeFrame(duration: Double(values[0]) / 1000, eyes: values[1], mouth: values[2], nod: values[3])
        }
    }()
    static func dozePose(idle: TimeInterval) -> (frame: DozeFrame, remaining: TimeInterval)? {
        guard idle >= idleDelay else { return nil }
        let cycle = dozeFrames.reduce(0) { $0 + $1.duration }
        var phase = (idle - idleDelay).truncatingRemainder(dividingBy: cycle)
        for frame in dozeFrames {
            if phase < frame.duration { return (frame, frame.duration - phase) }
            phase -= frame.duration
        }
        return (dozeFrames[0], dozeFrames[0].duration)
    }
    var onOpenSettings: (() -> Void)?

    override func rightMouseDown(with event: NSEvent) {
        onOpenSettings?()
    }

    private struct Key {
        let code: UInt16
        let label: String
        let center: CGPoint
        let width: CGFloat
        let right: Bool
    }
    private final class Stroke {
        let rest: CGPoint
        var start: CGPoint
        var target: CGPoint
        var started: TimeInterval = 0
        var code: UInt16 = 0
        var active = false
        init(_ point: CGPoint) {
            rest = point
            start = point
            target = point
        }
        func restWeight(_ now: TimeInterval) -> CGFloat {
            guard active else { return 1 }
            let age = max(0, now - started)
            return smooth(max(0, min(1, (age - 0.102) / (PetView.settleTime - 0.102))))
        }
        func position(_ now: TimeInterval) -> CGPoint {
            guard active else { return rest }
            let age = max(0, now - started)
            if age < 0.052 {
                let t = age / 0.052
                var p = mix(start, target, smooth(t))
                p.y -= 9 * sin(t * .pi)
                return p
            }
            if age < 0.102 { return target }
            if age >= PetView.settleTime { return rest }
            let t = (age - 0.102) / (PetView.settleTime - 0.102)
            var p = mix(target, rest, smooth(t))
            p.y -= 5 * sin(t * .pi)
            return p
        }
    }
    private static let keyboardBounds = CGRect(x: 86, y: 197, width: 279.84, height: 86.24)
    private static func makeKeys() -> [Key] {
        guard let url = Bundle.main.resourceURL?.appendingPathComponent("keyboard-layout.txt") else {
            fatalError("缺少 keyboard-layout.txt 资源。")
        }
        let contents: String
        do { contents = try String(contentsOf: url, encoding: .utf8).replacingOccurrences(of: "\u{feff}", with: "") }
        catch { fatalError("无法读取 keyboard-layout.txt：\(error)") }
        var result: [Key] = []
        for (index, line) in contents.components(separatedBy: .newlines).enumerated() {
            let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
            if trimmed.isEmpty || trimmed.hasPrefix("#") { continue }
            let fields = trimmed.split(whereSeparator: \.isWhitespace).map(String.init)
            guard fields.count == 7,
                  let code = UInt16(fields[1]),
                  fields[3] == "L" || fields[3] == "R",
                  let x = Double(fields[4]), let y = Double(fields[5]), let width = Double(fields[6]),
                  width > 0, x.isFinite, y.isFinite, width.isFinite else {
                fatalError("keyboard-layout.txt 第 \(index + 1) 行格式无效。")
            }
            let label = fields[2] == "Win" ? "⌘" : fields[2] == "Alt" ? "Option" : fields[2]
            result.append(Key(code: code, label: label,
                              center: CGPoint(x: CGFloat(x), y: CGFloat(y)),
                              width: CGFloat(width), right: fields[3] == "R"))
        }
        guard !result.isEmpty else { fatalError("keyboard-layout.txt 中没有键位数据。") }
        return result
    }
    private static let keys = makeKeys()
    private static let keyLookup = Dictionary(uniqueKeysWithValues: keys.map { ($0.code, $0) })
    private static let aliases: [UInt16: (UInt16, String)] = [76: (36, "Enter")]
    private static let extraLabels: [UInt16: String] = [
        123: "←", 124: "→", 125: "↓", 126: "↑", 115: "Home", 119: "End",
        116: "PgUp", 121: "PgDn", 117: "Del", 122: "F1", 120: "F2",
        99: "F3", 118: "F4", 96: "F5", 97: "F6", 98: "F7", 100: "F8",
        101: "F9", 109: "F10", 103: "F11", 111: "F12"
    ]
    private static let bubbleAttributes: [NSAttributedString.Key: Any] = [
        .font: NSFont.boldSystemFont(ofSize: 16),
        .foregroundColor: NSColor(calibratedRed: 78/255, green: 53/255, blue: 30/255, alpha: 1)
    ]
    private let left = Stroke(CGPoint(x: 172, y: 215))
    private let right = Stroke(CGPoint(x: 215, y: 213))
    private var skin = PetSkin.all[0]
    private var images: [String: CGImage] = [:]
    private var eyeFrames: [String: [CGImage]] = [:]
    private var mouseFrames: [String: [CGImage]] = [:]
    private var timer: Timer?
    private var blinkTimer: Timer?
    private var blinkStarted: TimeInterval?
    private var gazeFrame = 0
    private var lastGazeMove: TimeInterval = -.infinity
    private var lastEyeSample: TimeInterval = -.infinity
    private var gazeDX: Double = 0
    private var gazeDY: Double = 0
    private var lastFrame: TimeInterval = 0
    private var lastInput = ProcessInfo.processInfo.systemUptime
    private var mouthStarted: TimeInterval = -.infinity
    private var doze: DozeFrame?
    private var lastMouseUse: TimeInterval = -.infinity
    private var lastClick: TimeInterval = -.infinity
    private var lastScroll: TimeInterval = -.infinity
    private var scrollDirection: CGFloat = 0
    private var mousePosition = CGPoint(x: 74, y: 234)
    private var mouseDestination = CGPoint(x: 74, y: 234)
    private var mouseGrip: CGFloat = 0
    private var rightButton = false
    var showKeyLabels = true { didSet { needsDisplay = true } }
    override var isFlipped: Bool { true }
    override var mouseDownCanMoveWindow: Bool { true }

    override init(frame frameRect: NSRect) { super.init(frame: frameRect) }
    required init?(coder: NSCoder) { super.init(coder: coder) }
    deinit {
        timer?.invalidate()
        blinkTimer?.invalidate()
    }
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window == nil {
            timer?.invalidate()
            timer = nil
            blinkTimer?.invalidate()
            blinkTimer = nil
            blinkStarted = nil
            doze = nil
            gazeFrame = 0
            gazeDX = 0
            gazeDY = 0
        } else {
            lastInput = ProcessInfo.processInfo.systemUptime
            scheduleBlink()
        }
    }
    static func blinkFrame(elapsed: TimeInterval) -> Int? {
        guard elapsed >= 0, elapsed < 0.20 else { return nil }
        return [9, 10, 11, 10, 9][min(4, Int(elapsed / 0.04))]
    }
    private func scheduleBlink() {
        guard window != nil, blinkTimer == nil, blinkStarted == nil else { return }
        let idle = ProcessInfo.processInfo.systemUptime - lastInput
        let pose = Self.dozePose(idle: idle)
        doze = pose?.frame
        let delay = pose?.remaining ?? min(Double.random(in: 3...6), Self.idleDelay - idle)
        let timer = Timer(timeInterval: delay, repeats: false) { [weak self] _ in
            guard let self = self else { return }
            self.blinkTimer = nil
            let now = ProcessInfo.processInfo.systemUptime
            if now - self.lastInput >= Self.idleDelay {
                self.scheduleBlink()
                self.needsDisplay = true
                return
            }
            self.blinkStarted = now
            self.wake(now)
        }
        blinkTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }
    func noteInput(_ now: TimeInterval, twitch: Bool = false) {
        lastInput = now
        if twitch && now - mouthStarted >= 0.270 { mouthStarted = now }
        if doze != nil {
            doze = nil
            blinkTimer?.invalidate()
            blinkTimer = nil
            scheduleBlink()
            needsDisplay = true
        }
    }
    func selectSkin(_ id: String) {
        skin = PetSkin.named(id)
        needsDisplay = true
    }
    func pressKey(_ keyCode: UInt16) {
        let key = keyInfo(keyCode)
        let hand = key.right ? left : right
        let now = ProcessInfo.processInfo.systemUptime
        noteInput(now, twitch: true)
        let pose = currentPose(now)
        let start = key.right ? pose.leftTip : pose.rightTip
        let other = key.right ? right : left
        var when = now
        if other.active && other.started <= now && abs(key.center.x - other.target.x) > 140 {
            when = max(now, other.started + 0.130)
        }
        lastMouseUse = -.infinity
        lastClick = -.infinity
        lastScroll = -.infinity
        hand.start = start
        hand.target = key.center
        hand.started = when
        hand.code = keyCode
        hand.active = true
        wake(now)
    }
    func moveMouse(dx: Double, dy: Double) {
        guard dx.isFinite, dy.isFinite, dx != 0 || dy != 0 else { return }
        mouseDestination.x = max(65, min(82, mouseDestination.x + dx * 0.055))
        mouseDestination.y = max(226, min(240, mouseDestination.y + dy * 0.055))
        let now = ProcessInfo.processInfo.systemUptime
        noteInput(now)
        lastMouseUse = now
        updateGaze(dx: dx, dy: dy, now: now)
        wake(now)
    }
    private func updateGaze(dx: Double, dy: Double, now: TimeInterval) {
        if now - lastEyeSample > 0.4 {
            gazeDX = 0
            gazeDY = 0
        }
        lastEyeSample = now
        gazeDX = gazeDX * 0.65 + dx
        gazeDY = gazeDY * 0.65 + dy
        let magnitude = hypot(gazeDX, gazeDY)
        guard magnitude >= 2 else { return }
        let horizontal = abs(gazeDX) >= magnitude * 0.38 ? (gazeDX < 0 ? -1 : 1) : 0
        let vertical = abs(gazeDY) >= magnitude * 0.38 ? (gazeDY < 0 ? -1 : 1) : 0
        switch (horizontal, vertical) {
        case (-1, 0): gazeFrame = 1
        case (1, 0): gazeFrame = 2
        case (0, -1): gazeFrame = 3
        case (0, 1): gazeFrame = 4
        case (-1, -1): gazeFrame = 5
        case (1, -1): gazeFrame = 6
        case (-1, 1): gazeFrame = 7
        case (1, 1): gazeFrame = 8
        default: gazeFrame = 0
        }
        lastGazeMove = now
    }
    func clickMouse(right: Bool) {
        rightButton = right
        let now = ProcessInfo.processInfo.systemUptime
        noteInput(now, twitch: true)
        lastMouseUse = now
        lastClick = now + (1 - mouseGrip) * 0.110
        wake(now)
    }
    func scrollMouse(_ delta: Int64) {
        guard delta != 0 else { return }
        let now = ProcessInfo.processInfo.systemUptime
        noteInput(now)
        scrollDirection = delta > 0 ? 1 : -1
        lastScroll = now
        lastMouseUse = now
        wake(now)
    }
    private func wake(_ now: TimeInterval) {
        guard window != nil else { needsDisplay = true; return }
        if timer == nil {
            lastFrame = now
            let t = Timer(timeInterval: 0.024, repeats: true) { [weak self] _ in self?.tick() }
            timer = t
            RunLoop.main.add(t, forMode: .common)
        }
        needsDisplay = true
    }
    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        if let started = blinkStarted, Self.blinkFrame(elapsed: now - started) == nil {
            blinkStarted = nil
            scheduleBlink()
        }
        if gazeFrame != 0 && now - lastGazeMove >= 0.4 {
            gazeFrame = 0
            gazeDX = 0
            gazeDY = 0
        }
        for hand in [left, right] where hand.active && now - hand.started >= Self.bubbleTime { hand.active = false }
        advanceMouse(now)
        if !left.active && !right.active && mouseGrip == 0 && mousePosition == mouseDestination
            && now - lastMouseUse > 0.250 && blinkStarted == nil && gazeFrame == 0 {
            timer?.invalidate()
            timer = nil
        }
        needsDisplay = true
    }
    private func advanceMouse(_ now: TimeInterval) {
        let elapsed = max(0, now - lastFrame)
        lastFrame = now
        let holding = now - lastMouseUse < 0.520
        let typing = lastMouseUse.isInfinite && lastMouseUse < 0
        mouseGrip = holding ? min(1, mouseGrip + elapsed / 0.145)
            : max(0, mouseGrip - elapsed / (typing ? 0.055 : 0.195))
        let traction = smooth(max(0, (mouseGrip - 0.64) / 0.36))
        let follow = (1 - exp(-elapsed / 0.048)) * traction
        mousePosition.x += (mouseDestination.x - mousePosition.x) * follow
        mousePosition.y += (mouseDestination.y - mousePosition.y) * follow
        if hypot(mousePosition.x - mouseDestination.x, mousePosition.y - mouseDestination.y) < 0.1 {
            mousePosition = mouseDestination
        }
        if !holding && mouseGrip == 0 { mouseDestination = mousePosition }
    }

    private struct Pose {
        let body: CGRect
        let leftTip: CGPoint
        let rightTip: CGPoint
    }

    private func currentPose(_ now: TimeInterval) -> Pose {
        var lp = left.position(now), rp = right.position(now)
        let grip = smooth(mouseGrip)
        let maxLean: CGFloat = skin.isDrawn ? 115 : 90
        var lean = max(-78, min(maxLean, ((lp.x - left.rest.x) + (rp.x - right.rest.x)) * 0.84))
        lean += (mousePosition.x - 8 - 135 - lean) * grip
        let reachY = max(0, max(lp.y - left.rest.y, rp.y - right.rest.y))
        let dip = min(19, reachY * 0.42) * (1 - grip) + (12 + (mousePosition.y - 226) * 0.45) * grip
        lp.x += lean * left.restWeight(now)
        lp.y += dip * 0.68 * left.restWeight(now)
        rp.x += lean * right.restWeight(now)
        rp.y += dip * 0.68 * right.restWeight(now)

        let brace = CGPoint(x: 153, y: 227)
        rp = mix(rp, brace, grip * right.restWeight(now))
        let press = contact(now - lastClick)
        let scroll = CGFloat(max(0, 1 - (now - lastScroll) / 0.245))
        let mouseTip = CGPoint(x: mousePosition.x + 5.5 + (rightButton ? -11 : -1) * press,
                               y: mousePosition.y + 2.5 + (rightButton ? -1 : 3.2) * press - 11 * scrollDirection * scroll)
        lp = mix(lp, mouseTip, grip)
        lp.y -= sin(grip * .pi) * 5
        let nod = CGFloat((doze?.nod ?? 0) * 2)
        let body = skin.isDrawn
            ? CGRect(x: 135 + lean, y: 123 + dip + nod, width: 129, height: 85 - nod)
            : CGRect(x: 126 + lean.rounded(), y: 39 + dip.rounded() + nod, width: 174, height: 181 - nod)
        return Pose(body: body,
                    leftTip: lp, rightTip: rp)
    }
    private func keyInfo(_ code: UInt16) -> Key {
        if let key = Self.keyLookup[code] { return key }
        if let (alias, label) = Self.aliases[code], let key = Self.keyLookup[alias] {
            return Key(code: code, label: label,
                       center: key.center, width: key.width, right: true)
        }
        let isRight = [123, 124, 125, 126, 115, 119, 116, 121, 117, 76].contains(Int(code))
            || (65...92).contains(Int(code))
        let fallback = Self.keyLookup[isRight ? 38 : 3]!
        return Key(code: code, label: Self.extraLabels[code] ?? "", center: fallback.center,
                   width: fallback.width, right: isRight)
    }
    private func image(_ name: String) -> CGImage? {
        if let cached = images[name] { return cached }
        guard let url = Bundle.main.resourceURL?.appendingPathComponent(name), let image = NSImage(contentsOf: url), let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        images[name] = cg
        return cg
    }
    private func sprite(_ name: String, _ rect: CGRect, _ ctx: CGContext) {
        guard let image = image(name) else { return }
        drawImage(image, rect, ctx)
    }
    private func eyeImage(_ frame: Int) -> CGImage? {
        guard skin.isDrawn, (0..<12).contains(frame) else { return nil }
        if eyeFrames[skin.id] == nil {
            let atlasName = skin.networkID + "-eyes.png"
            guard let atlas = image(atlasName), atlas.width % 4 == 0, atlas.height % 3 == 0 else { return nil }
            let width = atlas.width / 4, height = atlas.height / 3
            var frames: [CGImage] = []
            for index in 0..<12 {
                guard let cropped = atlas.cropping(to: CGRect(x: (index % 4) * width, y: (index / 4) * height,
                                                               width: width, height: height)) else { return nil }
                frames.append(cropped)
            }
            eyeFrames[skin.id] = frames
        }
        return eyeFrames[skin.id]?[frame]
    }
    private func drawImage(_ image: CGImage, _ rect: CGRect, _ ctx: CGContext) {
        ctx.saveGState()
        ctx.translateBy(x: rect.minX, y: rect.maxY)
        ctx.scaleBy(x: 1, y: -1)
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: rect.width, height: rect.height))
        ctx.restoreGState()
    }
    private func mouseImage(_ frame: Int) -> CGImage? {
        let name = skin.isDrawn ? "drawn-mouse.png" : "mouse.png"
        if mouseFrames[name] == nil {
            guard let atlas = image(name), atlas.width % 3 == 0 else { return nil }
            let width = atlas.width / 3
            var frames: [CGImage] = []
            for index in 0..<3 {
                guard let cropped = atlas.cropping(to: CGRect(x: index * width, y: 0,
                                                               width: width, height: atlas.height)) else { return nil }
                frames.append(cropped)
            }
            mouseFrames[name] = frames
        }
        guard let frames = mouseFrames[name], frames.indices.contains(frame) else { return nil }
        return frames[frame]
    }
    private func drawKeyboard(_ ctx: CGContext) {
        let name = skin.isDrawn ? "drawn-keyboard.png" : "keyboard.png"
        guard let artwork = image(name) else { return }
        drawImage(artwork, Self.keyboardBounds, ctx)
    }
    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let scale = min(bounds.width / 400, bounds.height / 308)
        guard scale > 0 else { return }
        ctx.saveGState()
        ctx.translateBy(x: (bounds.width - 400 * scale) / 2, y: (bounds.height - 308 * scale) / 2)
        ctx.scaleBy(x: scale, y: scale)
        ctx.translateBy(x: 9, y: 0)
        ctx.interpolationQuality = skin.isDrawn ? .high : .none
        let now = ProcessInfo.processInfo.systemUptime
        let pose = currentPose(now)
        let l = left.active ? contact(now - left.started) : 0
        let r = right.active ? contact(now - right.started) : 0
        let press = contact(now - lastClick) * smooth(mouseGrip)
        let mouseFrame = press > 0.32 ? (rightButton ? 2 : 1) : 0
        let awakeEyes = blinkStarted.flatMap { Self.blinkFrame(elapsed: now - $0) } ?? (max(l, r) > 0.45 ? 10 : gazeFrame)
        let eyeFrame = doze.map { $0.eyes == 2 ? 11 : 10 } ?? awakeEyes
        let mouth = doze?.mouth ?? (now - mouthStarted < 0.145 ? 1 : 0)
        let bodyRect = pose.body
        if skin.isDrawn {
            drawDeskAndPaws(pose, l: l, r: r, mouseFrame: mouseFrame, ctx)
            ctx.saveGState()
            ctx.translateBy(x: 2 * bodyRect.midX, y: 0)
            ctx.scaleBy(x: -1, y: 1)
            sprite(skin.body, bodyRect, ctx)
            let gaze = [0, 2, 1, 3, 4, 6, 5, 8, 7][gazeFrame]
            let frame = doze.map { $0.eyes == 2 ? 11 : 10 }
                ?? (blinkStarted.flatMap { Self.blinkFrame(elapsed: now - $0) } ?? (max(l, r) > 0.45 ? 10 : gaze))
            if let eyes = eyeImage(frame) {
                ctx.interpolationQuality = .high
                drawImage(eyes, CGRect(x: bodyRect.minX + bodyRect.width * 0.58,
                                       y: bodyRect.minY + bodyRect.height * 0.25,
                                       width: bodyRect.width * 0.22, height: bodyRect.height * 0.30), ctx)
            }
            ctx.restoreGState()
        } else {
            sprite(skin.body, bodyRect, ctx)
            if mouth > 0 {
                let sx = bodyRect.width / 100, sy = bodyRect.height / 104
                sprite(mouth == 1 ? "mouth-peep.png" : "mouth-yawn.png",
                       CGRect(x: bodyRect.minX + 45 * sx, y: bodyRect.minY + 49 * sy,
                              width: 11 * sx, height: (mouth == 1 ? 5 : 11) * sy), ctx)
            }
            skin.drawEyes(in: bodyRect, frame: eyeFrame, context: ctx)
            drawDeskAndPaws(pose, l: l, r: r, mouseFrame: mouseFrame, ctx)
        }
        if showKeyLabels {
            drawBubble(left, now, false, bodyRect, ctx)
            drawBubble(right, now, true, bodyRect, ctx)
        }
        ctx.restoreGState()
    }
    private func drawDeskAndPaws(_ pose: Pose, l: CGFloat, r: CGFloat, mouseFrame: Int, _ ctx: CGContext) {
        drawKeyboard(ctx)
        if l > 0 { drawPressed(keyInfo(left.code), l, ctx) }
        if r > 0 { drawPressed(keyInfo(right.code), r, ctx) }
        if let mouse = mouseImage(mouseFrame) {
            let origin = skin.isDrawn ? mousePosition : CGPoint(x: mousePosition.x.rounded(), y: mousePosition.y.rounded())
            drawImage(mouse, CGRect(x: origin.x - 20, y: origin.y - 18, width: 40, height: 36), ctx)
        }
        drawPaw(pose.rightTip, pose.body, true, ctx)
        drawPaw(pose.leftTip, pose.body, false, ctx)
    }
    private func drawPressed(_ key: Key, _ amount: CGFloat, _ ctx: CGContext) {
        let width = max(0, key.width - 1.5)
        ctx.saveGState()
        ctx.translateBy(x: key.center.x, y: key.center.y)
        ctx.rotate(by: -3 * .pi / 180)
        let rect = CGRect(x: -width / 2, y: -7.3 / 2 + amount * 1.2,
                          width: width, height: 7.3)
        ctx.saveGState()
        ctx.setAlpha(amount)
        ctx.setFillColor(NSColor(calibratedRed: 1, green: 185/255, blue: 74/255, alpha: 0.31).cgColor)
        ctx.setStrokeColor(NSColor(calibratedRed: 125/255, green: 75/255, blue: 32/255, alpha: 0.47).cgColor)
        ctx.setLineWidth(0.9)
        ctx.addPath(CGPath(roundedRect: rect, cornerWidth: 2.5, cornerHeight: 2.5, transform: nil))
        ctx.drawPath(using: .fillStroke)
        ctx.restoreGState()
        ctx.restoreGState()
    }
    private func drawPaw(_ tip: CGPoint, _ chest: CGRect, _ far: Bool, _ ctx: CGContext) {
        let grip = far ? CGFloat(0) : smooth(mouseGrip)
        let anchorX = chest.minX + (far ? (skin.isDrawn ? 75 : 89) : (skin.isDrawn ? 36 : 46))
        let shoulder = CGPoint(x: anchorX + max(-13, min(13, (tip.x - anchorX) * 0.30)),
                               y: chest.minY + (skin.isDrawn ? 63 : chest.height * (153.0 / 181.0)))
        let reachX = tip.x - shoulder.x, reachY = tip.y - shoulder.y
        var angle = max(-0.58, min(0.58, -atan2(reachX, reachY)))
        angle += (0.52 - angle) * grip
        let down = CGPoint(x: -sin(angle), y: cos(angle))
        let joint = CGPoint(x: tip.x - down.x * 6.8, y: tip.y - down.y * 6.8)
        let axis = CGPoint(x: joint.x - shoulder.x, y: joint.y - shoulder.y)
        let length = hypot(axis.x, axis.y)
        if length > 1 {
            let normal = CGPoint(x: axis.y / length, y: -axis.x / length)
            let bend = CGPoint(x: shoulder.x + axis.x * 0.55 + (far ? 2.6 : -3.2),
                               y: shoulder.y + axis.y * 0.55 - 2.0)
            let path = CGMutablePath()
            path.move(to: CGPoint(x: shoulder.x - normal.x * 6.2, y: shoulder.y - normal.y * 6.2))
            path.addQuadCurve(to: CGPoint(x: joint.x - normal.x * 4.5, y: joint.y - normal.y * 4.5),
                              control: CGPoint(x: bend.x - normal.x * 5.5, y: bend.y - normal.y * 5.5))
            path.addLine(to: CGPoint(x: joint.x + normal.x * 4.5, y: joint.y + normal.y * 4.5))
            path.addQuadCurve(to: CGPoint(x: shoulder.x + normal.x * 6.2, y: shoulder.y + normal.y * 6.2),
                              control: CGPoint(x: bend.x + normal.x * 5.5, y: bend.y + normal.y * 5.5))
            path.closeSubpath()
            ctx.addPath(path)
            ctx.setFillColor(skin.furColor)
            ctx.setStrokeColor(skin.inkColor)
            ctx.setLineWidth(1.05)
            ctx.drawPath(using: .fillStroke)
        }
        guard let paw = image(skin.paw) else { return }
        let sideScale = far ? CGFloat(0) : grip
        let width = 17 - 2.2 * sideScale
        let height = 15.4 - 2 * sideScale
        ctx.saveGState()
        ctx.translateBy(x: tip.x, y: tip.y)
        ctx.rotate(by: angle)
        if far { ctx.scaleBy(x: -1, y: 1) }
        ctx.interpolationQuality = skin.isDrawn ? .high : .none
        drawImage(paw, CGRect(x: -width / 2, y: -15 + sideScale * 2,
                              width: width, height: height), ctx)
        ctx.restoreGState()
    }
    private func drawBubble(_ hand: Stroke, _ now: TimeInterval, _ onRight: Bool, _ bodyRect: CGRect, _ ctx: CGContext) {
        guard hand.active else { return }
        let label = hand.code == 49 ? "Space" : keyInfo(hand.code).label
        let age = now - hand.started
        guard !label.isEmpty, age >= 0.022, age < Self.bubbleTime else { return }
        let t = (age - 0.022) / (Self.bubbleTime - 0.022), opacity = min(1, min(t * 9, (1 - t) * 3.8))
        let attrs = Self.bubbleAttributes
        let text = label as NSString, size = text.size(withAttributes: attrs), width = max(29, size.width + 13)
        let x = onRight ? max(300, hand.target.x + 6) : min(90, hand.target.x - 6)
        var y = 173 - 34 * t
        if x + width / 2 > bodyRect.minX - 5 && x - width / 2 < bodyRect.maxX + 5 {
            y = min(y, bodyRect.minY - 18)
        }
        ctx.saveGState()
        ctx.setAlpha(opacity)
        ctx.translateBy(x: x, y: y)
        ctx.rotate(by: (onRight ? 7 : -7) * .pi / 180)
        ctx.setStrokeColor(NSColor(calibratedRed: 78/255, green: 53/255, blue: 30/255, alpha: 1).cgColor)
        ctx.setLineWidth(1.55)
        for (offset, color) in [(CGFloat(-11), NSColor(calibratedRed: 229/255, green: 188/255, blue: 97/255, alpha: 1)), (CGFloat(-14), NSColor(calibratedRed: 1, green: 243/255, blue: 216/255, alpha: 1))] {
            ctx.setFillColor(color.cgColor)
            ctx.addPath(CGPath(roundedRect: CGRect(x: -width/2, y: offset, width: width, height: 24), cornerWidth: 5, cornerHeight: 5, transform: nil))
            ctx.drawPath(using: .fillStroke)
        }
        text.draw(at: CGPoint(x: -size.width/2, y: -2 - size.height/2), withAttributes: attrs)
        ctx.restoreGState()
    }
}

private func smooth(_ t: CGFloat) -> CGFloat { t * t * (3 - 2 * t) }
private func mix(_ a: CGPoint, _ b: CGPoint, _ t: CGFloat) -> CGPoint { CGPoint(x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t) }
private func contact(_ age: TimeInterval) -> CGFloat {
    guard age >= 0.035, age <= 0.155 else { return 0 }
    return min(1, min((age - 0.035) / 0.017, (0.155 - age) / 0.053))
}
