import SwiftUI

private extension View {
    func card() -> some View {
        padding(14)
            .background(RoundedRectangle(cornerRadius: 10).fill(Color(white: 0.135)))
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.white.opacity(0.09)))
    }
}

@MainActor
struct ContentView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Window Overlay").font(.system(size: 21, weight: .semibold))
                Text("Mirror any windows into always-on-top overlays that clicks pass straight through.")
                    .foregroundStyle(.secondary)
            }
            if model.needsPermission { permissionCard }
            HStack(alignment: .top, spacing: 14) {
                windowsCard
                VStack(spacing: 14) {
                    overlaysCard
                    settingsCard
                }
                .frame(width: 380)
            }
            statusBar
        }
        .padding(20)
        .background(Color(white: 0.095))
        .preferredColorScheme(.dark)
    }

    // MARK: Cards

    private var permissionCard: some View {
        HStack {
            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.yellow)
            Text("Allow Window Overlay under System Settings › Privacy & Security › Screen Recording, then press Try again. "
                 + "You may need to quit and reopen the app after allowing it.")
                .fixedSize(horizontal: false, vertical: true)
            Spacer()
            Button("Open Settings") { model.openScreenRecordingSettings() }
            Button("Try again") { Task { await model.refreshWindows() } }
        }
        .card()
    }

    private var windowsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                Text("Windows").font(.headline)
                HStack {
                    Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                    TextField("Search windows", text: $model.search)
                        .textFieldStyle(.plain)
                        .onSubmit { model.addOverlay(windowID: model.selectedWindowID ?? model.filteredWindows.first?.id) }
                }
                .padding(6)
                .background(RoundedRectangle(cornerRadius: 6).fill(Color(white: 0.11)))
                Button { Task { await model.refreshWindows() } } label: { Image(systemName: "arrow.clockwise") }
                    .help("Refresh window list")
            }
            List(selection: $model.selectedWindowID) {
                ForEach(model.filteredWindows) { window in
                    WindowRow(window: window, liveCount: model.liveCount(for: window.id)).tag(window.id)
                }
            }
            .scrollContentBackground(.hidden)
            // Double-click or Return adds an overlay.
            .contextMenu(forSelectionType: CGWindowID.self, menu: { _ in }) { ids in
                if let id = ids.first { model.addOverlay(windowID: id) }
            }
            Text("Double-click a window (or press Return) to add an overlay. Add the same window more than once to show "
                 + "different cropped parts of it. Sources can be covered by other windows but must not be minimised.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .card()
    }

    private var overlaysCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Overlays").font(.headline)
            HStack {
                Button { model.addOverlay() } label: {
                    Label("Add overlay", systemImage: "plus").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                Button { model.stopAll() } label: {
                    Label("Stop all", systemImage: "stop.fill").frame(maxWidth: .infinity)
                }
                .disabled(model.overlays.isEmpty)
            }
            .controlSize(.large)

            Group {
                if model.overlays.isEmpty {
                    Text("No overlays yet.\nPick a window and press Add overlay.")
                        .font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    List(selection: $model.selectedOverlayID) {
                        ForEach(model.overlays) { item in
                            OverlayRow(item: item) { model.remove(item) }.tag(item.id)
                        }
                    }
                    .scrollContentBackground(.hidden)
                    .onDeleteCommand { model.removeSelected() }
                }
            }
            .frame(height: 150)

            Toggle(isOn: $model.locked) {
                VStack(alignment: .leading) {
                    Text("Overlay mode")
                    Text(model.hotkey.map { "Lock all, click-through · \($0.label)" }
                         ?? "Lock all overlays and let clicks pass through")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            .toggleStyle(.switch)

            HStack {
                Text("Toggle hotkey")
                Spacer()
                Button(model.hotkeyLabel) { model.startRecordingHotkey() }
                    .help("Click, then press keys. Esc cancels, Delete clears.")
                    .frame(minWidth: 140)
            }

            Toggle(isOn: $model.snapToEdges) {
                VStack(alignment: .leading) {
                    Text("Snap to edges")
                    Text("Overlays stick to screen edges and to each other").font(.caption).foregroundStyle(.secondary)
                }
            }
            .toggleStyle(.switch)
        }
        .card()
    }

    private var settingsCard: some View {
        let item = model.selectedOverlay
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Overlay settings").font(.headline)
                Spacer()
                Button { model.fitSelected() } label: { Image(systemName: "arrow.up.left.and.arrow.down.right") }
                    .help("Fit overlay to the source's shape")
                Button { model.removeSelected() } label: { Image(systemName: "trash") }
                    .help("Close this overlay")
            }
            .buttonStyle(.borderless)
            Text(item.map { "Overlay \($0.number) · \($0.source.title)" } ?? "Select an overlay to change its settings.")
                .font(.caption).foregroundStyle(.secondary).lineLimit(1)

            Group {
                HStack {
                    Text("Opacity")
                    Spacer()
                    Text("\(Int((model.opacityBinding.wrappedValue * 100).rounded()))%").bold()
                }
                Slider(value: model.opacityBinding, in: 0.1...1)

                HStack {
                    Text("Crop")
                    Spacer()
                    Button("Reset") { model.resetCrop() }
                        .buttonStyle(.borderless)
                }
                Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 6) {
                    GridRow {
                        cropSlider("Left", \.left)
                        cropSlider("Right", \.right)
                    }
                    GridRow {
                        cropSlider("Top", \.top)
                        cropSlider("Bottom", \.bottom)
                    }
                }
                Text("macOS has no separate \"client area\": use Crop › Top to hide a window's title bar.")
                    .font(.caption2).foregroundStyle(.secondary)
            }
            .disabled(item == nil)
        }
        .card()
    }

    @ViewBuilder
    private func cropSlider(_ name: String, _ side: WritableKeyPath<Crop, Double>) -> some View {
        let binding = model.cropBinding(side)
        Text(name).foregroundStyle(.secondary).frame(width: 46, alignment: .leading)
        Slider(value: binding, in: 0...AppModel.maxCrop)
        Text("\(Int((binding.wrappedValue * 100).rounded()))%").font(.caption).monospacedDigit()
            .frame(width: 34, alignment: .trailing)
    }

    private var statusBar: some View {
        HStack(spacing: 8) {
            Circle()
                .fill(model.statusKind == .live ? Color.green : model.statusKind == .warning ? Color.orange : Color.gray)
                .frame(width: 8, height: 8)
            Text(model.status).foregroundStyle(model.statusKind == .idle ? Color.secondary : Color.primary).lineLimit(1)
        }
    }
}

@MainActor
private struct WindowRow: View {
    let window: SourceWindow
    let liveCount: Int

    var body: some View {
        HStack(spacing: 12) {
            AppIcon(image: window.icon, size: 24)
            VStack(alignment: .leading, spacing: 1) {
                Text(window.title).fontWeight(.semibold).lineLimit(1)
                Text(window.appName).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            if liveCount > 0 {
                Text(liveCount > 1 ? "LIVE ×\(liveCount)" : "LIVE")
                    .font(.caption.bold())
                    .foregroundStyle(Color.green)
                    .padding(.horizontal, 8).padding(.vertical, 2)
                    .background(Capsule().fill(Color.green.opacity(0.16)))
            }
        }
        .padding(.vertical, 4)
    }
}

@MainActor
private struct OverlayRow: View {
    let item: OverlayItem
    let onRemove: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            AppIcon(image: item.source.icon, size: 20)
            VStack(alignment: .leading, spacing: 1) {
                Text(item.source.title).fontWeight(.semibold).lineLimit(1)
                Text(item.summary).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            Button(action: onRemove) { Image(systemName: "xmark") }
                .buttonStyle(.borderless)
                .help("Close this overlay")
        }
        .padding(.vertical, 2)
    }
}

private struct AppIcon: View {
    let image: NSImage?
    let size: CGFloat

    var body: some View {
        if let image {
            Image(nsImage: image).resizable().frame(width: size, height: size)
        } else {
            Image(systemName: "macwindow").frame(width: size, height: size).foregroundStyle(.secondary)
        }
    }
}
