import Foundation
import QuickTranslateCore

private enum TestFailure: Error, CustomStringConvertible {
    case assertion(String)

    var description: String {
        switch self {
        case .assertion(let message): return message
        }
    }
}

private func expect(_ condition: @autoclosure () -> Bool, _ message: String) throws {
    guard condition() else { throw TestFailure.assertion(message) }
}

@main
struct OfficialCodexTranslatorTests {
    static func main() async throws {
        try detectsOfficialClientRestrictionInMixedJSONAndSSEBody()
        try requiresBothAccessibilityAndInputMonitoringForTheKeyboardMonitor()
        try requiresThreeSeparateSpacePressCycles()
        try ignoresDuplicateSpaceKeyDownEvents()
        try resetsTheSpaceSequenceAfterTimeout()
        try allowsPasteWhenBundleIdentifierMatchesAfterPIDChange()
        try blocksPasteWhenFrontmostApplicationDiffers()
        try await runsOfficialCodexWithIsolatedTranslationArgumentsAndStdin()
        print("QuickTranslateCoreTests passed")
    }

    static func detectsOfficialClientRestrictionInMixedJSONAndSSEBody() throws {
        let body = Data("""
        {"error":{"message":"This account only allows Codex official clients"}}event: response.failed
        """.utf8)

        try expect(
            DirectResponsePolicy.requiresOfficialCodexClient(statusCode: 403, data: body),
            "403 official-client restriction should trigger the fallback"
        )
        try expect(
            !DirectResponsePolicy.requiresOfficialCodexClient(statusCode: 401, data: body),
            "non-403 responses must not trigger the fallback"
        )
    }

    static func requiresBothAccessibilityAndInputMonitoringForTheKeyboardMonitor() throws {
        try expect(
            MonitorPermissionState.evaluate(
                accessibilityTrusted: true,
                inputMonitoringTrusted: true
            ) == .ready,
            "both permissions should allow the keyboard monitor to start"
        )
        try expect(
            MonitorPermissionState.evaluate(
                accessibilityTrusted: false,
                inputMonitoringTrusted: true
            ) == .waitingForAccessibility,
            "missing Accessibility should keep the monitor stopped"
        )
        try expect(
            MonitorPermissionState.evaluate(
                accessibilityTrusted: true,
                inputMonitoringTrusted: false
            ) == .waitingForInputMonitoring,
            "missing Input Monitoring should keep the monitor stopped"
        )
    }

    static func requiresThreeSeparateSpacePressCycles() throws {
        var sequence = TripleSpaceSequence(maxInterval: 0.7)

        try expect(
            !sequence.handleKeyDown(at: 1.0, processID: 42, isRepeat: false),
            "the first physical Space press must not trigger"
        )
        sequence.handleKeyUp()
        try expect(
            !sequence.handleKeyDown(at: 1.1, processID: 42, isRepeat: false),
            "two physical Space presses must not trigger"
        )
        sequence.handleKeyUp()
        try expect(
            sequence.handleKeyDown(at: 1.2, processID: 42, isRepeat: false),
            "the third physical Space press should trigger"
        )
    }

    static func ignoresDuplicateSpaceKeyDownEvents() throws {
        var sequence = TripleSpaceSequence(maxInterval: 0.7)

        try expect(
            !sequence.handleKeyDown(at: 2.0, processID: 42, isRepeat: false),
            "the first keyDown must not trigger"
        )
        try expect(
            !sequence.handleKeyDown(at: 2.01, processID: 42, isRepeat: false),
            "a duplicate keyDown before keyUp must not count as another press"
        )
        sequence.handleKeyUp()
        try expect(
            !sequence.handleKeyDown(at: 2.1, processID: 42, isRepeat: false),
            "the second physical press must not trigger after a duplicate keyDown"
        )
        sequence.handleKeyUp()
        try expect(
            sequence.handleKeyDown(at: 2.2, processID: 42, isRepeat: false),
            "the third physical press should still trigger"
        )
    }

    static func resetsTheSpaceSequenceAfterTimeout() throws {
        var sequence = TripleSpaceSequence(maxInterval: 0.7)

        try expect(
            !sequence.handleKeyDown(at: 3.0, processID: 42, isRepeat: false),
            "the initial Space press must not trigger"
        )
        sequence.handleKeyUp()
        try expect(
            !sequence.handleKeyDown(at: 4.0, processID: 42, isRepeat: false),
            "a press after the timeout should start a new sequence"
        )
        sequence.handleKeyUp()
        try expect(
            !sequence.handleKeyDown(at: 4.1, processID: 42, isRepeat: false),
            "the second press in the new sequence must not trigger"
        )
        sequence.handleKeyUp()
        try expect(
            sequence.handleKeyDown(at: 4.2, processID: 42, isRepeat: false),
            "the third press in the new sequence should trigger"
        )
    }

    static func allowsPasteWhenBundleIdentifierMatchesAfterPIDChange() throws {
        try expect(
            PasteTargetPolicy.shouldPaste(
                sourceProcessID: 100,
                sourceBundleIdentifier: "com.electron.lark",
                currentProcessID: 200,
                currentBundleIdentifier: "com.electron.lark"
            ),
            "the translated text should return to the same app after its process ID changes"
        )
    }

    static func blocksPasteWhenFrontmostApplicationDiffers() throws {
        try expect(
            !PasteTargetPolicy.shouldPaste(
                sourceProcessID: 100,
                sourceBundleIdentifier: "com.tencent.xinWeChat",
                currentProcessID: 200,
                currentBundleIdentifier: "com.electron.lark"
            ),
            "translation must not paste into a different frontmost application"
        )
    }

    static func runsOfficialCodexWithIsolatedTranslationArgumentsAndStdin() async throws {
        let fileManager = FileManager.default
        let directory = fileManager.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? fileManager.removeItem(at: directory) }

        let executable = directory.appendingPathComponent("fake-codex")
        let argumentsFile = directory.appendingPathComponent("arguments.txt")
        let stdinFile = directory.appendingPathComponent("stdin.txt")
        let script = """
        #!/bin/sh
        printf '%s\\n' "$@" > "$QT_TEST_ARGUMENTS_FILE"
        cat > "$QT_TEST_STDIN_FILE"
        printf 'Hello, world!\\n'
        """
        try Data(script.utf8).write(to: executable)
        try fileManager.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)

        setenv("QT_TEST_ARGUMENTS_FILE", argumentsFile.path, 1)
        setenv("QT_TEST_STDIN_FILE", stdinFile.path, 1)
        defer {
            unsetenv("QT_TEST_ARGUMENTS_FILE")
            unsetenv("QT_TEST_STDIN_FILE")
        }

        let translator = OfficialCodexTranslator(
            executableURL: executable,
            workingDirectoryURL: directory
        )
        let translation = try await translator.translate("你好，世界！")

        try expect(translation == "Hello, world!", "translator should return trimmed stdout")
        let arguments = try String(contentsOf: argumentsFile, encoding: .utf8)
            .split(separator: "\n")
            .map(String.init)
        try expect(arguments == [
            "exec",
            "--ephemeral",
            "--ignore-rules",
            "--skip-git-repo-check",
            "--sandbox",
            "read-only",
            "--model",
            "gpt-5.6-luna",
            "-c",
            "model_reasoning_effort=\"low\"",
            "-c",
            "mcp_servers={}",
            "-c",
            "plugins={}",
            "-C",
            directory.path,
            "-"
        ], "translator should isolate the Codex invocation")

        let prompt = try String(contentsOf: stdinFile, encoding: .utf8)
        try expect(
            prompt.contains("Translate the text inside <translation_input>"),
            "stdin should contain translation-only instructions"
        )
        try expect(
            prompt.contains("<translation_input>\n你好，世界！\n</translation_input>"),
            "stdin should contain the source text inside explicit delimiters"
        )
    }
}
