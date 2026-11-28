import AppKit
import Darwin
import Foundation

enum WheekPackError: LocalizedError {
    case invalid(String)
    var errorDescription: String? {
        switch self { case .invalid(let message): return message }
    }
}

enum WheekPackLoader {
    private static let fileLimit = 4 * 1024 * 1024, totalLimit = 12 * 1024 * 1024
    private static let archiveLimit = 13 * 1024 * 1024, manifestLimit = 64 * 1024

    private struct Pack {
        let id: String, name: String, baseCoat: String?, author: String?
        let fur: UInt32?, ink: UInt32?, eye: UInt32?
        let body: Data?, paw: Data?, mouth: Data?
        func register() -> PetSkin {
            PetSkin.registerCustom(id: id, name: name, baseCoat: baseCoat, author: author,
                                   furColor: fur, inkColor: ink, eyeColor: eye,
                                   body: body, paw: paw, mouth: mouth)
        }
    }

    static func loadInstalled(directory: URL) {
        guard let files = try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil) else { return }
        for file in files where file.pathExtension.lowercased() == "wheek" {
            if let data = try? readFile(file), let pack = try? read(data),
               file.lastPathComponent == String(pack.id.dropFirst(6)) + ".wheek" { _ = pack.register() }
        }
    }

    @discardableResult static func install(url: URL, directory: URL) throws -> PetSkin {
        let data = try readFile(url)
        let pack = try read(data)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let destination = directory.appendingPathComponent(String(pack.id.dropFirst(6)) + ".wheek")
        try data.write(to: destination, options: .atomic)
        return pack.register()
    }

    @discardableResult static func uninstall(id: String, directory: URL) throws -> Bool {
        guard id.hasPrefix("wheek-"), validID(String(id.dropFirst(6))),
              let skin = PetSkin.find(id), skin.isCustom else { return false }
        let path = directory.appendingPathComponent(String(id.dropFirst(6)) + ".wheek")
        do {
            let attributes = try FileManager.default.attributesOfItem(atPath: path.path)
            guard attributes[.type] as? FileAttributeType == .typeRegular else {
                throw WheekPackError.invalid("外观包安装文件不是普通文件，无法删除。")
            }
            if unlink(path.path) != 0 && errno != ENOENT {
                throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno))
            }
        } catch let error as NSError where error.domain == NSCocoaErrorDomain && (error.code == NSFileNoSuchFileError || error.code == NSFileReadNoSuchFileError) {
            // A missing installation file can still be removed from the registry.
        }
        PetSkin.unregisterCustom(id: id)
        return true
    }

    private static func validID(_ id: String) -> Bool {
        !id.isEmpty && id.utf8.count <= 64 && id.utf8.allSatisfy { ($0 >= 97 && $0 <= 122) || ($0 >= 48 && $0 <= 57) || $0 == 45 }
    }

    private static func readFile(_ url: URL) throws -> Data {
        guard url.pathExtension.lowercased() == "wheek" else { throw WheekPackError.invalid("请选择 .wheek 外观包。") }
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        let data = try handle.read(upToCount: archiveLimit + 1) ?? Data()
        guard data.count <= archiveLimit else { throw WheekPackError.invalid("外观包超出大小限制。") }
        return data
    }

    private static func read(_ data: Data) throws -> Pack {
        return try data.withUnsafeBytes { bytes in
            guard let archive = archive_read_new() else { throw WheekPackError.invalid("无法读取外观包。") }
            defer { _ = archive_read_free(archive) }
            _ = archive_read_support_format_zip(archive)
            guard let base = bytes.baseAddress, archive_read_open_memory(archive, base, bytes.count) == 0 else { throw WheekPackError.invalid("外观包格式无效。") }
            var entries: [String: Data] = [:], total = 0, count = 0
            var entry: OpaquePointer?
            while true {
                let status = archive_read_next_header(archive, &entry)
                if status == ARCHIVE_EOF { break }
                guard status == ARCHIVE_OK, let item = entry, let path = archive_entry_pathname(item), let name = String(validatingUTF8: path) else { throw WheekPackError.invalid("外观包格式无效。") }
                count += 1
                let size = archive_entry_size(item)
                guard count <= 128, size >= 0, size <= fileLimit, total + Int(size) <= totalLimit else { throw WheekPackError.invalid("外观包超出大小限制。") }
                total += Int(size)
                let key = name.lowercased()
                guard entries[key] == nil else { throw WheekPackError.invalid("外观包内有重名文件。") }
                if key == "manifest.json" && size > manifestLimit { throw WheekPackError.invalid("manifest.json 超出大小限制。") }
                var result = Data(), buffer = [UInt8](repeating: 0, count: 8192)
                while true {
                    let n = buffer.withUnsafeMutableBytes { archive_read_data(archive, $0.baseAddress!, $0.count) }
                    guard n >= 0, result.count + n <= Int(size) else { throw WheekPackError.invalid("外观包解压失败。") }
                    if n == 0 { break }
                    result.append(contentsOf: buffer.prefix(n))
                }
                guard result.count == Int(size) else { throw WheekPackError.invalid("外观包解压长度不一致。") }
                entries[key] = result
            }
            guard let manifest = entries["manifest.json"], let json = String(data: manifest, encoding: .utf8),
                  let fields = try JSONSerialization.jsonObject(with: Data(json.trimmingCharacters(in: CharacterSet(charactersIn: "\u{FEFF}")).utf8)) as? [String: Any] else { throw WheekPackError.invalid("外观包缺少有效的 manifest.json。") }
            func field(_ key: String, _ limit: Int) throws -> String? {
                guard let value = fields[key], !(value is NSNull) else { return nil }
                guard let text = value as? String, text.utf16.count <= limit, text.unicodeScalars.allSatisfy({ !CharacterSet.controlCharacters.contains($0) }) else { throw WheekPackError.invalid("外观包中的 \(key) 无效。") }
                return text
            }
            func color(_ key: String) throws -> UInt32? {
                guard let value = try field(key, 7) else { return nil }
                guard value.utf8.count == 7, value.first == "#", value.utf8.dropFirst().allSatisfy({
                    (48...57).contains($0) || (65...70).contains($0) || (97...102).contains($0)
                }), let rgb = UInt32(value.dropFirst(), radix: 16) else { throw WheekPackError.invalid("外观包中的 \(key) 必须使用 #RRGGBB。") }
                return rgb
            }
            func image(_ name: String) throws -> Data? {
                guard let image = entries[name] else { return nil }
                let signature: [UInt8] = [137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82]
                guard image.count >= 33, image.prefix(16).elementsEqual(signature) else { throw WheekPackError.invalid("\(name) 不是有效的 PNG。") }
                let bytes = [UInt8](image.prefix(24))
                let width = bytes[16..<20].reduce(0) { ($0 << 8) | Int($1) }
                let height = bytes[20..<24].reduce(0) { ($0 << 8) | Int($1) }
                guard width > 0, height > 0, width <= 2048, height <= 2048,
                      let rep = NSBitmapImageRep(data: image), rep.pixelsWide == width, rep.pixelsHigh == height else { throw WheekPackError.invalid("\(name) 不是有效的 PNG。") }
                return image
            }
            guard let rawID = try field("id", 64), validID(rawID.lowercased()), rawID.utf8.allSatisfy({ $0 < 128 }),
                  let name = try field("name", 64), !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw WheekPackError.invalid("外观包需要合法的 id 和非空的 name。") }
            guard (try field("mode", 8) ?? "pixel") == "pixel" else { throw WheekPackError.invalid("仅支持像素外观包。") }
            return try Pack(id: "wheek-" + rawID.lowercased(), name: name.trimmingCharacters(in: .whitespacesAndNewlines),
                            baseCoat: field("baseCoat", 64), author: field("author", 80), fur: color("furColor"),
                            ink: color("inkColor"), eye: color("eyeColor"), body: image("body.png"), paw: image("paw.png"), mouth: image("mouth.png"))
        }
    }
}
