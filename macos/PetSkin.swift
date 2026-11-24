import AppKit

struct PetSkin {
    let id: String
    let name: String
    let body: String
    let paw: String
    let networkID: String
    let isDrawn: Bool
    let furColor: CGColor
    let inkColor: CGColor
    private let eyeColor: CGColor

    static let all = [
        PetSkin("tricolor", "像素三花", paw: "tricolor", networkID: "tricolor", darkEyes: true),
        PetSkin("black-white", "像素黑白", paw: "black-white", networkID: "silver-agouti", darkEyes: true),
        PetSkin("golden-white", "像素黄白", paw: "golden-white", networkID: "gold-white"),
        PetSkin("buff", "像素浅金棕", coat: "white", paw: "white", networkID: "gold-white", fur: 0xE6C391),
        PetSkin("cream", "像素奶油", paw: "cream", networkID: "californian", fur: 0xF5DEAF),
        PetSkin("chocolate", "像素巧克力", paw: "chocolate", networkID: "teddy", fur: 0xA17B63, ink: 0x4A3127),
        PetSkin("californian", "像素加州", paw: "tricolor", networkID: "californian"),
        PetSkin("longhair-gold-white", "像素长毛黄白", coat: "longhair-template", paw: "golden-white", networkID: "longhair"),
        PetSkin("longhair-tricolor", "像素长毛三花", paw: "tricolor", networkID: "longhair", darkEyes: true),
        PetSkin("longhair-black-white", "像素长毛黑白", paw: "black-white", networkID: "longhair", darkEyes: true),
        PetSkin("longhair-buff", "像素长毛浅金棕", paw: "white", networkID: "longhair", fur: 0xE6C391),
        PetSkin("longhair-cream", "像素长毛奶油", paw: "cream", networkID: "longhair", fur: 0xF5DEAF),
        PetSkin("longhair-chocolate", "像素长毛巧克力", paw: "chocolate", networkID: "longhair", fur: 0xA17B63, ink: 0x4A3127),
        PetSkin("longhair-californian", "像素长毛加州", paw: "tricolor", networkID: "longhair"),
        PetSkin("drawn-tricolor", "手绘三花", body: "guinea-pig.png", paw: "paw.png", networkID: "tricolor", fur: 0xFAE4CB, ink: 0x513A27, isDrawn: true),
        PetSkin("drawn-californian", "手绘白加州", body: "californian-body.png", paw: "californian-paw.png", networkID: "californian", fur: 0xFAE4CB, ink: 0x513A27, isDrawn: true),
        PetSkin("drawn-gold-white", "手绘黄白英短", body: "gold-white-body.png", paw: "gold-white-paw.png", networkID: "gold-white", fur: 0xFAE4CB, ink: 0x513A27, isDrawn: true),
        PetSkin("drawn-teddy", "手绘美泰", body: "teddy-body.png", paw: "teddy-paw.png", networkID: "teddy", fur: 0xA86946, ink: 0x513A27, isDrawn: true),
        PetSkin("drawn-silver-agouti", "手绘雕灰", body: "silver-agouti-body.png", paw: "silver-agouti-paw.png", networkID: "silver-agouti", fur: 0xA19086, ink: 0x513A27, isDrawn: true),
        PetSkin("drawn-longhair", "手绘长毛猪", body: "longhair-body.png", paw: "longhair-paw.png", networkID: "longhair", fur: 0xFAE4CB, ink: 0x513A27, isDrawn: true)
    ]

    private static let legacyIDs = [
        "tricolor": "pixel-tricolor", "californian": "pixel-californian", "gold-white": "pixel-golden-white",
        "teddy": "pixel-chocolate", "silver-agouti": "pixel-black-white", "longhair": "pixel-longhair-gold-white"
    ]
    private static let gaze = [CGPoint.zero, CGPoint(x: -2, y: 0), CGPoint(x: 2, y: 0),
                               CGPoint(x: 0, y: -2), CGPoint(x: 0, y: 2),
                               CGPoint(x: -2, y: -2), CGPoint(x: 2, y: -2),
                               CGPoint(x: -2, y: 2), CGPoint(x: 2, y: 2)]
    private static let openEye: [UInt8] = [0b011110, 0b111111, 0b111111, 0b111111, 0b111111, 0b011110]
    private static let halfEye: [UInt8] = [0, 0, 0b011110, 0b111111, 0b011110, 0]
    private static let closedEye: [UInt8] = [0, 0, 0b100001, 0b110011, 0b011110, 0]
    private static let glint = color(0xFFF0D5)

    private init(_ id: String, _ name: String, coat: String? = nil, body: String? = nil, paw: String, networkID: String,
                 fur: UInt32 = 0xFFFAF1, ink: UInt32 = 0x624737, darkEyes: Bool = false, isDrawn: Bool = false) {
        self.id = isDrawn ? id : "pixel-" + id
        self.name = name
        self.body = body ?? "guinea-pig-\(coat ?? id).png"
        self.paw = isDrawn ? paw : "paw-\(paw).png"
        self.networkID = networkID
        self.isDrawn = isDrawn
        self.furColor = Self.color(fur)
        self.inkColor = Self.color(ink)
        self.eyeColor = Self.color(darkEyes ? 0x302722 : ink)
    }

    static func find(_ id: String) -> PetSkin? {
        let resolved = legacyIDs[id] ?? id
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
        let sx = rect.width / 100, sy = rect.height / 104
        context.saveGState()
        context.setShouldAntialias(false)
        context.setFillColor(eyeColor)
        for x in [CGFloat(30), CGFloat(65)] {
            for (y, row) in rows.enumerated() {
                for column in 0..<6 where row & (0b100000 >> column) != 0 {
                    context.fill(CGRect(x: rect.minX + (x + CGFloat(column) + offset.x) * sx,
                                        y: rect.minY + (30 + CGFloat(y) + offset.y) * sy, width: sx, height: sy))
                }
            }
        }
        if frame < 9 {
            context.setFillColor(Self.glint)
            for x in [CGFloat(31), CGFloat(66)] {
                context.fill(CGRect(x: rect.minX + (x + offset.x) * sx,
                                    y: rect.minY + (31 + offset.y) * sy, width: sx, height: sy))
            }
        }
        context.restoreGState()
    }

    func portrait(size: NSSize) -> NSImage? {
        guard let url = Bundle.main.url(forResource: body, withExtension: nil), let image = NSImage(contentsOf: url) else { return nil }
        return NSImage(size: size, flipped: true) { rect in
            guard let context = NSGraphicsContext.current?.cgContext else { return false }
            context.saveGState()
            context.interpolationQuality = self.isDrawn ? .high : .none
            if self.isDrawn {
                context.translateBy(x: 2 * rect.midX, y: 0)
                context.scaleBy(x: -1, y: 1)
            }
            image.draw(in: rect, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
            if self.isDrawn {
                let atlasName = self.networkID + "-eyes"
                if let atlasURL = Bundle.main.url(forResource: atlasName, withExtension: "png"),
                   let atlas = NSImage(contentsOf: atlasURL),
                   let cg = atlas.cgImage(forProposedRect: nil, context: nil, hints: nil),
                   let eye = cg.cropping(to: CGRect(x: 0, y: 0, width: cg.width / 4, height: cg.height / 3)) {
                    let eyeRect = CGRect(x: rect.minX + rect.width * 0.58, y: rect.minY + rect.height * 0.25,
                                         width: rect.width * 0.22, height: rect.height * 0.30)
                    context.saveGState()
                    context.translateBy(x: eyeRect.minX, y: eyeRect.maxY)
                    context.scaleBy(x: 1, y: -1)
                    context.draw(eye, in: CGRect(origin: .zero, size: eyeRect.size))
                    context.restoreGState()
                }
            } else {
                self.drawEyes(in: rect, frame: 0, context: context)
            }
            context.restoreGState()
            return true
        }
    }
}
