import Foundation

func runWheekPackSmokeTest() throws {
    let fm = FileManager.default
    let root = fm.temporaryDirectory.appendingPathComponent("wheek-smoke-" + UUID().uuidString, isDirectory: true)
    try fm.createDirectory(at: root, withIntermediateDirectories: true)
    defer { try? fm.removeItem(at: root) }
    let id = "test-" + UUID().uuidString.lowercased()
    let skinID = "wheek-" + id
    let source = root.appendingPathComponent("source.wheek")
    let installed = root.appendingPathComponent("installed", isDirectory: true)
    let canonical = installed.appendingPathComponent(id + ".wheek")
    defer { PetSkin.unregisterCustom(id: skinID) }
    func check(_ condition: @autoclosure () -> Bool, _ message: String) throws {
        if !condition() { throw WheekPackError.invalid("外观包测试失败：" + message) }
    }
    func writePack(_ name: String, mode: String = "pixel") throws {
        let manifest = root.appendingPathComponent("manifest.json")
        let data = try JSONSerialization.data(withJSONObject: ["id": id, "name": name, "mode": mode])
        try data.write(to: manifest)
        if fm.fileExists(atPath: source.path) { try fm.removeItem(at: source) }
        let zip = Process()
        zip.executableURL = URL(fileURLWithPath: "/usr/bin/zip")
        zip.currentDirectoryURL = root
        zip.arguments = ["-q", source.path, "manifest.json"]
        try zip.run()
        zip.waitUntilExit()
        try check(zip.terminationStatus == 0, "创建测试包")
    }
    try writePack("First")
    _ = try WheekPackLoader.install(url: source, directory: installed)
    try check(PetSkin.find(skinID)?.name == "First", "安装")
    try writePack("Invalid", mode: "drawn")
    do {
        _ = try WheekPackLoader.install(url: source, directory: installed)
        throw WheekPackError.invalid("无效外观包被接受")
    } catch WheekPackError.invalid(let reason) where reason != "无效外观包被接受" { }
    try check(PetSkin.find(skinID)?.name == "First", "无效包保留已安装皮肤")
    try writePack("Second")
    _ = try WheekPackLoader.install(url: source, directory: installed)
    try check(PetSkin.find(skinID)?.name == "Second", "重装替换")
    PetSkin.unregisterCustom(id: skinID)
    WheekPackLoader.loadInstalled(directory: installed)
    try check(PetSkin.find(skinID)?.name == "Second", "启动加载")
    let foreign = installed.appendingPathComponent("other.wheek")
    try fm.copyItem(at: canonical, to: foreign)
    let removed = try WheekPackLoader.uninstall(id: skinID, directory: installed)
    try check(removed, "卸载")
    WheekPackLoader.loadInstalled(directory: installed)
    try check(PetSkin.find(skinID) == nil && fm.fileExists(atPath: foreign.path), "非规范名称不得加载或删除")
    _ = try WheekPackLoader.install(url: source, directory: installed)
    try fm.removeItem(at: canonical)
    try fm.createDirectory(at: canonical, withIntermediateDirectories: false)
    let sentinel = canonical.appendingPathComponent("keep")
    try Data().write(to: sentinel)
    do {
        _ = try WheekPackLoader.uninstall(id: skinID, directory: installed)
        throw WheekPackError.invalid("同名目录被删除")
    } catch WheekPackError.invalid(let reason) where reason != "同名目录被删除" { }
    try check(fm.fileExists(atPath: sentinel.path) && PetSkin.find(skinID) != nil, "同名目录和注册信息保留")
}
