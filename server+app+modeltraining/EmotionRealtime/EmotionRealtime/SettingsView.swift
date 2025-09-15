import SwiftUI

struct SettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var input = ApiClient.shared.currentBaseURL
    @State private var message = ""
    @State private var testing = false

    var body: some View {
        NavigationView {
            Form {
                Section(header: Text("服务器根地址")) {
                    TextField("http://192.168.3.192:8000 或 https://api.example.com", text: $input)
                        .textInputAutocapitalization(.never)
                        .keyboardType(.URL)
                        .autocorrectionDisabled(true)
                }
                
                if !message.isEmpty {
                    Section {
                        Text(message)
                            .font(.footnote)
                            .foregroundStyle(message.contains("失败") ? .red : .secondary)
                    }
                }
                
                Section {
                    Button {
                        if let err = ApiClient.shared.setBaseURL(input) {
                            message = "保存失败：\(err)"
                        } else {
                            message = "已保存。"
                        }
                    } label: {
                        Text("保存")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)

                    Button {
                        testing = true
                        message = "测试中..."
                        ApiClient.shared.pingHealth { result in
                            DispatchQueue.main.async {
                                testing = false
                                switch result {
                                case .success(let text):
                                    message = "健康检查成功：\(text)"
                                case .failure(let err):
                                    message = "健康检查失败：\(err.localizedDescription)"
                                }
                            }
                        }
                    } label: {
                        HStack {
                            Text("测试 /health")
                            if testing {
                                Spacer()
                                ProgressView()
                            }
                        }
                    }
                    .disabled(testing)
                }
            }
            .navigationTitle("服务器设置")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("关闭") { dismiss() }
                }
            }
        }
    }
}
