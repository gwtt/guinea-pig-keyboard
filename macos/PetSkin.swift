import AppKit

struct EditableRGBABitmap {
    private let context: CGContext
    private let pixels: UnsafeMutablePointer<UInt8>
    var width: Int { context.width }
    var height: Int { context.height }
    var cgImage: CGImage? { context.makeImage() }

    init?(_ image: CGImage) {
        let format = CGBitmapInfo.byteOrder32Big.rawValue | CGImageAlphaInfo.premultipliedLast.rawValue
        guard let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: image.width, height: image.height,
                bitsPerComponent: 8, bytesPerRow: 0, space: colorSpace, bitmapInfo: format),
              let data = context.data else { return nil }
        context.interpolationQuality = .none
        context.setBlendMode(.copy)
        context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        self.context = context
        pixels = data.assumingMemoryBound(to: UInt8.self)
    }

    func rgb(atX x: Int, y: Int) -> [Int]? {
        let pixel = pixels.advanced(by: y * context.bytesPerRow + x * 4)
        let alpha = Int(pixel[3])
        guard alpha > 0 else { return nil }
        return (0..<3).map { min(255, (Int(pixel[$0]) * 255 + alpha / 2) / alpha) }
    }

    func setRGB(_ rgb: [Int], atX x: Int, y: Int) {
        let pixel = pixels.advanced(by: y * context.bytesPerRow + x * 4)
        let alpha = Int(pixel[3])
        for channel in 0..<3 { pixel[channel] = UInt8((rgb[channel] * alpha + 127) / 255) }
    }
}

struct PetSkin {
    let id: String
    let name: String
    let body: String
    let paw: String
    let networkID: String
    let appearanceNetworkID: String
    let isProne: Bool
    let furColor: CGColor
    let inkColor: CGColor
    private let eyeColor: CGColor

    private static let builtins = [
        PetSkin("tricolor", "三花", paw: "tricolor", networkID: "tricolor", darkEyes: true),
        PetSkin("black-white", "黑白", paw: "black-white", networkID: "silver-agouti", darkEyes: true),
        PetSkin("golden-white", "黄白", paw: "golden-white", networkID: "gold-white"),
        PetSkin("buff", "浅金棕", coat: "white", paw: "white", networkID: "gold-white", fur: 0xE6C391),
        PetSkin("cream", "奶油", paw: "cream", networkID: "californian", fur: 0xF5DEAF),
        PetSkin("chocolate", "巧克力", paw: "chocolate", networkID: "teddy", fur: 0xA17B63, ink: 0x4A3127),
        PetSkin("californian", "加州", paw: "tricolor", networkID: "californian"),
        PetSkin("longhair-gold-white", "长毛黄白", coat: "longhair-template", paw: "golden-white", networkID: "longhair"),
        PetSkin("longhair-tricolor", "长毛三花", paw: "tricolor", networkID: "longhair", darkEyes: true),
        PetSkin("longhair-black-white", "长毛黑白", paw: "black-white", networkID: "longhair", darkEyes: true),
        PetSkin("longhair-buff", "长毛浅金棕", paw: "white", networkID: "longhair", fur: 0xE6C391),
        PetSkin("longhair-cream", "长毛奶油", paw: "cream", networkID: "longhair", fur: 0xF5DEAF),
        PetSkin("longhair-chocolate", "长毛巧克力", paw: "chocolate", networkID: "longhair", fur: 0xA17B63, ink: 0x4A3127),
        PetSkin("longhair-californian", "长毛加州", paw: "tricolor", networkID: "longhair"),
        PetSkin("tricolor", "趴趴三花", paw: "tricolor", networkID: "tricolor", darkEyes: true, prone: true),
        PetSkin("black-white", "趴趴黑白", paw: "black-white", networkID: "silver-agouti", darkEyes: true, prone: true),
        PetSkin("golden-white", "趴趴黄白", paw: "golden-white", networkID: "gold-white", prone: true),
        PetSkin("cream", "趴趴奶油", paw: "cream", networkID: "californian", fur: 0xF5DEAF, prone: true),
        PetSkin("californian", "趴趴白加州", paw: "tricolor", networkID: "californian", prone: true),
        PetSkin("teddy", "趴趴美泰", paw: "chocolate", networkID: "teddy", fur: 0xA17B63, ink: 0x4A3127, prone: true),
        PetSkin("silver-agouti", "趴趴雕灰", paw: "black-white", networkID: "silver-agouti", darkEyes: true, prone: true),
        PetSkin("longhair", "趴趴长毛猪", paw: "golden-white", networkID: "longhair", prone: true)
    ]

    private static var custom: [PetSkin] = []
    static var all: [PetSkin] { builtins + custom }
    var isCustom: Bool { id.hasPrefix("wheek-") }
    var usesDrawnArt: Bool { isProne && !isCustom }
    var bodyImage: CGImage? = nil
    var pawImage: CGImage? = nil
    var mouthImage: CGImage? = nil
    private static var limbImages: [String: CGImage] = [:]

    func drawnLimb(_ resource: String) -> CGImage? {
        let key = id + "/" + resource
        if let cached = Self.limbImages[key] { return cached }
        guard let url = Bundle.main.url(forResource: resource, withExtension: "png"),
              let source = NSImage(contentsOf: url)?.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        if id != "prone-tricolor" {
            let brown: UInt32
            switch id {
            case "prone-black-white": brown = 0x433D3A
            case "prone-cream": brown = 0xE8D2A4
            case "prone-californian": brown = 0x76604E
            case "prone-teddy": brown = 0xA66E47
            case "prone-silver-agouti": brown = 0xAAA095
            default: brown = 0xE7C383
            }
            let preserveLight = ["prone-black-white", "prone-golden-white", "prone-longhair"].contains(id)
            guard let bitmap = EditableRGBABitmap(source) else { return nil }
            let coatRGB = [Int(brown >> 16), Int((brown >> 8) & 255), Int(brown & 255)]
            for y in 0..<bitmap.height {
                for x in 0..<bitmap.width {
                    guard let pixel = bitmap.rgb(atX: x, y: y) else { continue }
                    let r = Double(pixel[0]), g = Double(pixel[1]), b = Double(pixel[2])
                    if r < 90 && g < 65 && b < 45 { continue }
                    let light = r * 0.2126 + g * 0.7152 + b * 0.0722
                    if preserveLight && light > 180 { continue }
                    let factor = min(1.12, max(0.75, light / (light > 180 ? 235 : 97)))
                    bitmap.setRGB(coatRGB.map { min(255, Int((Double($0) * factor).rounded())) }, atX: x, y: y)
                }
            }
            Self.limbImages[key] = bitmap.cgImage
        } else { Self.limbImages[key] = source }
        return Self.limbImages[key]
    }

    @discardableResult
    static func registerCustom(id: String, name: String, baseCoat: String?, author: String?, furColor: UInt32?, inkColor: UInt32?, eyeColor: UInt32?, body: Data?, paw: Data?, mouth: Data?) -> PetSkin {
        var base = named(baseCoat)
        if base.isCustom { base = builtins[0] }
        let drawnBases = ["prone-californian": "pixel-californian", "prone-teddy": "pixel-chocolate",
                          "prone-silver-agouti": "pixel-black-white", "prone-longhair": "pixel-longhair-gold-white"]
        if let replacement = drawnBases[base.id] { base = named(replacement) }
        var skin = PetSkin(id: id, name: name, base: base, fur: furColor, ink: inkColor, eye: eyeColor)
        func decode(_ data: Data?) -> CGImage? {
            guard let data = data, let image = NSImage(data: data) else { return nil }
            return image.cgImage(forProposedRect: nil, context: nil, hints: nil)
        }
        func resource(_ name: String) -> CGImage? {
            guard let url = Bundle.main.url(forResource: name, withExtension: nil), let image = NSImage(contentsOf: url) else { return nil }
            return image.cgImage(forProposedRect: nil, context: nil, hints: nil)
        }
        skin.bodyImage = decode(body)
        skin.pawImage = decode(paw)
        skin.mouthImage = decode(mouth)
        if furColor != nil || inkColor != nil {
            if skin.bodyImage == nil, let source = resource(base.body) {
                skin.bodyImage = recolor(source, base: base, fur: furColor, ink: inkColor)
            }
            if skin.pawImage == nil, let source = resource("paw.png") {
                skin.pawImage = recolor(source, base: base, fur: furColor, ink: inkColor)
            }
        }
        custom.removeAll { $0.id == id }
        custom.append(skin)
        return skin
    }
    static func unregisterCustom(id: String) { custom.removeAll { $0.id == id } }
    private init(id: String, name: String, base: PetSkin, fur: UInt32?, ink: UInt32?, eye: UInt32?) {
        self.id = id; self.name = name; self.body = base.body; self.paw = base.paw
        self.networkID = base.networkID; self.appearanceNetworkID = base.id; self.isProne = base.isProne
        self.furColor = fur.map(Self.color) ?? base.furColor
        self.inkColor = ink.map(Self.color) ?? base.inkColor
        self.eyeColor = eye.map(Self.color) ?? base.eyeColor
    }

    private static func recolor(_ source: CGImage, base: PetSkin, fur: UInt32?, ink: UInt32?) -> CGImage? {
        guard let rep = EditableRGBABitmap(source) else { return nil }
        func rgb(_ value: UInt32) -> [Int] { [Int((value >> 16) & 255), Int((value >> 8) & 255), Int(value & 255)] }
        func components(_ color: CGColor) -> [Int] {
            let c = NSColor(cgColor: color)!.usingColorSpace(.sRGB)!
            return [c.redComponent, c.greenComponent, c.blueComponent].map { Int(($0 * 255).rounded()) }
        }
        let targetFur = fur.map(rgb) ?? components(base.furColor)
        let targetInk = ink.map(rgb) ?? components(base.inkColor)
        func blend(_ amount: Double) -> [Int] {
            zip(targetFur, targetInk).map { Int((Double($0) + Double($1 - $0) * amount).rounded(.toNearestOrEven)) }
        }
        func point(_ x: Int) -> [Int] {
            let coat = base.id.replacingOccurrences(of: "prone-", with: "pixel-")
            if coat == "pixel-tricolor" || coat == "pixel-longhair-tricolor" {
                return rgb(x < 6 ? 0x77706A : x > 11 ? 0xDCA45E : 0xA49684)
            }
            if coat == "pixel-black-white" || coat == "pixel-longhair-black-white" {
                return rgb(x > 9 ? 0xD0BAA7 : 0x77706A)
            }
            if coat.contains("golden-white") || coat == "pixel-longhair-gold-white" { return rgb(0xEBD29B) }
            if coat.contains("buff") { return rgb(0xB1896B) }
            if coat.contains("cream") { return rgb(0xBE9E74) }
            if coat.contains("chocolate") { return rgb(0x755341) }
            return rgb(0xA49684)
        }
        func furAt(_ x: Int, _ y: Int) -> [Int] {
            let px = Double(x / 2 * 2), py = Double(y / 2 * 2)
            func inside(_ cx: Double, _ cy: Double, _ rx: Double, _ ry: Double) -> Bool {
                let dx = (px - cx) / rx, dy = (py - cy) / ry
                return dx * dx + dy * dy < 1
            }
            let left = inside(20, 24, 21, 29), right = inside(85, 24, 21, 30)
            let leftSide = inside(2, 101, 23, 31), rightSide = inside(101, 99, 22, 28)
            let coat = base.id.replacingOccurrences(of: "prone-", with: "pixel-")
            if coat == "pixel-tricolor" || coat == "pixel-longhair-tricolor" {
                if left || leftSide { return rgb(0x77706A) }
                if right || rightSide { return rgb(0xDCA45E) }
            }
            if (coat == "pixel-black-white" || coat == "pixel-longhair-black-white") && (left || rightSide) { return rgb(0x77706A) }
            if (coat == "pixel-golden-white" || coat == "pixel-longhair-gold-white") && (left || right || leftSide) { return rgb(0xEBD29B) }
            return targetFur
        }
        for y in 0..<rep.height {
            for x in 0..<rep.width {
                guard let v = rep.rgb(atX: x, y: y) else { continue }
                var next = v
                if v == rgb(0xFFFAF1) || (fur != nil && !(v[0] < 110 && v[1] < 85 && v[2] < 70)) {
                    next = fur != nil ? targetFur : furAt(x, y)
                } else if v == rgb(0xA49684) { next = fur != nil ? blend(0.28) : point(x) }
                else if v == rgb(0x624737) || v == rgb(0x4A3127) { next = targetInk }
                else if v == rgb(0xB59779) { next = blend(0.35) }
                else if v == rgb(0xD2C1A9) { next = blend(0.16) }
                else if v == rgb(0x7C614D) { next = blend(0.62) }
                if next != v { rep.setRGB(next, atX: x, y: y) }
            }
        }
        return rep.cgImage
    }

    private static let legacyIDs = [
        "tricolor": "pixel-tricolor", "californian": "pixel-californian", "gold-white": "pixel-golden-white",
        "teddy": "pixel-chocolate", "silver-agouti": "pixel-black-white", "longhair": "pixel-longhair-gold-white",
        "black-white": "pixel-black-white", "golden-white": "pixel-golden-white", "white": "pixel-buff",
        "cream": "pixel-cream", "chocolate": "pixel-chocolate", "longhair-template": "pixel-longhair-gold-white",
        "longhair-tricolor": "pixel-longhair-tricolor", "longhair-black-white": "pixel-longhair-black-white",
        "longhair-buff": "pixel-longhair-buff", "longhair-cream": "pixel-longhair-cream",
        "longhair-chocolate": "pixel-longhair-chocolate", "longhair-californian": "pixel-longhair-californian"
    ]
    private static let gaze = [CGPoint.zero, CGPoint(x: -2, y: 0), CGPoint(x: 2, y: 0),
                               CGPoint(x: 0, y: -2), CGPoint(x: 0, y: 2),
                               CGPoint(x: -2, y: -2), CGPoint(x: 2, y: -2),
                               CGPoint(x: -2, y: 2), CGPoint(x: 2, y: 2)]
    private static let openEye: [UInt8] = [0b011110, 0b111111, 0b111111, 0b111111, 0b111111, 0b011110]
    private static let halfEye: [UInt8] = [0, 0, 0b011110, 0b111111, 0b011110, 0]
    private static let closedEye: [UInt8] = [0, 0, 0b100001, 0b110011, 0b011110, 0]
    private static let glint = color(0xFFF0D5)

    private init(_ id: String, _ name: String, coat: String? = nil, paw: String, networkID: String,
                 fur: UInt32 = 0xFFFAF1, ink: UInt32 = 0x624737, darkEyes: Bool = false, prone: Bool = false) {
        self.isProne = prone
        self.id = (prone ? "prone-" : "pixel-") + id
        self.name = name
        self.body = "guinea-pig-" + (prone ? self.id : (coat ?? id)) + ".png"
        self.paw = "paw-\(paw).png"
        self.networkID = networkID
        self.appearanceNetworkID = self.id
        self.furColor = Self.color(fur)
        self.inkColor = Self.color(ink)
        self.eyeColor = Self.color(darkEyes ? 0x302722 : ink)
    }

    static func find(_ id: String) -> PetSkin? {
        let legacyID = id.hasPrefix("drawn-") ? String(id.dropFirst(6)) : id
        let resolved = legacyIDs[legacyID] ?? id
        return all.first { $0.id == resolved }
    }

    static func named(_ id: String?) -> PetSkin { id.flatMap { find($0) } ?? all[0] }

    private static func color(_ rgb: UInt32) -> CGColor {
        NSColor(srgbRed: CGFloat((rgb >> 16) & 255) / 255, green: CGFloat((rgb >> 8) & 255) / 255,
                blue: CGFloat(rgb & 255) / 255, alpha: 1).cgColor
    }

    func drawEyes(in rect: CGRect, frame: Int, context: CGContext) {
        let offset = Self.gaze.indices.contains(frame) ? Self.gaze[frame] : .zero
        let rows = frame == 11 ? Self.closedEye : frame >= 9 ? Self.halfEye : Self.openEye
        let sx = rect.width / 100, sy = rect.height / (isProne ? 58 : 104)
        let positions: [CGFloat] = isProne ? [73] : [30, 65]
        let eyeY: CGFloat = isProne ? 19 : 30
        context.saveGState()
        context.setShouldAntialias(false)
        context.setFillColor(eyeColor)
        for x in positions {
            for (y, row) in rows.enumerated() {
                for column in 0..<6 where row & (0b100000 >> column) != 0 {
                    context.fill(CGRect(x: rect.minX + (x + CGFloat(column) + offset.x) * sx,
                                        y: rect.minY + (eyeY + CGFloat(y) + offset.y) * sy, width: sx, height: sy))
                }
            }
        }
        if frame < 9 {
            context.setFillColor(Self.glint)
            for x in positions {
                context.fill(CGRect(x: rect.minX + (x + 1 + offset.x) * sx,
                                    y: rect.minY + (eyeY + 1 + offset.y) * sy, width: sx, height: sy))
            }
        }
        context.restoreGState()
    }

    func portrait(size: NSSize) -> NSImage? {
        let resource = usesDrawnArt ? "drawn-\(String(id.dropFirst(6)))-body.png" : body
        guard let image = bodyImage.map({ NSImage(cgImage: $0, size: NSSize(width: CGFloat($0.width), height: CGFloat($0.height))) }) ?? Bundle.main.url(forResource: resource, withExtension: nil).flatMap({ NSImage(contentsOf: $0) }) else { return nil }
        return NSImage(size: size, flipped: true) { rect in
            guard let context = NSGraphicsContext.current?.cgContext else { return false }
            context.saveGState()
            context.interpolationQuality = .none
            let height = self.isProne ? CGFloat(58) : 104
            let scale = min(rect.width / 100, rect.height / height)
            let bodyRect = CGRect(x: rect.midX - 50 * scale, y: rect.midY - height * scale / 2,
                                  width: 100 * scale, height: height * scale)
            if self.usesDrawnArt { context.translateBy(x: bodyRect.midX * 2, y: 0); context.scaleBy(x: -1, y: 1) }
            image.draw(in: bodyRect, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
            if self.usesDrawnArt {
                if let feetImage = self.drawnLimb("drawn-tricolor-feet") {
                    let feet = NSImage(cgImage: feetImage, size: NSSize(width: feetImage.width, height: feetImage.height))
                    feet.draw(in: CGRect(x: bodyRect.minX + bodyRect.width * 102 / 492,
                        y: bodyRect.minY + bodyRect.height * 221 / 286,
                        width: bodyRect.width * 350 / 492, height: bodyRect.height * 58 / 286),
                        from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
                }
                if let url = Bundle.main.url(forResource: "drawn-tricolor-eyes.png", withExtension: nil),
                   let sheet = NSImage(contentsOf: url), let atlas = sheet.cgImage(forProposedRect: nil, context: nil, hints: nil),
                   let eye = atlas.cropping(to: CGRect(x: 0, y: 0, width: atlas.width / 4, height: atlas.height / 3)) {
                    let eyeImage = NSImage(cgImage: eye, size: NSSize(width: CGFloat(eye.width), height: CGFloat(eye.height)))
                    eyeImage.draw(in: CGRect(x: bodyRect.minX + bodyRect.width * 0.67600677,
                        y: bodyRect.minY + bodyRect.height * 0.26405, width: bodyRect.width * 0.22300079,
                        height: bodyRect.height * 0.31329), from: .zero, operation: .sourceOver,
                        fraction: 1, respectFlipped: true, hints: nil)
                }
            } else { self.drawEyes(in: bodyRect, frame: 0, context: context) }
            context.restoreGState()
            return true
        }
    }
}
