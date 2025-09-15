import Foundation
import HealthKit
import CoreMotion

/// 采集心率与加速度并每秒聚合为 5 个特征：
/// HR_mean, HR_slope, ACC_rms, ACC_energy, ACC_zcr（+ quality）
/// - ACC_* 特征基于“去重力后”的三轴加速度（优先用 userAcceleration；否则每秒对加速度做去均值）
/// - 采样频率：25 Hz（每秒约 25 点）
/// - 聚合频率：1 Hz（每秒产出一条特征）
final class HealthAccelerometer: NSObject, ObservableObject,
                                 HKWorkoutSessionDelegate, HKLiveWorkoutBuilderDelegate {

    // MARK: - Services
    private let hkStore = HKHealthStore()
    private let motion  = CMMotionManager()

    // MARK: - Published HR
    @Published var hrCurrent: Double? = nil
    private var lastHR: Double? = nil

    // MARK: - Accel buffers (1s window)
    private var accBuf: [(x: Double, y: Double, z: Double)] = []
    private var samplesThisSecond = 0

    // MARK: - HK workout
    private var session: HKWorkoutSession?
    private var builder: HKLiveWorkoutBuilder?

    // MARK: - 1 Hz timer
    private var timer: Timer?

    // MARK: - Public API
    func requestAuth(completion: @escaping (Bool)->Void) {
        let toShare: Set = [HKObjectType.workoutType()]
        let toRead : Set = [HKObjectType.quantityType(forIdentifier: .heartRate)!]
        hkStore.requestAuthorization(toShare: toShare, read: toRead) { ok, _ in
            DispatchQueue.main.async { completion(ok) }
        }
    }

    func start() {
        startWorkoutHR()
        startAccel()
        startTick()
    }

    func stop() {
        // 1) stop timer
        timer?.invalidate()
        timer = nil

        // 2) stop motion
        motion.stopDeviceMotionUpdates()
        motion.stopAccelerometerUpdates()

        // 3) stop HK session
        builder?.endCollection(withEnd: Date()) { [weak self] _, _ in
            self?.session?.end()
            self?.builder = nil
            self?.session  = nil
        }

        // 4) clear buffers
        accBuf.removeAll(keepingCapacity: true)
        samplesThisSecond = 0
    }

    deinit {
        timer?.invalidate()
        motion.stopDeviceMotionUpdates()
        motion.stopAccelerometerUpdates()
    }

    // MARK: - Heart Rate via HealthKit
    private func startWorkoutHR() {
        let config = HKWorkoutConfiguration()
        config.activityType = .other
        config.locationType = .indoor

        do {
            let session = try HKWorkoutSession(healthStore: hkStore, configuration: config)
            let builder = session.associatedWorkoutBuilder()
            session.delegate = self
            builder.delegate = self
            builder.dataSource = HKLiveWorkoutDataSource(healthStore: hkStore, workoutConfiguration: config)

            self.session = session
            self.builder = builder

            let now = Date()
            session.startActivity(with: now)
            builder.beginCollection(withStart: now) { success, err in
                if let err { print("Begin collection error:", err.localizedDescription) }
                if success { print("[HK] Workout data collection started") }
            }
        } catch {
            print("[HK] start error:", error.localizedDescription)
        }
    }

    // MARK: - Acceleration (prefer userAcceleration)
    private func startAccel() {
        if motion.isDeviceMotionAvailable {
            // 使用 DeviceMotion.userAcceleration（去重力），参考系设为 Z 轴竖直
            motion.deviceMotionUpdateInterval = 1.0 / 25.0
            motion.startDeviceMotionUpdates(using: .xArbitraryCorrectedZVertical, to: .main) { [weak self] dm, _ in
                guard let self, let ua = dm?.userAcceleration else { return }
                self.accBuf.append((ua.x, ua.y, ua.z))
                self.samplesThisSecond += 1
            }
            print("[ACC] Using DeviceMotion.userAcceleration @25Hz")
        } else if motion.isAccelerometerAvailable {
            // 回退：直接加速度（含重力）。后续每秒会做“去均值”以近似去掉重力 DC 分量。
            motion.accelerometerUpdateInterval = 1.0 / 25.0
            motion.startAccelerometerUpdates(to: .main) { [weak self] data, _ in
                guard let self, let a = data?.acceleration else { return }
                self.accBuf.append((a.x, a.y, a.z))
                self.samplesThisSecond += 1
            }
            print("[ACC] Using raw Accelerometer (will demean per-second)")
        } else {
            print("[ACC] No motion source available")
        }
    }

    // MARK: - 1 Hz aggregation
    private func startTick() {
        timer?.invalidate()
        // 使用 commonModes，确保 UI 滚动等情况下也能准时触发
        timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            self?.aggregateOneSecond()
        }
        RunLoop.main.add(timer!, forMode: .common)
    }

    private func aggregateOneSecond() {
        // ---- HR features ----
        let hrMean = hrCurrent ?? 0.0
        let hrSlope: Double = {
            defer { lastHR = hrCurrent }
            guard let last = lastHR, let cur = hrCurrent else { return 0.0 }
            return cur - last // BPM/s
        }()

        // ---- ACC features (25Hz within 1s) ----
        let n = max(accBuf.count, 1)

        // 每秒每轴去均值（对 userAcceleration 影响很小；对 raw accelerometer 可移除重力 DC）
        let meanX = accBuf.reduce(0.0) { $0 + $1.x } / Double(n)
        let meanY = accBuf.reduce(0.0) { $0 + $1.y } / Double(n)
        let meanZ = accBuf.reduce(0.0) { $0 + $1.z } / Double(n)
        let centered = accBuf.map { (x: $0.x - meanX, y: $0.y - meanY, z: $0.z - meanZ) }

        // 模长序列（中心化）
        let mags = centered.map { sqrt($0.x * $0.x + $0.y * $0.y + $0.z * $0.z) }

        // RMS：sqrt(mean(square(mag)))
        let meanSq = mags.reduce(0.0) { $0 + $1 * $1 } / Double(n)
        let rms = sqrt(meanSq)

        // Energy：平均能量（与 RMS² 同量纲，训练端更易归一化）
        let energy = meanSq

        // ZCR：在中心化后的模长上计算过零率（进一步去均值）
        var zcrRate = 0.0
        if mags.count > 1 {
            let meanMag = mags.reduce(0.0, +) / Double(mags.count)
            var prev = mags[0] - meanMag
            var zcr = 0.0
            for i in 1..<mags.count {
                let cur = mags[i] - meanMag
                if (prev == 0 && cur != 0) || (prev * cur < 0) { zcr += 1 }
                prev = cur
            }
            zcrRate = zcr / Double(mags.count - 1)
        }

        // 质量估计（保留你的逻辑）
        let accQ = min(1.0, max(0.2, Double(samplesThisSecond) / 18.0))
        let hrQ  = (hrCurrent == nil) ? 0.5 : 1.0
        let quality = max(0.0, min(1.0, accQ * hrQ))

        // 输出一秒特征
        FeatureAggregator.shared.pushSecond(
            hrMean: hrMean,
            hrSlope: hrSlope,
            accRms: rms,
            accEnergy: energy,
            accZcr: zcrRate,
            quality: quality
        )

        // Debug（可注释）
        print(String(format: "[1s] n=%2d HR=%.1f slope=%+.2f RMS=%.3f Eng=%.5f ZCR=%.3f Q=%.2f",
                     samplesThisSecond, hrMean, hrSlope, rms, energy, zcrRate, quality))

        // reset 1s buffers
        accBuf.removeAll(keepingCapacity: true)
        samplesThisSecond = 0
    }

    // MARK: - HKWorkoutSessionDelegate
    func workoutSession(_ workoutSession: HKWorkoutSession,
                        didChangeTo toState: HKWorkoutSessionState,
                        from fromState: HKWorkoutSessionState,
                        date: Date) {
        print("[HK] state \(fromState.rawValue) -> \(toState.rawValue)")
        if toState == .ended || toState == .stopped {
            timer?.invalidate()
            timer = nil
        }
    }

    func workoutSession(_ workoutSession: HKWorkoutSession, didFailWithError error: Error) {
        print("[HK] error:", error.localizedDescription)
        timer?.invalidate()
        timer = nil
    }

    #if os(iOS)
    func workoutSession(_ workoutSession: HKWorkoutSession,
                        didDisconnectDevice device: HKDevice) {
        print("[HK] Device disconnected")
    }
    #endif

    // MARK: - HKLiveWorkoutBuilderDelegate
    func workoutBuilderDidCollectEvent(_ workoutBuilder: HKLiveWorkoutBuilder) {
        // optional
    }

    func workoutBuilder(_ workoutBuilder: HKLiveWorkoutBuilder,
                        didCollectDataOf collectedTypes: Set<HKSampleType>) {
        guard let hrType = HKQuantityType.quantityType(forIdentifier: .heartRate),
              collectedTypes.contains(hrType) else { return }
        if let stats = workoutBuilder.statistics(for: hrType),
           let mostRecent = stats.mostRecentQuantity() {
            let bpm = mostRecent.doubleValue(for: HKUnit(from: "count/min"))
            DispatchQueue.main.async { self.hrCurrent = bpm }
        }
    }
}

