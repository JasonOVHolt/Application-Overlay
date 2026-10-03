import AppKit
import Carbon.HIToolbox

/// A key combination stored as a virtual key code plus Carbon modifier flags.
struct HotkeyCombo: Codable, Equatable {
    var keyCode: UInt32
    var modifiers: UInt32
    var label: String

    static let defaultToggle = HotkeyCombo(keyCode: UInt32(kVK_ANSI_O),
                                           modifiers: UInt32(controlKey | optionKey),
                                           label: "⌃⌥O")

    /// Builds a combo from a key-down event, or nil if it has no Command/Control/Option and isn't
    /// a function key (plain letters would hijack normal typing everywhere).
    init?(event: NSEvent) {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        var carbon: UInt32 = 0
        var symbols = ""
        if flags.contains(.control) { carbon |= UInt32(controlKey); symbols += "⌃" }
        if flags.contains(.option) { carbon |= UInt32(optionKey); symbols += "⌥" }
        if flags.contains(.shift) { carbon |= UInt32(shiftKey); symbols += "⇧" }
        if flags.contains(.command) { carbon |= UInt32(cmdKey); symbols += "⌘" }

        let key = Self.keyName(event)
        let isFunctionKey = key.count > 1 && key.hasPrefix("F") && Int(key.dropFirst()) != nil
        guard carbon & UInt32(controlKey | optionKey | cmdKey) != 0 || isFunctionKey else { return nil }
        self.init(keyCode: UInt32(event.keyCode), modifiers: carbon, label: symbols + key)
    }

    init(keyCode: UInt32, modifiers: UInt32, label: String) {
        self.keyCode = keyCode
        self.modifiers = modifiers
        self.label = label
    }

    private static func keyName(_ event: NSEvent) -> String {
        guard let scalar = event.charactersIgnoringModifiers?.unicodeScalars.first else { return "Key \(event.keyCode)" }
        switch Int(scalar.value) {
        case NSF1FunctionKey...NSF35FunctionKey: return "F\(Int(scalar.value) - NSF1FunctionKey + 1)"
        case 0x20: return "Space"
        case 0x0D, 0x03: return "↩"
        case 0x09: return "⇥"
        case NSUpArrowFunctionKey: return "↑"
        case NSDownArrowFunctionKey: return "↓"
        case NSLeftArrowFunctionKey: return "←"
        case NSRightArrowFunctionKey: return "→"
        default: return String(scalar).uppercased()
        }
    }
}

/// System-wide hotkey via Carbon's RegisterEventHotKey (works without Accessibility permission).
final class HotkeyManager {
    static let shared = HotkeyManager()

    var onPressed: (() -> Void)?
    private var hotKeyRef: EventHotKeyRef?
    private var handlerInstalled = false

    private init() {}

    func installHandler() {
        guard !handlerInstalled else { return }
        handlerInstalled = true
        var eventType = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
            HotkeyManager.shared.onPressed?()
            return noErr
        }, 1, &eventType, nil, nil)
    }

    /// Returns false if the combination couldn't be registered (e.g. already taken).
    @discardableResult
    func register(_ combo: HotkeyCombo?) -> Bool {
        unregister()
        guard let combo else { return true }
        let id = EventHotKeyID(signature: OSType(0x574F_564C), id: 1) // 'WOVL'
        let status = RegisterEventHotKey(combo.keyCode, combo.modifiers, id, GetApplicationEventTarget(), 0, &hotKeyRef)
        return status == noErr
    }

    func unregister() {
        if let hotKeyRef {
            UnregisterEventHotKey(hotKeyRef)
        }
        hotKeyRef = nil
    }
}
