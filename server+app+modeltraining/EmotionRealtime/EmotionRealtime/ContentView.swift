import SwiftUI

struct ContentView: View {
    @StateObject var recv = PhoneReceiver.shared
    @State private var showSettings = false

    var body: some View {
        VStack(spacing: 16) {
            // 顶部标题和设置按钮
            HStack {
                Text("Emotion Relay").font(.title2).bold()
                Spacer()
                Button {
                    showSettings = true
                } label: {
                    Image(systemName: "gearshape")
                        .font(.title3)
                }
            }
            .padding(.horizontal)
            
            // 服务器地址显示
            Text("Base: \(ApiClient.shared.currentBaseURL)")
                .font(.footnote)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .truncationMode(.middle)
                .padding(.horizontal)
            
            // 状态信息
            VStack(spacing: 12) {
                InfoRow(title: "Status:", value: recv.lastStatus)
                InfoRow(title: "Label:", value: recv.lastLabel)
                InfoRow(title: "Conf:", value: String(format: "%.2f", recv.lastConf))
                InfoRow(title: "Quality:", value: String(format: "%.2f", recv.lastQuality))
            }
            .padding()
            .background(Color.gray.opacity(0.1))
            .cornerRadius(12)
            .padding(.horizontal)
            
            Spacer()
            
            // 提示信息
            Text("确保 iPhone 与电脑在同一 Wi-Fi，且后端已启动")
                .font(.footnote)
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)
                .padding()
        }
        .padding(.vertical)
        .sheet(isPresented: $showSettings) {
            SettingsView()
        }
    }
}

// 辅助视图：信息行
struct InfoRow: View {
    let title: String
    let value: String
    
    var body: some View {
        HStack {
            Text(title).bold()
            Spacer()
            Text(value).monospaced()
        }
    }
}
