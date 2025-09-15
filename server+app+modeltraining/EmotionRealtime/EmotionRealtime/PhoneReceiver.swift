import Foundation
import WatchConnectivity
import SwiftUI

final class PhoneReceiver: NSObject, ObservableObject, WCSessionDelegate {
    static let shared = PhoneReceiver()
    @Published var lastStatus: String = "Idle"
    @Published var lastLabel: String = "-"
    @Published var lastConf: Double = 0.0
    @Published var lastQuality: Double = 0.0

    private override init() {
        super.init()
        if WCSession.isSupported() {
            let session = WCSession.default
            session.delegate = self
            session.activate()
        }
    }

    // MARK: - WCSessionDelegate 必需的方法

    #if os(iOS)
    func sessionDidBecomeInactive(_ session: WCSession) {
        // iOS端会话变为非活跃状态
        print("Session became inactive")
    }

    func sessionDidDeactivate(_ session: WCSession) {
        // iOS端会话已停用，需要重新激活
        WCSession.default.activate()
    }
    #endif

    func session(_ session: WCSession, activationDidCompleteWith activationState: WCSessionActivationState, error: Error?) {
        if let error = error {
            print("Session activation failed with error: \(error.localizedDescription)")
            DispatchQueue.main.async {
                self.lastStatus = "Activation Error"
            }
            return
        }
        
        print("Session activated with state: \(activationState.rawValue)")
        DispatchQueue.main.async {
            self.lastStatus = activationState == .activated ? "Connected" : "Disconnected"
        }
    }

    // MARK: - 接收消息的方法

    func session(_ session: WCSession, didReceiveMessage message: [String : Any]) {
        print("Received message: \(message)")
        forward(payload: message)
    }

    func session(_ session: WCSession, didReceiveUserInfo userInfo: [String : Any]) {
        print("Received user info: \(userInfo)")
        forward(payload: userInfo)
    }

    // MARK: - 消息转发逻辑（手动解析JSON）

    private func forward(payload: [String: Any]) {
        guard let fw = payload["feature_window"] as? [[String: Double]],
              fw.count == 12,
              let q = payload["quality_hint"] as? Double,
              let ts = payload["ts"] as? Double
        else {
            print("Invalid payload format")
            DispatchQueue.main.async {
                self.lastStatus = "Invalid Data"
            }
            return
        }

        let req: [String: Any] = ["ts": ts, "feature_window": fw, "quality_hint": q]
        DispatchQueue.main.async {
            self.lastStatus = "Sending..."
            self.lastQuality = q
        }
        
        ApiClient.shared.postPredict(json: req) { result in
            DispatchQueue.main.async {
                switch result {
                case .success(let data):
                    // 手动解析JSON响应
                    self.handleResponseData(data)
                case .failure(let err):
                    self.lastStatus = "Err: \(err.localizedDescription)"
                    self.lastLabel = "-"
                    self.lastConf = 0.0
                }
            }
        }
    }

    // 手动解析响应数据
    private func handleResponseData(_ data: Data) {
        do {
            // 首先尝试解析为JSON对象
            if let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] {
                print("收到响应JSON: \(json)")
                
                // 尝试多种可能的字段名
                let label = json["label"] as? String ??
                           json["emotion"] as? String ??
                           "unknown"
                
                let confidence = json["confidence"] as? Double ??
                               json["conf"] as? Double ??
                               json["score"] as? Double ??
                               0.0
                
                self.lastStatus = "Success"
                self.lastLabel = label
                self.lastConf = confidence
                
            } else {
                // 如果不是字典，尝试其他格式
                if let responseString = String(data: data, encoding: .utf8) {
                    print("响应内容: \(responseString)")
                    self.lastStatus = "Unexpected format"
                    self.lastLabel = "raw"
                    self.lastConf = 0.0
                } else {
                    self.lastStatus = "Invalid response"
                }
            }
        } catch {
            print("JSON解析错误: \(error)")
            self.lastStatus = "Parse error"
            self.lastLabel = "-"
            self.lastConf = 0.0
            
            // 调试：打印原始数据
            if let rawString = String(data: data, encoding: .utf8) {
                print("原始响应: \(rawString)")
            }
        }
    }
}
