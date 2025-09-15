import Foundation
import WatchConnectivity

final class PhoneBridge: NSObject, WCSessionDelegate, ObservableObject {
    static let shared = PhoneBridge()
    @Published var sentCount = 0
    
    private override init() {
        super.init()
        if WCSession.isSupported() {
            WCSession.default.delegate = self
            WCSession.default.activate()
        }
    }
    
    func sendWindow(featureWindow: [[String: Double]], quality: Double) {
        let payload: [String: Any] = [
            "ts": Date().timeIntervalSince1970,
            "feature_window": featureWindow,
            "quality_hint": quality
        ]
        if WCSession.default.isReachable {
            WCSession.default.sendMessage(payload, replyHandler: nil) { _ in
                WCSession.default.transferUserInfo(payload)
            }
        } else {
            WCSession.default.transferUserInfo(payload)
        }
        sentCount += 1
    }
    
    // 必要空实现
    func session(_ session: WCSession, activationDidCompleteWith activationState: WCSessionActivationState, error: Error?) {}
}
