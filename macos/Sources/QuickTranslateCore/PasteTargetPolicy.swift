import Foundation

public enum PasteTargetPolicy {
    public static func shouldPaste(
        sourceProcessID: Int32,
        sourceBundleIdentifier: String?,
        currentProcessID: Int32,
        currentBundleIdentifier: String?
    ) -> Bool {
        if sourceProcessID != 0, sourceProcessID == currentProcessID {
            return true
        }

        guard let sourceBundleIdentifier = normalized(sourceBundleIdentifier),
              let currentBundleIdentifier = normalized(currentBundleIdentifier) else {
            return false
        }
        return sourceBundleIdentifier == currentBundleIdentifier
    }

    private static func normalized(_ bundleIdentifier: String?) -> String? {
        guard let value = bundleIdentifier?.trimmingCharacters(in: .whitespacesAndNewlines),
              !value.isEmpty else {
            return nil
        }
        return value.lowercased()
    }
}
