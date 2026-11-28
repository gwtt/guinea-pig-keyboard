import AppKit
import Carbon
import IOKit.hid

// Match anonymous HID pulses with precise events briefly held in memory.
struct InputPulseGate {
    static let window: TimeInterval = 0.12
    private var pending: [TimeInterval] = []
    private var precise: [TimeInterval] = []
    mutating func reset() { pending.removeAll(); precise.removeAll() }
    mutating func noteHID(at now: TimeInterval) {
        precise.removeAll { now - $0 > Self.window }
        if !precise.isEmpty { precise.removeFirst() } else { pending.append(now) }
    }
    mutating func notePrecise(at now: TimeInterval) {
        if let index = pending.firstIndex(where: { abs(now - $0) <= Self.window }) { pending.remove(at: index) }
        else { precise.append(now) }
        precise.removeAll { now - $0 > Self.window }
    }
    mutating func takeReady(at now: TimeInterval) -> Int {
        precise.removeAll { now - $0 > Self.window }
        let count = pending.prefix { now - $0 >= Self.window }.count
        pending.removeFirst(count)
        return count
    }
}

final class AnonymousKeyboardInput {
    private var manager: IOHIDManager?
    private var gate = InputPulseGate()
    private var timer: Timer?
    var onPulse: (() -> Void)?
    static func canRun(authorized: Bool, secureInput: Bool) -> Bool { authorized && !secureInput }
    static func permitted() -> Bool { canRun(authorized: CGPreflightListenEventAccess(), secureInput: IsSecureEventInputEnabled()) }

    func refresh() {
        guard Self.permitted() else { stop(); return }
        guard manager == nil else { return }
        let hid = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
        let match: [String: Int] = [kIOHIDDeviceUsagePageKey: Int(kHIDPage_GenericDesktop), kIOHIDDeviceUsageKey: Int(kHIDUsage_GD_Keyboard)]
        IOHIDManagerSetDeviceMatching(hid, match as CFDictionary)
        IOHIDManagerRegisterInputValueCallback(hid, { context, result, _, value in
            guard result == kIOReturnSuccess, let context = context else { return }
            let owner = Unmanaged<AnonymousKeyboardInput>.fromOpaque(context).takeUnretainedValue()
            guard AnonymousKeyboardInput.permitted() else { owner.stop(); return }
            let element = IOHIDValueGetElement(value)
            guard IOHIDElementGetUsagePage(element) == UInt32(kHIDPage_KeyboardOrKeypad),
                  IOHIDValueGetIntegerValue(value) > 0 else { return }
            owner.gate.noteHID(at: ProcessInfo.processInfo.systemUptime)
        }, Unmanaged.passUnretained(self).toOpaque())
        IOHIDManagerScheduleWithRunLoop(hid, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
        guard IOHIDManagerOpen(hid, IOOptionBits(kIOHIDOptionsTypeNone)) == kIOReturnSuccess else {
            IOHIDManagerUnscheduleFromRunLoop(hid, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
            return
        }
        manager = hid
        let timer = Timer(timeInterval: 0.03, repeats: true) { [weak self] _ in self?.drain() }
        self.timer = timer
        RunLoop.main.add(timer, forMode: .common)
    }
    func notePrecise() {
        guard manager != nil, Self.permitted() else { stop(); return }
        gate.notePrecise(at: ProcessInfo.processInfo.systemUptime)
    }
    private func drain() {
        guard Self.permitted() else { stop(); return }
        let count = gate.takeReady(at: ProcessInfo.processInfo.systemUptime)
        for _ in 0..<count { onPulse?() }
    }
    func stop() {
        timer?.invalidate(); timer = nil
        if let hid = manager {
            IOHIDManagerUnscheduleFromRunLoop(hid, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
            IOHIDManagerClose(hid, IOOptionBits(kIOHIDOptionsTypeNone))
        }
        manager = nil; gate.reset()
    }
}
