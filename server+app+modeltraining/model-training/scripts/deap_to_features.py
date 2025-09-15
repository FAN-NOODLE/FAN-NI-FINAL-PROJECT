import argparse
import glob
import os
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import pickle


def load_subject(file_path):
    """
    Load subject data from .dat file
    """
    try:
        with open(file_path, 'rb') as f:
            data = pickle.load(f, encoding='latin1')
        return data['data'], data['labels']
    except Exception as e:
        raise ValueError(f"Failed to load {file_path}: {e}")


def va_to_5class(valence, arousal):
    """
    将 DEAP 的 (V,A)∈[1..9] 按指定规则映射为 5 类：
    兴奋(Excited), 快乐(Happy), 焦虑(Anxious), 悲伤(Sad), 平静(Calm)

    规则优先级（先判阈值箱，再灰区回退）：
      兴奋:  V>=7  且  A>=7
      快乐:  V>=7  且  4<=A<=6
      焦虑:  V<=4  且  A>=6
      悲伤:  V<=4  且  A<=4
      平静:  5<=V<=7 且  A<=3
    其余落在灰区的样本 → 按到 5 个“原型点”的 L1 距离就近归类。
    """
    v, a = float(valence), float(arousal)

    # 1) 阈值箱（严格命中）
    if v >= 7 and a >= 7:
        return "Excited"   # Excited
    if v >= 7 and 4 <= a <= 6:
        return "Happy"   # Happy
    if v <= 4 and a >= 6:
        return "Anxious"   # Anxious
    if v <= 4 and a <= 4:
        return "Sad"   # Sad
    if 5 <= v <= 7 and a <= 3:
        return "Calm"   # Calm

    # 2) 灰区回退：按到各类“原型点”的 L1 距离最近归类
    # （原型点可微调；这里选在各类规则的“典型位置”）
    prototypes = {
        "Excited": (8.0, 8.0),   # 高V高A
        "Happy": (8.0, 5.0),   # 高V中A
        "Anxious": (3.0, 7.0),   # 低V高A
        "Sad": (3.0, 3.0),   # 低V低A
        "Calm": (6.0, 2.0),   # 中高V低A
    }
    # 计算 L1 距离
    best_lbl, best_d = None, 1e9
    for lbl, (pv, pa) in prototypes.items():
        d = abs(v - pv) + abs(a - pa)
        if d < best_d:
            best_lbl, best_d = lbl, d
    return best_lbl




def bvp_to_hr_per_sec(bvp_signal, fs=128, window_sec=1):
    """
    从 BVP（光体积描记）估计逐秒心率与逐秒斜率。
    步骤：
      1) 带通滤波 (0.7–3.0 Hz ≈ 42–180 BPM) + 去趋势 + 标准化
      2) 峰值检测（相邻峰最小间隔 ~0.33s；自适应阈值）
      3) 计算 R-R 间期(IBI) → 瞬时 HR = 60/IBI（单位 BPM）
      4) 将瞬时 HR 按峰间时间线性插值到逐采样，再按每秒平均 → HR_mean
      5) 逐秒差分 → HR_slope（BPM/s）
      6) 质量分：考虑峰密度、峰值信噪、IBI 变异等，归一到 [0,1]
    返回：
      hr_sec:   (n_seconds,)  每秒心率
      slope:    (n_seconds,)  每秒斜率（相邻秒差）
    说明：本函数只返回 HR 与 slope；若你需要 quality，可在内部一起返回并在主循环保存。
    """
    import numpy as np

    x = np.asarray(bvp_signal, dtype=np.float32)
    N = len(x)
    if N < fs * 5:  # 时长太短
        n_windows = max(0, N // fs)
        return np.zeros(n_windows, dtype=np.float32), np.zeros(n_windows, dtype=np.float32)

    # ---- (1) 预处理：带通 + 去趋势 + 标准化 ----
    try:
        from scipy import signal
        # 带通：0.7–3.0 Hz（约 42–180 BPM）
        lo, hi = 0.7, 3.0
        b, a = signal.butter(3, [lo/(fs*0.5), hi/(fs*0.5)], btype='bandpass')
        xf = signal.filtfilt(b, a, x)
        # 去趋势（高通到 0.05Hz 附近，防止漂移），再标准化
        xf = signal.detrend(xf, type='linear')
    except Exception:
        # 没有 scipy 就用简化版：去均值+滑动均值去噪
        xf = x - np.nanmean(x)
        win = max(1, int(0.15*fs))  # 150 ms 平滑
        c = np.convolve(xf, np.ones(win)/win, mode='same')
        xf = xf - c

    # 标准化
    s = np.nanstd(xf) + 1e-6
    xf = xf / s

    # ---- (2) 峰值检测 ----
    # 最小峰间距：~0.33s（~180BPM 上限），阈值根据数据自适应
    min_dist = int(0.33 * fs)
    thr = 0.5  # 基础阈值（标准化后）
    peaks = None
    try:
        from scipy.signal import find_peaks
        peaks, props = find_peaks(xf, distance=min_dist, height=thr)
        # 若峰太少则逐步降低阈值
        if len(peaks) < int(N/fs) * 0.6:
            for thr_try in [0.4, 0.3, 0.2]:
                peaks, props = find_peaks(xf, distance=min_dist, height=thr_try)
                if len(peaks) >= int(N/fs) * 0.6:
                    break
    except Exception:
        # 简易峰值扫描（无 scipy）
        peaks = []
        last_p = -10**9
        for i in range(1, N-1):
            if xf[i] > thr and xf[i] > xf[i-1] and xf[i] > xf[i+1] and (i - last_p) >= min_dist:
                peaks.append(i); last_p = i
        peaks = np.array(peaks, dtype=int)

    # 如果几乎没有峰，直接返回 0
    if peaks is None or len(peaks) < 2:
        n_windows = N // fs
        return np.zeros(n_windows, dtype=np.float32), np.zeros(n_windows, dtype=np.float32)

    # ---- (3) IBI & 瞬时 HR（按峰时刻）----
    ibi = np.diff(peaks) / float(fs)          # s
    hr_inst = 60.0 / np.clip(ibi, 1e-3, 10.0) # BPM（上限 ~360）
    t_inst = peaks[1:] / float(fs)            # 每个瞬时 HR 的时间戳（对齐右峰）

    # ---- (4) 插值到逐采样，再逐秒平均 ----
    t_all = np.arange(N) / float(fs)
    # 为了可插值，前后各补一个端点
    t_pad = np.concatenate([[t_all[0]], t_inst, [t_all[-1]]])
    h_pad = np.concatenate([[hr_inst[0]], hr_inst, [hr_inst[-1]]])
    hr_interp = np.interp(t_all, t_pad, h_pad)  # 每个采样点的 HR

    # 按秒平均
    n_seconds = N // fs
    if n_seconds <= 0:
        return np.zeros(0, dtype=np.float32), np.zeros(0, dtype=np.float32)
    hr_sec = np.zeros(n_seconds, dtype=np.float32)
    for s_idx in range(n_seconds):
        seg = hr_interp[s_idx*fs:(s_idx+1)*fs]
        hr_sec[s_idx] = float(np.nanmean(seg)) if seg.size else 0.0

    # ---- (5) 逐秒斜率（BPM/s）----
    slope = np.zeros_like(hr_sec)
    if len(hr_sec) > 1:
        slope[1:] = hr_sec[1:] - hr_sec[:-1]

    return hr_sec, slope



def validate_arguments(args):
    """
    Validate command line arguments
    """
    if not os.path.exists(args.deap_dir):
        raise FileNotFoundError(f"Directory not found: {args.deap_dir}")

    # Create output directory if it doesn't exist
    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    return args


def main():
    # Argument parser
    ap = argparse.ArgumentParser(
        description='Process DEAP dataset and extract features for emotion recognition'
    )
    ap.add_argument('--deap_dir', required=True,
                    help='path to data_preprocessed_python folder containing s*.dat files')
    ap.add_argument('--out', required=True,
                    help='output file path (.parquet or .csv)')
    ap.add_argument('--bvp_idx', type=int, default=39,
                    help='BVP channel index (common: 38 or 39)')
    ap.add_argument('--fs', type=int, default=128,
                    help='sampling rate of DEAP preprocessed data (default 128)')
    ap.add_argument('--debug', action='store_true',
                    help='print extra debug information')

    args = ap.parse_args()

    try:
        # Validate arguments
        args = validate_arguments(args)

        # Find subject files
        files = sorted(glob.glob(os.path.join(args.deap_dir, 's*.dat')))
        if len(files) == 0:
            raise FileNotFoundError(
                f"No s*.dat files found in {args.deap_dir}. "
                f"Point to the folder that directly contains s01.dat … s32.dat."
            )

        if args.debug:
            print(f"Found {len(files)} subject files")

        rows = []
        processed_subjects = 0

        for f in files:
            subject_id = Path(f).stem  # e.g., s01

            try:
                X, Y = load_subject(f)  # X: (40, 40, 8064), Y: (40, 4)
                fs = int(args.fs)
                T = 60  # seconds per trial

                if args.debug:
                    print(f"Loaded {subject_id}: X={X.shape}, Y={Y.shape}")

                for trial_idx in range(X.shape[0]):
                    trial = X[trial_idx]  # (40, 8064)
                    v, a = float(Y[trial_idx, 0]), float(Y[trial_idx, 1])
                    label = va_to_5class(v, a)

                    # Validate BVP channel index
                    ch = args.bvp_idx
                    if ch < 0 or ch >= trial.shape[0]:
                        print(f"Warning: bvp_idx {ch} out of range for {subject_id} "
                              f"(max: {trial.shape[0] - 1}). Using channel 0.")
                        ch = 0

                    bvp = trial[ch]

                    # Process BVP signal
                    hr, slope = bvp_to_hr_per_sec(bvp, fs=fs)

                    if args.debug:
                        m, s = float(np.mean(hr)), float(np.std(hr))
                        print(f"  {subject_id} trial{trial_idx:02d}: "
                              f"label={label}, HR mean={m:.1f}±{s:.1f}")

                    # Build second-wise rows
                    for t in range(min(T, hr.shape[0])):
                        rows.append({
                            'time': t,
                            'HR_mean': float(hr[t]),
                            'HR_slope': float(slope[t]),
                            'ACC_rms': 0.0,  # Placeholder - you'll need to implement ACC processing
                            'ACC_energy': 0.0,
                            'ACC_zcr': 0.0,
                            'subject_id': subject_id,
                            'session_id': f'{subject_id}_trial{trial_idx:02d}',
                            'activity': 'sitting',
                            'label': label,
                        })

                processed_subjects += 1

            except Exception as e:
                print(f"Error processing {subject_id}: {e}")
                continue

        if len(rows) == 0:
            raise ValueError("No data was processed successfully")

        # Create DataFrame
        df = pd.DataFrame(rows)
        out_path = Path(args.out)

        # Save data
        try:
            if out_path.suffix.lower() == '.csv':
                df.to_csv(out_path, index=False)
                print(f"Saved CSV: {out_path}")
            else:
                # Default to parquet
                df.to_parquet(out_path, index=False)
                print(f"Saved Parquet: {out_path}")

        except Exception as e:
            print(f"Failed to save as {out_path.suffix}: {e}")
            # Fallback to CSV
            csv_path = out_path.with_suffix('.csv')
            df.to_csv(csv_path, index=False)
            print(f"Saved as fallback CSV: {csv_path}")
            out_path = csv_path

        # Print summary
        n_sub = df['subject_id'].nunique()
        n_sess = df['session_id'].nunique()

        summary = {
            'saved_path': str(out_path),
            'total_rows': len(df),
            'subjects_processed': processed_subjects,
            'unique_subjects': int(n_sub),
            'sessions': int(n_sess),
            'seconds_per_session': 60,
            'file_size_MB': round(out_path.stat().st_size / (1024 * 1024), 2)
        }

        print("\n=== Processing Summary ===")
        for key, value in summary.items():
            print(f"{key:20}: {value}")

    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)


if __name__ == '__main__':
    main()