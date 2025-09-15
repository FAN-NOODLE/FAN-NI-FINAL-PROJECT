import SwiftUI

@main
struct EmotionRealtime_Watch_App: App {
    // 初始化 HealthAccelerometer 和 PhoneBridge
    @StateObject private var healthAccelerometer = HealthAccelerometer()
    @StateObject private var phoneBridge = PhoneBridge.shared
    
    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(healthAccelerometer)
                .environmentObject(phoneBridge)
        }
    }
}
