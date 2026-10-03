import AppKit
import ScreenCaptureKit
import SwiftUI

struct SourceWindow: Identifiable, Hashable {
    let id: CGWindowID
    let title: String
    let appName: String
    let icon: NSImage?

    static func == (a: SourceWindow, b: SourceWindow) -> Bool { a.id == b.id }
    func hash(into hasher: inout Hasher) { hasher.combine(id) }
}

@MainActor
final class OverlayItem: Identifiable {
    let id = UUID()
    let number: Int
    let source: SourceWindow
    let controller: OverlayController
    var opacity = 1.0 { didSet { controller.setOpacity(opacity) } }
    var crop = Crop() { didSet { controller.setCrop(crop) } }

    init(number: Int, source: SourceWindow, controller: OverlayController) {
        self.number = number
        self.source = source
        self.controller = controller
    }

    var summary: String {
        "Overlay \(number) · \(Int((opacity * 100).rounded()))% opacity" + (crop.isZero ? "" : " · cropped")
    }
}

enum StatusKind { case idle, live, warning }

@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()

    @Published var windows: [SourceWindow] = []
    @Published var search = ""
    @Published var selectedWindowID: CGWindowID?
    @Published private(set) var overlays: [OverlayItem] = []
    @Published var selectedOverlayID: UUID? { didSet { updateSelectionBorders() } }
    @Published var locked = false { didSet { applyLocked() } }
    @Published var snapToEdges = false { didSet { applySnap() } }
    @Published private(set) var hotkey: HotkeyCombo? = nil
    @Published private(set) var isRecordingHotkey = false
    @Published private(set) var needsPermission = false
    @Published private(set) var status = "Pick a window and press Add overlay."
    @Published private(set) var statusKind = StatusKind.idle

    private var scWindows: [CGWindowID: SCWindow] = [:]
    private var nextNumber = 1
    private var keyMonitor: Any?
    private let defaults = UserDefaults.standard

    private init() {
        let defaults = UserDefaults.standard
        snapToEdges = defaults.bool(forKey: "snapToEdges")
        if let data = defaults.data(forKey: "toggleHotkey") {
            hotkey = try? JSONDecoder().decode(HotkeyCombo?.self, from: data)
        } else {
            hotkey = .defaultToggle
        }
    }

    func start() {
        HotkeyManager.shared.onPressed = { [weak self] in
            guard let self else { return }
            Task { @MainActor in
                if !self.isRecordingHotkey { self.locked.toggle() }
            }
        }
        HotkeyManager.shared.installHandler()
        if !HotkeyManager.shared.register(hotkey) {
            setStatus("\(hotkey?.label ?? "") is already in use. Pick another hotkey or use the switch.", .warning)
        }
        Task { await refreshWindows() }
    }

    var filteredWindows: [SourceWindow] {
        let text = search.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty else { return windows }
        return windows.filter { $0.title.localizedCaseInsensitiveContains(text) || $0.appName.localizedCaseInsensitiveContains(text) }
    }

    var selectedOverlay: OverlayItem? { overlays.first { $0.id == selectedOverlayID } }

    func liveCount(for windowID: CGWindowID) -> Int { overlays.filter { $0.source.id == windowID }.count }

    // MARK: Windows

    func refreshWindows() async {
        if !CGPreflightScreenCaptureAccess() {
            CGRequestScreenCaptureAccess() // shows the system prompt the first time
        }
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: false)
            let ownPID = ProcessInfo.processInfo.processIdentifier
            let usable = content.windows.filter { w in
                w.windowLayer == 0 && !(w.title ?? "").isEmpty && w.owningApplication?.processID != ownPID
                    && w.frame.width > 40 && w.frame.height > 40
            }
            scWindows = Dictionary(usable.map { ($0.windowID, $0) }, uniquingKeysWith: { a, _ in a })
            windows = usable.map { w in
                let pid = w.owningApplication?.processID ?? 0
                return SourceWindow(id: w.windowID, title: w.title ?? "",
                                    appName: w.owningApplication?.applicationName ?? "?",
                                    icon: NSRunningApplication(processIdentifier: pid)?.icon)
            }
            needsPermission = false
        } catch {
            needsPermission = true
            setStatus("Screen Recording permission is needed to see other windows.", .warning)
        }
    }

    func openScreenRecordingSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    // MARK: Overlays

    func addOverlay(windowID: CGWindowID? = nil) {
        guard let id = windowID ?? selectedWindowID, let source = windows.first(where: { $0.id == id }) else {
            setStatus("Select a window first.", .warning)
            return
        }
        guard let scWindow = scWindows[id] else {
            setStatus("That window is no longer available.", .warning)
            Task { await refreshWindows() }
            return
        }

        // Default placement: bottom-right of the main window's screen, cascading new overlays.
        let screen = NSApp.keyWindow?.screen ?? NSScreen.main
        let area = screen?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1280, height: 800)
        let width = area.width / 3
        let aspect = scWindow.frame.height / max(1, scWindow.frame.width)
        let height = max(OverlayView.minSize.height, min(area.height / 2, width * aspect))
        let margin = 24 + CGFloat(overlays.count % 6) * 36
        let frame = CGRect(x: area.maxX - width - margin, y: area.minY + margin, width: width, height: height)

        let controller = OverlayController(window: scWindow, frame: frame)
        let item = OverlayItem(number: nextNumber, source: source, controller: controller)
        nextNumber += 1
        controller.setLocked(locked)
        controller.snapToEdges = snapToEdges
        controller.otherOverlayFrames = { [weak self, weak controller] in
            guard let self else { return [] }
            return self.overlays.filter { $0.controller !== controller }.map { $0.controller.frame }
        }
        controller.onClicked = { [weak self, weak item] in
            guard let self, let item else { return }
            self.selectedOverlayID = item.id
        }
        controller.onSourceLost = { [weak self, weak item] in
            guard let self, let item else { return }
            self.remove(item, closeController: false)
            self.setStatus("Overlay \(item.number) closed because its window (\(item.source.title)) was closed.", .warning)
            Task { await self.refreshWindows() }
        }
        controller.start()

        overlays.append(item)
        selectedOverlayID = item.id
        updateRunningStatus()
    }

    func remove(_ item: OverlayItem, closeController: Bool = true) {
        guard let index = overlays.firstIndex(where: { $0.id == item.id }) else { return }
        if closeController { item.controller.stop() }
        overlays.remove(at: index)
        if selectedOverlayID == item.id {
            selectedOverlayID = overlays.isEmpty ? nil : overlays[min(index, overlays.count - 1)].id
        }
        updateRunningStatus()
    }

    func removeSelected() {
        if let item = selectedOverlay { remove(item) }
    }

    func stopAll() {
        for item in overlays { item.controller.stop() }
        overlays.removeAll()
        selectedOverlayID = nil
        locked = false
        updateRunningStatus()
    }

    func fitSelected() { selectedOverlay?.controller.fitToSource() }

    // Bindings to the selected overlay's settings (no-ops when nothing is selected).

    var opacityBinding: Binding<Double> {
        Binding(get: { self.selectedOverlay?.opacity ?? 1 },
                set: { value in self.updateSelected { $0.opacity = value } })
    }

    func cropBinding(_ side: WritableKeyPath<Crop, Double>) -> Binding<Double> {
        Binding(get: { self.selectedOverlay?.crop[keyPath: side] ?? 0 },
                set: { value in self.updateSelected { $0.crop[keyPath: side] = value } })
    }

    func resetCrop() { updateSelected { $0.crop = Crop() } }

    private func updateSelected(_ change: (OverlayItem) -> Void) {
        guard let item = selectedOverlay else { return }
        objectWillChange.send()
        change(item)
    }

    private func applyLocked() {
        for item in overlays { item.controller.setLocked(locked) }
        updateRunningStatus()
    }

    private func applySnap() {
        defaults.set(snapToEdges, forKey: "snapToEdges")
        for item in overlays { item.controller.snapToEdges = snapToEdges }
    }

    private func updateSelectionBorders() {
        for item in overlays { item.controller.setSelected(item.id == selectedOverlayID) }
    }

    private func updateRunningStatus() {
        if overlays.isEmpty {
            setStatus("No overlays running.")
        } else {
            let count = overlays.count
            setStatus("\(count) overlay\(count == 1 ? "" : "s") running · "
                      + (locked ? "locked (click-through)" : "unlocked: drag to move, drag edges to resize"), .live)
        }
    }

    func setStatus(_ text: String, _ kind: StatusKind = .idle) {
        status = text
        statusKind = kind
    }

    // MARK: Hotkey recording

    var hotkeyLabel: String { isRecordingHotkey ? "Press keys…" : (hotkey?.label ?? "None") }

    func startRecordingHotkey() {
        guard !isRecordingHotkey else { return }
        isRecordingHotkey = true
        HotkeyManager.shared.unregister() // so pressing the current combination is captured here
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self else { return event }
            self.handleRecordingKey(event)
            return nil // swallow the key
        }
    }

    private func handleRecordingKey(_ event: NSEvent) {
        let plain = event.modifierFlags.intersection([.command, .control, .option, .shift]).isEmpty
        if plain && event.keyCode == UInt16(kVK_Escape) {
            finishRecording(nil, changed: false)
        } else if plain && (event.keyCode == UInt16(kVK_Delete) || event.keyCode == UInt16(kVK_ForwardDelete)) {
            finishRecording(nil, changed: true)
        } else if let combo = HotkeyCombo(event: event) {
            finishRecording(combo, changed: true)
        } else {
            setStatus("Add ⌘, ⌃ or ⌥ (or use an F-key).", .warning)
        }
    }

    private func finishRecording(_ combo: HotkeyCombo?, changed: Bool) {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
        isRecordingHotkey = false
        guard changed else {
            HotkeyManager.shared.register(hotkey)
            return
        }
        if HotkeyManager.shared.register(combo) {
            hotkey = combo
            defaults.set(try? JSONEncoder().encode(combo), forKey: "toggleHotkey")
            setStatus(combo.map { "Toggle hotkey set to \($0.label)." } ?? "Toggle hotkey cleared.")
        } else {
            setStatus("\(combo?.label ?? "") is already in use. Kept \(hotkey?.label ?? "None").", .warning)
            HotkeyManager.shared.register(hotkey)
        }
    }
}
