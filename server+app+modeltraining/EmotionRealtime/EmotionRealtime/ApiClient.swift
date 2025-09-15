import Foundation

final class ApiClient {
    static let shared = ApiClient()
    private init() {}

    // 默认地址（你现在正在用的）；如果没配，会回落到这个
    private let defaultBase = "http://192.168.3.192:8000"

    // Key 存储在 UserDefaults
    private let baseKey = "api.base.url"

    // 读取当前 Base URL（带兜底）
    var currentBaseURL: String {
        UserDefaults.standard.string(forKey: baseKey) ?? defaultBase
    }

    // 组合最终接口路径（自动处理末尾斜杠）
    private var predictURL: URL? {
        var base = currentBaseURL.trimmingCharacters(in: .whitespacesAndNewlines)
        if base.hasSuffix("/") { base.removeLast() }
        return URL(string: base + "/predict_from_features")
    }

    // 设置/校验 Base URL；返回错误信息（为空则成功）
    @discardableResult
    func setBaseURL(_ raw: String) -> String? {
        let s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        // 允许 http 或 https；必须是合法 URL；不能有路径（最多一个末尾斜杠）
        guard let u = URL(string: s), let scheme = u.scheme, let host = u.host else {
            return "请输入合法的 URL（例如：http://192.168.3.192:8000 或 https://api.example.com）"
        }
        if !(scheme == "http" || scheme == "https") {
            return "仅支持 http 或 https 协议"
        }
        if (u.pathComponents.count > 1) { // 有除 "/" 外的路径
            return "请只填写服务器根地址，不要包含路径（/predict_from_features 会自动补上）"
        }
        UserDefaults.standard.set(s, forKey: baseKey)
        return nil
    }

    // 可选：探活 /health，便于在设置页里测试
    func pingHealth(completion: @escaping (Result<String,Error>) -> Void) {
        var base = currentBaseURL
        if base.hasSuffix("/") { base.removeLast() }
        guard let url = URL(string: base + "/health") else {
            completion(.failure(NSError(domain: "ApiClient", code: -2,
                     userInfo: [NSLocalizedDescriptionKey: "URL 组合失败"])))
            return
        }
        let req = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 5)
        URLSession.shared.dataTask(with: req) { data, resp, err in
            if let err = err { completion(.failure(err)); return }
            let text = data.flatMap { String(data: $0, encoding: .utf8) } ?? ""
            completion(.success(text))
        }.resume()
    }

    // 改回原来的方式：返回原始 Data
    func postPredict(json: [String: Any], completion: @escaping (Result<Data,Error>) -> Void) {
        guard let url = predictURL else {
            completion(.failure(NSError(domain: "ApiClient", code: -1,
                       userInfo: [NSLocalizedDescriptionKey: "服务器地址无效，请在设置里检查"])))
            return
        }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.timeoutInterval = 8
        req.httpBody = try? JSONSerialization.data(withJSONObject: json, options: [])

        URLSession.shared.dataTask(with: req) { data, resp, err in
            if let err = err { completion(.failure(err)); return }
            guard let data = data else {
                completion(.failure(NSError(domain: "ApiClient", code: -3,
                         userInfo: [NSLocalizedDescriptionKey: "服务器无返回数据"])))
                return
            }
            completion(.success(data))
        }.resume()
    }
}
