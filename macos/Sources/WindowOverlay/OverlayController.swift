import AppKit
import CoreMedia
import ScreenCaptureKit

/// Receives frames from ScreenCaptureKit on a background queue.
final class StreamOutput: NSObject, SCStreamOutput, SCStreamDelegate {
    let queue = DispatchQueue(label: "WindowOverlay.capture", qos: .userInteractive)
    // @Sendable: these are called on the capture queue, not the main thread.
    var onFrame: (@Sendable (IOSurfaceRef, CGSize) -> Void)?
    var onStop: (@Sendable (Error) -> Void)?

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, sampleBuffer.isValid,
              let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false)
                as? [[SCStreamFrameInfo: Any]],
              let statusValue = attachments.first?[.status] as? Int,
              SCFrameStatus(rawValue: statusValue) == .complete, // idle/blank frames carry no new image
              let pixelBuffer = sampleBuffer.imageBuffer,
              let surface = CVPixelBufferGetIOSurface(pixelBuffer)?.takeUnretainedValue()
        else { return }
        let size = CGSize(width: CVPixelBufferGetWidth(pixelBuffer), height: CVPixelBufferGetHeight(pixelBuffer))
        onFrame?(surface, size)
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        onStop?(error)
    }
}

/// One overlay: its panel, view and capture stream for a single source window.
@MainActor
final class OverlayController {
    let windowID: CGWindowID
    let panel: OverlayPanel
    let view: OverlayView

    var onSourceLost: (() -> Void)?
    var onClicked: (() -> Void)? { didSet { view.onClicked = onClicked } }
    var otherOverlayFrames: () -> [CGRect] = { [] }
    var snapToEdges = false {
        didSet {
            if snapToEdges {
                view.snapLines = { [weak self] frame in
                    guard let self else { return (xs: [], ys: []) }
                    return self.snapLines(for: frame)
                }
            } else {
                view.snapLines = nil
            }
        }
    }

    private var scWindow: SCWindow
    private var stream: SCStream?
    private let output = StreamOutput()
    private var watchTimer: Timer?
    private var capturedSize: CGSize = .zero
    private var wasOnScreen = true

    init(window: SCWindow, frame: CGRect) {
        scWindow = window
        windowID = window.windowID
        panel = OverlayPanel(frame: frame)
        view = OverlayView(frame: CGRect(origin: .zero, size: frame.size))
        view.autoresizingMask = [.width, .height]
        panel.contentView = view

        output.onFrame = { [weak self] surface, size in
            guard let self else { return }
            Task { @MainActor in self.view.show(surface: surface, size: size) }
        }
        output.onStop = { [weak self] _ in
            guard let self else { return }
            Task { @MainActor in self.stream = nil }
        }
    }

    var frame: CGRect { panel.frame }

    func start() {
        panel.orderFrontRegardless()
        Task { await startCapture() }
        watchTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            guard let self else { return }
            Task { @MainActor in self.watchSource() }
        }
    }

    func stop() {
        watchTimer?.invalidate()
        watchTimer = nil
        if let stream {
            Task { try? await stream.stopCapture() }
        }
        stream = nil
        panel.orderOut(nil)
        panel.close()
    }

    func setLocked(_ locked: Bool) {
        panel.ignoresMouseEvents = locked // click-through
        view.isLocked = locked
    }

    func setOpacity(_ value: Double) { panel.alphaValue = CGFloat(max(0.1, min(1, value))) }
    func setCrop(_ crop: Crop) { view.crop = crop }
    func setSelected(_ selected: Bool) { view.isSelected = selected }

    /// Resize (keeping width and top edge) so the overlay matches the cropped source's aspect ratio.
    func fitToSource() {
        let pixels = view.sourcePixelSize == .zero ? capturedSize : view.sourcePixelSize
        let source = view.croppedSize(of: pixels)
        guard source.width > 0, source.height > 0 else { return }
        let inset: CGFloat = view.isLocked ? 0 : 8
        var frame = panel.frame
        let height = max(OverlayView.minSize.height, (frame.width - inset) * source.height / source.width + inset)
        frame.origin.y = frame.maxY - height
        frame.size.height = height
        panel.setFrame(frame, display: true)
    }

    // MARK: Capture

    private func backingScale() -> CGFloat {
        (panel.screen ?? NSScreen.main)?.backingScaleFactor ?? 2
    }

    private func configuration(for size: CGSize) -> SCStreamConfiguration {
        let config = SCStreamConfiguration()
        let scale = backingScale()
        config.width = max(2, Int(size.width * scale))
        config.height = max(2, Int(size.height * scale))
        config.minimumFrameInterval = CMTime(value: 1, timescale: 60)
        config.pixelFormat = kCVPixelFormatType_32BGRA
        config.showsCursor = false
        config.queueDepth = 5
        return config
    }

    private func startCapture() async {
        capturedSize = scWindow.frame.size
        let filter = SCContentFilter(desktopIndependentWindow: scWindow)
        let stream = SCStream(filter: filter, configuration: configuration(for: capturedSize), delegate: output)
        do {
            try stream.addStreamOutput(output, type: .screen, sampleHandlerQueue: output.queue)
            try await stream.startCapture()
            self.stream = stream
        } catch {
            view.showPlaceholder("Can't capture this window")
        }
    }

    /// Follows the source's size and notices when it's minimised or closed.
    private func watchSource() {
        guard let info = (CGWindowListCopyWindowInfo([.optionIncludingWindow], windowID) as? [[String: Any]])?.first,
              let boundsDictionary = info[kCGWindowBounds as String] as? NSDictionary,
              let bounds = CGRect(dictionaryRepresentation: boundsDictionary as CFDictionary)
        else {
            stop()
            onSourceLost?()
            return
        }

        let onScreen = (info[kCGWindowIsOnscreen as String] as? Bool) ?? false
        if onScreen != wasOnScreen {
            wasOnScreen = onScreen
            if !onScreen { view.showPlaceholder("Source is minimised or on another Space") }
        }

        if bounds.size != capturedSize, bounds.width > 1, bounds.height > 1, let stream {
            capturedSize = bounds.size
            let config = configuration(for: bounds.size)
            Task { try? await stream.updateConfiguration(config) }
        }
    }

    // MARK: Snapping

    private func snapLines(for frame: CGRect) -> (xs: [CGFloat], ys: [CGFloat]) {
        // Screens are read on every drag, so Dock/menu bar changes and display setups are always current.
        let screen = NSScreen.screens.max { a, b in
            area(a.frame.intersection(frame)) < area(b.frame.intersection(frame))
        } ?? NSScreen.main
        guard let screen else { return ([], []) }
        return Snapping.lines(for: frame, screenFrame: screen.frame, visibleFrame: screen.visibleFrame,
                              others: otherOverlayFrames())
    }

    private func area(_ r: CGRect) -> CGFloat { r.isNull ? 0 : r.width * r.height }
}
