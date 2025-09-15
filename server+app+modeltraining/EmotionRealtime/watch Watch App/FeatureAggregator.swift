import Foundation

final class FeatureAggregator {
    static let shared = FeatureAggregator()
    private init() {}
    
    private var window: [[String: Double]] = []
    private var lastQuality: Double = 1.0
    
    func pushSecond(hrMean: Double, hrSlope: Double, accRms: Double, accEnergy: Double, accZcr: Double, quality: Double) {
        func san(_ x: Double) -> Double { x.isFinite ? x : 0.0 }
        let sec: [String: Double] = [
            "HR_mean": san(hrMean),
            "HR_slope": san(hrSlope),
            "ACC_rms": san(accRms),
            "ACC_energy": san(accEnergy),
            "ACC_zcr": san(accZcr)
        ]
        window.append(sec)
        if window.count > 12 { window.removeFirst() }
        lastQuality = quality
        
        if window.count == 12 {
            PhoneBridge.shared.sendWindow(featureWindow: window, quality: lastQuality)
        }
    }
}
