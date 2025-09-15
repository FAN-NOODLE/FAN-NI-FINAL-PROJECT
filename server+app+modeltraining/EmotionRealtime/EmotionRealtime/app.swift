import SwiftUI

@main
struct EmotionRealtimeApp: App {
    // 初始化 PhoneReceiver，确保单例被创建
    @StateObject private var phoneReceiver = PhoneReceiver.shared
    
    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(phoneReceiver)
        }
    }
}
