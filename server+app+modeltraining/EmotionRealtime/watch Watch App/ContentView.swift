import SwiftUI

struct ContentView: View {
    @StateObject var ha = HealthAccelerometer()
    @StateObject var bridge = PhoneBridge.shared
    @State private var running = false
    @State private var authed = false
    
    var body: some View {
        VStack(spacing: 8) {
            Text("Emotion Sender").font(.headline)
            if !authed {
                Button("授权 HealthKit") {
                    ha.requestAuth { ok in authed = ok }
                }
            }
            Button(running ? "停止" : "开始") {
                if running { ha.stop() } else { ha.start() }
                running.toggle()
            }
            .tint(running ? .red : .green)
            
            Text("已发送：\(bridge.sentCount)").font(.footnote).monospaced()
        }
        .padding()
    }
}
