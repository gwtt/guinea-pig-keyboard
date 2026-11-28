import AppKit
import CoreGraphics
import Darwin

struct CursorTheme {
    struct RoleSpec {
        let role: String
        let systemCursorName: String
        let hotspotX64: CGFloat
        let hotspotY64: CGFloat
    }

    let id: String
    let name: String
    let summary: String
    let roles: [RoleSpec]

    var isSystem: Bool { id == "system" }

    // Window edges and AppKit dividers have separate bidirectional cursor IDs.
    private static let resizeRoles: [RoleSpec] = [
        ("resize-ns", [32, 23]),
        ("resize-we", [28, 19]),
        ("resize-nwse", [34]),
        ("resize-nesw", [30]),
    ].flatMap { role, ids in
        ids.map { RoleSpec(role: role, systemCursorName: "com.apple.cursor.\($0)", hotspotX64: 32, hotspotY64: 32) }
    }

    static let systemDefault = CursorTheme(id: "system", name: "系统默认", summary: "恢复系统原生指针", roles: [])

    static let all: [CursorTheme] = [
        systemDefault,
        CursorTheme(
            id: "tricolor",
            name: "奶油三花",
            summary: "极简办公 · 锐利低遮挡",
            roles: [
                RoleSpec(role: "arrow", systemCursorName: "com.apple.coregraphics.Arrow", hotspotX64: 6, hotspotY64: 4),
                RoleSpec(role: "hand", systemCursorName: "com.apple.cursor.13", hotspotX64: 24, hotspotY64: 6),
                RoleSpec(role: "ibeam", systemCursorName: "com.apple.coregraphics.IBeam", hotspotX64: 32, hotspotY64: 32),
                RoleSpec(role: "busy", systemCursorName: "com.apple.coregraphics.Wait", hotspotX64: 32, hotspotY64: 32),
            ] + resizeRoles
        ),
        CursorTheme(
            id: "carrot",
            name: "牧草胡萝卜",
            summary: "清爽食物 · 天然尖头",
            roles: [
                RoleSpec(role: "arrow", systemCursorName: "com.apple.coregraphics.Arrow", hotspotX64: 6, hotspotY64: 6),
                RoleSpec(role: "hand", systemCursorName: "com.apple.cursor.13", hotspotX64: 20, hotspotY64: 8),
                RoleSpec(role: "ibeam", systemCursorName: "com.apple.coregraphics.IBeam", hotspotX64: 32, hotspotY64: 32),
                RoleSpec(role: "busy", systemCursorName: "com.apple.coregraphics.Wait", hotspotX64: 32, hotspotY64: 32),
            ] + resizeRoles
        ),
        CursorTheme(
            id: "pixel",
            name: "复古像素猪",
            summary: "桌宠同款 · 锐利像素风",
            roles: [
                RoleSpec(role: "arrow", systemCursorName: "com.apple.coregraphics.Arrow", hotspotX64: 4, hotspotY64: 4),
                RoleSpec(role: "hand", systemCursorName: "com.apple.cursor.13", hotspotX64: 22, hotspotY64: 10),
                RoleSpec(role: "ibeam", systemCursorName: "com.apple.coregraphics.IBeam", hotspotX64: 32, hotspotY64: 32),
                RoleSpec(role: "busy", systemCursorName: "com.apple.coregraphics.Wait", hotspotX64: 32, hotspotY64: 32),
            ] + resizeRoles
        ),
        CursorTheme(
            id: "piggy-pixel",
            name: "三花猪猪",
            summary: "像素三花 · 手绘指针",
            roles: [
                RoleSpec(role: "arrow", systemCursorName: "com.apple.coregraphics.Arrow", hotspotX64: 4, hotspotY64: 4),
                RoleSpec(role: "hand", systemCursorName: "com.apple.cursor.13", hotspotX64: 2, hotspotY64: 14),
                RoleSpec(role: "ibeam", systemCursorName: "com.apple.coregraphics.IBeam", hotspotX64: 20, hotspotY64: 32),
                RoleSpec(role: "busy", systemCursorName: "com.apple.coregraphics.Wait", hotspotX64: 50, hotspotY64: 28),
            ] + resizeRoles
        ),
    ]

    static func named(_ id: String?) -> CursorTheme {
        guard let id else { return systemDefault }
        return all.first { $0.id == id } ?? systemDefault
    }

    static func previewImage(for themeID: String) -> NSImage? {
        guard themeID != "system",
              let url = Bundle.main.url(forResource: "cursor-\(themeID)-arrow", withExtension: "png") else {
            return nil
        }
        let image = NSImage(contentsOf: url)
        image?.size = NSSize(width: 16, height: 16)
        return image
    }
}

enum CursorThemeApplier {
    private typealias CGSMainConnectionIDFn = @convention(c) () -> Int32
    private typealias CGSRegisterCursorWithImagesFn = @convention(c) (
        Int32,
        UnsafePointer<CChar>,
        Bool,
        Bool,
        Bool,
        CGSize,
        CGPoint,
        Int32,
        Double,
        CFArray,
        UnsafeMutablePointer<UInt32>?
    ) -> CGError
    private typealias CGSRemoveRegisteredCursorFn = @convention(c) (
        Int32,
        UnsafePointer<CChar>,
        Bool
    ) -> CGError

    private static let managedCursorNames = Set(CursorTheme.all.flatMap { $0.roles.map { $0.systemCursorName } })

    private static let cgHandle: UnsafeMutableRawPointer? = {
        dlopen("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", RTLD_LAZY)
    }()

    private static func loadSymbol<T>(_ name: String, as type: T.Type) -> T? {
        guard let handle = cgHandle, let sym = dlsym(handle, name) else { return nil }
        return unsafeBitCast(sym, to: type)
    }

    @discardableResult
    static func apply(_ theme: CursorTheme) -> (ok: Bool, message: String) {
        guard let mainConnFn = loadSymbol("CGSMainConnectionID", as: CGSMainConnectionIDFn.self) else {
            return (false, "当前 macOS 版本暂不支持直接替换系统指针")
        }
        let cid = mainConnFn()

        if theme.isSystem {
            guard let removeFn = loadSymbol("CGSRemoveRegisteredCursor", as: CGSRemoveRegisteredCursorFn.self) else {
                return (false, "无法调用系统指针恢复接口，注销或重启后将自动还原")
            }
            for name in managedCursorNames {
                name.withCString { _ = removeFn(cid, $0, true) }
            }
            return (true, "已恢复系统默认指针")
        }

        guard let registerFn = loadSymbol("CGSRegisterCursorWithImages", as: CGSRegisterCursorWithImagesFn.self) else {
            return (false, "当前 macOS 版本暂不支持直接替换系统指针")
        }

        var appliedCount = 0
        for spec in theme.roles {
            guard let url = Bundle.main.url(forResource: "cursor-\(theme.id)-\(spec.role)", withExtension: "png"),
                  let image = NSImage(contentsOf: url),
                  let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
                continue
            }
            let size = CGSize(width: 32, height: 32)
            let hotspot = CGPoint(x: spec.hotspotX64 / 2.0, y: spec.hotspotY64 / 2.0)
            let images = [cgImage] as CFArray
            var seed: UInt32 = 0
            let err = spec.systemCursorName.withCString { cName in
                registerFn(cid, cName, true, true, false, size, hotspot, 1, 0.0, images, &seed)
            }
            if err == .success {
                appliedCount += 1
            }
        }

        if appliedCount == 0 {
            return (false, "应用「\(theme.name)」失败，当前系统限制了指针替换")
        }
        return (true, "已应用「\(theme.name)」· 可随时切回系统默认")
    }
}
