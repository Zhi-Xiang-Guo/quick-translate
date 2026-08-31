import Foundation

public struct TripleSpaceSequence {
    private let maxInterval: TimeInterval
    private var pressCount = 0
    private var lastPressTime: TimeInterval?
    private var processID: Int32?
    private var spaceIsDown = false

    public init(maxInterval: TimeInterval) {
        precondition(maxInterval > 0)
        self.maxInterval = maxInterval
    }

    public mutating func handleKeyDown(
        at time: TimeInterval,
        processID: Int32,
        isRepeat: Bool
    ) -> Bool {
        guard !isRepeat, !spaceIsDown else { return false }
        spaceIsDown = true

        if let lastPressTime,
           time - lastPressTime <= maxInterval,
           self.processID == processID {
            pressCount += 1
        } else {
            pressCount = 1
        }
        self.lastPressTime = time
        self.processID = processID

        guard pressCount == 3 else { return false }
        resetPresses()
        return true
    }

    public mutating func handleKeyUp() {
        spaceIsDown = false
    }

    public mutating func cancel() {
        resetPresses()
        spaceIsDown = false
    }

    private mutating func resetPresses() {
        pressCount = 0
        lastPressTime = nil
        processID = nil
    }
}
