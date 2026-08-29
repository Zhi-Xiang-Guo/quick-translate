import AppKit
import ApplicationServices
import Foundation
import ServiceManagement

private let tripleSpaceInterval: TimeInterval = 0.7

private enum QuickTranslateError: LocalizedError {
    case message(String)

    var errorDescription: String? {
        switch self {
        case .message(let text): return text
        }
    }
}

private struct CodexConfig {
    let providerName: String
    let model: String
    let baseURL: String
    let wireAPI: String
    let apiKey: String

    var endpoint: URL? {
        let suffix = wireAPI.lowercased() == "responses" ? "responses" : "chat/completions"
        let trimmed = baseURL.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if trimmed.lowercased().hasSuffix("/" + suffix) {
            return URL(string: trimmed)
        }
        return URL(string: trimmed + "/" + suffix)
    }
}

private enum CodexConfigLoader {
    static func load() throws -> CodexConfig {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let configURL = home.appendingPathComponent(".codex/config.toml")
        let authURL = home.appendingPathComponent(".codex/auth.json")
        guard FileManager.default.fileExists(atPath: configURL.path) else {
            throw QuickTranslateError.message("CC Switch Codex config was not found")
        }
        guard FileManager.default.fileExists(atPath: authURL.path) else {
            throw QuickTranslateError.message("Codex auth.json was not found")
        }

        let text = try String(contentsOf: configURL, encoding: .utf8)
        var globals: [String: String] = [:]
        var providers: [String: [String: String]] = [:]
        var currentProvider: String?
        var readingGlobals = true

        for rawLine in text.components(separatedBy: .newlines) {
            let line = rawLine.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("[") {
                readingGlobals = false
                if line.hasPrefix("[model_providers."), line.hasSuffix("]") {
                    let start = line.index(line.startIndex, offsetBy: "[model_providers.".count)
                    let end = line.index(before: line.endIndex)
                    currentProvider = String(line[start..<end])
                    if let name = currentProvider, providers[name] == nil {
                        providers[name] = [:]
                    }
                } else {
                    currentProvider = nil
                }
                continue
            }

            guard let pair = parseAssignment(line) else { continue }
            if readingGlobals {
                globals[pair.key] = pair.value
            } else if let name = currentProvider {
                var values = providers[name] ?? [:]
                values[pair.key] = pair.value
                providers[name] = values
            }
        }

        guard let providerID = globals["model_provider"], !providerID.isEmpty else {
            throw QuickTranslateError.message("model_provider is missing from config.toml")
        }
        guard let model = globals["model"], !model.isEmpty else {
            throw QuickTranslateError.message("model is missing from config.toml")
        }
        guard let provider = providers[providerID], let baseURL = provider["base_url"] else {
            throw QuickTranslateError.message("The active provider has no base_url")
        }

        let authData = try Data(contentsOf: authURL)
        guard let auth = try JSONSerialization.jsonObject(with: authData) as? [String: Any],
              let apiKey = auth["OPENAI_API_KEY"] as? String,
              !apiKey.isEmpty else {
            throw QuickTranslateError.message("OPENAI_API_KEY is missing from auth.json")
        }

        let result = CodexConfig(
            providerName: provider["name"] ?? providerID,
            model: model,
            baseURL: baseURL,
            wireAPI: provider["wire_api"] ?? "responses",
            apiKey: apiKey
        )
        guard result.endpoint != nil else {
            throw QuickTranslateError.message("The active provider URL is invalid")
        }
        return result
    }

    private static func parseAssignment(_ line: String) -> (key: String, value: String)? {
        guard !line.isEmpty, !line.hasPrefix("#"), let equals = line.firstIndex(of: "=") else {
            return nil
        }
        let key = String(line[..<equals]).trimmingCharacters(in: .whitespaces)
        var value = line[line.index(after: equals)...].trimmingCharacters(in: .whitespaces)
        guard value.hasPrefix("\"") else { return nil }
        value.removeFirst()
        guard let closingQuote = value.firstIndex(of: "\"") else { return nil }
        let unescaped = String(value[..<closingQuote])
            .replacingOccurrences(of: "\\\"", with: "\"")
            .replacingOccurrences(of: "\\\\", with: "\\")
        return (String(key), unescaped)
    }
}

private final class TranslationClient {
    private let instructions = """
    Translate the user's Chinese text into natural, concise English. Preserve meaning, tone, paragraph breaks, Markdown, names, numbers, URLs, and code fragments. Do not explain, annotate, quote, or wrap the translation. Output only the translated English text.
    """

    func translate(_ source: String, completion: @escaping (Result<String, Error>) -> Void) {
        do {
            let config = try CodexConfigLoader.load()
            guard let endpoint = config.endpoint else {
                throw QuickTranslateError.message("The active provider URL is invalid")
            }

            var body: [String: Any] = ["model": config.model]
            if config.wireAPI.lowercased() == "responses" {
                body["instructions"] = instructions
                body["input"] = source
                body["max_output_tokens"] = 4000
                body["store"] = false
            } else {
                body["messages"] = [
                    ["role": "system", "content": instructions],
                    ["role": "user", "content": source]
                ]
                body["max_tokens"] = 4000
                body["stream"] = false
            }

            var request = URLRequest(url: endpoint)
            request.httpMethod = "POST"
            request.timeoutInterval = 90
            request.setValue("Bearer \(config.apiKey)", forHTTPHeaderField: "Authorization")
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.setValue("application/json", forHTTPHeaderField: "Accept")
            request.httpBody = try JSONSerialization.data(withJSONObject: body)

            URLSession.shared.dataTask(with: request) { data, response, error in
                if let error = error {
                    completion(.failure(error))
                    return
                }
                guard let httpResponse = response as? HTTPURLResponse, let data = data else {
                    completion(.failure(QuickTranslateError.message("The API returned no response")))
                    return
                }
                do {
                    let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] ?? [:]
                    guard (200..<300).contains(httpResponse.statusCode) else {
                        throw QuickTranslateError.message(Self.extractError(json, status: httpResponse.statusCode))
                    }
                    guard let text = Self.extractText(json, wireAPI: config.wireAPI), !text.isEmpty else {
                        throw QuickTranslateError.message("The API response did not contain translated text")
                    }
                    completion(.success(Self.clean(text)))
                } catch {
                    completion(.failure(error))
                }
            }.resume()
        } catch {
            completion(.failure(error))
        }
    }

    private static func extractText(_ json: [String: Any], wireAPI: String) -> String? {
        if let direct = json["output_text"] as? String { return direct }
        if wireAPI.lowercased() == "responses" {
            let output = json["output"] as? [[String: Any]] ?? []
            let parts = output.flatMap { item -> [String] in
                let content = item["content"] as? [[String: Any]] ?? []
                return content.compactMap { $0["text"] as? String }
            }
            return parts.joined()
        }
        guard let choices = json["choices"] as? [[String: Any]],
              let first = choices.first,
              let message = first["message"] as? [String: Any] else { return nil }
        return message["content"] as? String
    }

    private static func extractError(_ json: [String: Any], status: Int) -> String {
        if let error = json["error"] as? [String: Any], let message = error["message"] as? String {
            return "HTTP \(status): \(String(message.prefix(400)))"
        }
        return "HTTP \(status)"
    }

    private static func clean(_ text: String) -> String {
        var value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.hasPrefix("```"), value.hasSuffix("```") {
            let lines = value.components(separatedBy: .newlines)
            if lines.count >= 3 {
                value = lines.dropFirst().dropLast().joined(separator: "\n")
                    .trimmingCharacters(in: .whitespacesAndNewlines)
            }
        }
        return value
    }
}

private struct PasteboardSnapshot {
    private let items: [[NSPasteboard.PasteboardType: Data]]

    init(_ pasteboard: NSPasteboard = .general) {
        items = pasteboard.pasteboardItems?.map { item in
            var values: [NSPasteboard.PasteboardType: Data] = [:]
            for type in item.types {
                if let data = item.data(forType: type) { values[type] = data }
            }
            return values
        } ?? []
    }

    func restore(to pasteboard: NSPasteboard = .general) {
        pasteboard.clearContents()
        let restoredItems = items.map { values -> NSPasteboardItem in
            let item = NSPasteboardItem()
            for (type, data) in values { item.setData(data, forType: type) }
            return item
        }
        if !restoredItems.isEmpty { pasteboard.writeObjects(restoredItems) }
    }
}

private enum AccessibilitySupport {
    static func requestPermission() -> Bool {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }

    static func isEditableElementFocused() -> Bool {
        let systemWide = AXUIElementCreateSystemWide()
        var focusedValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(
            systemWide,
            kAXFocusedUIElementAttribute as CFString,
            &focusedValue
        ) == .success, let focusedValue = focusedValue else { return false }
        let focused = focusedValue as! AXUIElement

        var roleValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(focused, kAXRoleAttribute as CFString, &roleValue) == .success,
              let role = roleValue as? String else { return false }

        var subroleValue: CFTypeRef?
        AXUIElementCopyAttributeValue(focused, kAXSubroleAttribute as CFString, &subroleValue)
        if (subroleValue as? String) == "AXSecureTextField" { return false }

        return ["AXTextField", "AXTextArea", "AXComboBox", "AXSearchField"].contains(role)
    }

    static func canTranslateInFocusedApplication() -> Bool {
        if isEditableElementFocused() { return true }
        guard let application = NSWorkspace.shared.frontmostApplication else { return false }
        let identity = ((application.bundleIdentifier ?? "") + " " + (application.localizedName ?? "")).lowercased()
        return ["wechat", "weixin", "wework", "feishu", "lark", "chatgpt"].contains {
            identity.contains($0)
        }
    }
}

private final class TripleSpaceMonitor {
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var count = 0
    private var lastPress = Date.distantPast
    private var sequencePID: pid_t = 0
    private var suppressNextSpaceUp = false
    private let onTrigger: () -> Void

    init(onTrigger: @escaping () -> Void) {
        self.onTrigger = onTrigger
    }

    func start() throws {
        let mask = (CGEventMask(1) << CGEventType.keyDown.rawValue) |
            (CGEventMask(1) << CGEventType.keyUp.rawValue) |
            (CGEventMask(1) << CGEventType.tapDisabledByTimeout.rawValue) |
            (CGEventMask(1) << CGEventType.tapDisabledByUserInput.rawValue)
        let pointer = Unmanaged.passUnretained(self).toOpaque()
        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: mask,
            callback: quickTranslateEventTap,
            userInfo: pointer
        ) else {
            throw QuickTranslateError.message("Enable Accessibility permission, then reopen QuickTranslate")
        }
        eventTap = tap
        runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), runLoopSource, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }

    func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let eventTap = eventTap { CGEvent.tapEnable(tap: eventTap, enable: true) }
            return Unmanaged.passUnretained(event)
        }

        let keyCode = event.getIntegerValueField(.keyboardEventKeycode)
        let isSpace = keyCode == 49
        if type == .keyUp, isSpace {
            if suppressNextSpaceUp {
                suppressNextSpaceUp = false
                return nil
            }
            return Unmanaged.passUnretained(event)
        }
        guard type == .keyDown else { return Unmanaged.passUnretained(event) }
        if event.getIntegerValueField(.keyboardEventAutorepeat) != 0 {
            return Unmanaged.passUnretained(event)
        }

        if isSpace {
            let now = Date()
            let pid = NSWorkspace.shared.frontmostApplication?.processIdentifier ?? 0
            if now.timeIntervalSince(lastPress) > tripleSpaceInterval || pid != sequencePID {
                count = 1
                sequencePID = pid
            } else {
                count += 1
            }
            lastPress = now
            if count >= 3 {
                count = 0
                sequencePID = 0
                if AccessibilitySupport.canTranslateInFocusedApplication() {
                    suppressNextSpaceUp = true
                    DispatchQueue.main.async { [onTrigger] in onTrigger() }
                    return nil
                }
            }
        } else if ![55, 56, 58, 59, 60, 61, 62].contains(keyCode) {
            count = 0
            sequencePID = 0
        }
        return Unmanaged.passUnretained(event)
    }

    deinit {
        if let source = runLoopSource { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap = eventTap { CFMachPortInvalidate(tap) }
    }
}

private func quickTranslateEventTap(
    proxy: CGEventTapProxy,
    type: CGEventType,
    event: CGEvent,
    userInfo: UnsafeMutableRawPointer?
) -> Unmanaged<CGEvent>? {
    guard let userInfo = userInfo else { return Unmanaged.passUnretained(event) }
    let monitor = Unmanaged<TripleSpaceMonitor>.fromOpaque(userInfo).takeUnretainedValue()
    return monitor.handle(type: type, event: event)
}

private final class TranslationCoordinator {
    var onStatus: ((String, Bool) -> Void)?
    private let client = TranslationClient()
    private var busy = false

    func translateCurrentInput() {
        guard !busy else { return }
        guard AccessibilitySupport.canTranslateInFocusedApplication() else {
            report("Focus an editable input field", error: true)
            return
        }
        busy = true
        let sourcePID = NSWorkspace.shared.frontmostApplication?.processIdentifier ?? 0
        let pasteboard = NSPasteboard.general
        let snapshot = PasteboardSnapshot(pasteboard)
        pasteboard.clearContents()
        let emptyChangeCount = pasteboard.changeCount

        postCommandShortcut(keyCode: 0)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.08) { [weak self] in
            self?.postCommandShortcut(keyCode: 8)
            self?.waitForCopiedText(
                attempt: 0,
                initialChangeCount: emptyChangeCount,
                sourcePID: sourcePID,
                snapshot: snapshot
            )
        }
    }

    func testAPI() {
        guard !busy else { return }
        busy = true
        report("Testing API...", error: false)
        client.translate("你好，世界！") { [weak self] result in
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.busy = false
                switch result {
                case .success(let text): self.report("API OK: \(text)", error: false)
                case .failure(let error): self.report(error.localizedDescription, error: true)
                }
            }
        }
    }

    private func waitForCopiedText(
        attempt: Int,
        initialChangeCount: Int,
        sourcePID: pid_t,
        snapshot: PasteboardSnapshot
    ) {
        let pasteboard = NSPasteboard.general
        if pasteboard.changeCount != initialChangeCount,
           var source = pasteboard.string(forType: .string),
           !source.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            while source.last?.isWhitespace == true { source.removeLast() }
            let containsChinese = source.unicodeScalars.contains {
                (0x3400...0x9FFF).contains(Int($0.value))
            }
            guard containsChinese else {
                snapshot.restore()
                finish("The current input does not contain Chinese text", error: true)
                return
            }
            client.translate(source) { [weak self] result in
                DispatchQueue.main.async {
                    self?.handleTranslation(result, sourcePID: sourcePID, snapshot: snapshot)
                }
            }
            return
        }
        if attempt >= 25 {
            snapshot.restore()
            finish("No text could be read from the current input", error: true)
            return
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.04) { [weak self] in
            self?.waitForCopiedText(
                attempt: attempt + 1,
                initialChangeCount: initialChangeCount,
                sourcePID: sourcePID,
                snapshot: snapshot
            )
        }
    }

    private func handleTranslation(
        _ result: Result<String, Error>,
        sourcePID: pid_t,
        snapshot: PasteboardSnapshot
    ) {
        switch result {
        case .failure(let error):
            snapshot.restore()
            finish(error.localizedDescription, error: true)
        case .success(let translation):
            let pasteboard = NSPasteboard.general
            pasteboard.clearContents()
            pasteboard.setString(translation, forType: .string)
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == sourcePID else {
                finish("Focus changed; translation copied to clipboard", error: true)
                return
            }
            postCommandShortcut(keyCode: 9)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in
                snapshot.restore()
                self?.finish("Ready", error: false)
            }
        }
    }

    private func postCommandShortcut(keyCode: CGKeyCode) {
        guard let source = CGEventSource(stateID: .combinedSessionState),
              let keyDown = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: true),
              let keyUp = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false) else { return }
        keyDown.flags = .maskCommand
        keyUp.flags = .maskCommand
        keyDown.post(tap: .cghidEventTap)
        keyUp.post(tap: .cghidEventTap)
    }

    private func finish(_ text: String, error: Bool) {
        busy = false
        report(text, error: error)
    }

    private func report(_ text: String, error: Bool) {
        onStatus?(text, error)
        if error { NSSound.beep() }
    }
}

private enum StartupManager {
    static func enable() throws {
        if #available(macOS 13.0, *) {
            if SMAppService.mainApp.status == .notRegistered {
                try SMAppService.mainApp.register()
            }
        }
    }
}

private final class QuickTranslateApplication: NSObject, NSApplicationDelegate {
    private let coordinator = TranslationCoordinator()
    private var monitor: TripleSpaceMonitor?
    private var statusItem: NSStatusItem!
    private var statusMenuItem: NSMenuItem!

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        configureMenuBar()
        coordinator.onStatus = { [weak self] text, isError in
            self?.statusMenuItem.title = text
            self?.statusItem.button?.contentTintColor = isError ? .systemRed : nil
        }

        do { try StartupManager.enable() }
        catch { statusMenuItem.title = "Login item: \(error.localizedDescription)" }

        guard AccessibilitySupport.requestPermission() else {
            statusMenuItem.title = "Grant Accessibility permission, then reopen"
            return
        }
        do {
            let monitor = TripleSpaceMonitor { [weak self] in self?.coordinator.translateCurrentInput() }
            try monitor.start()
            self.monitor = monitor
            statusMenuItem.title = "Ready · press Space 3 times"
        } catch {
            statusMenuItem.title = error.localizedDescription
            NSSound.beep()
        }
    }

    private func configureMenuBar() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if #available(macOS 11.0, *) {
            statusItem.button?.image = NSImage(
                systemSymbolName: "character.cursor.ibeam",
                accessibilityDescription: "QuickTranslate"
            )
        } else {
            statusItem.button?.title = "QT"
        }

        let menu = NSMenu()
        statusMenuItem = NSMenuItem(title: "Starting...", action: nil, keyEquivalent: "")
        statusMenuItem.isEnabled = false
        menu.addItem(statusMenuItem)
        menu.addItem(.separator())
        let testItem = NSMenuItem(title: "Test API", action: #selector(testAPI), keyEquivalent: "")
        testItem.target = self
        menu.addItem(testItem)
        menu.addItem(.separator())
        let quitItem = NSMenuItem(title: "Quit QuickTranslate", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)
        statusItem.menu = menu
    }

    @objc private func testAPI() {
        coordinator.testAPI()
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}

private let application = NSApplication.shared
private let applicationDelegate = QuickTranslateApplication()
application.delegate = applicationDelegate
application.run()
