"""
将逐秒特征(含 subject_id/session_id/activity/label) 构造成 (12,5) 滑动窗样本。
窗尾时刻的 label 作为监督信号。
"""
import argparse, pandas as pd, numpy as np
from pathlib import Path


FEAT_ORDER = ["HR_mean","HR_slope","ACC_rms","ACC_energy","ACC_zcr"]


def make_windows(df: pd.DataFrame, win: int = 12):
X, y = [], []
# 按 subject_id, session_id, activity 分组，避免跨段窗口
for _, g in df.sort_values("time").groupby(["subject_id","session_id","activity"], dropna=False):
g = g.reset_index(drop=True)
# 构建窗口
for i in range(len(g) - win + 1):
seg = g.iloc[i:i+win]
feats = seg[FEAT_ORDER].to_numpy(dtype=np.float32)
X.append(feats)
y.append(seg.iloc[-1]["label"]) # 窗尾标签
return np.stack(X), np.array(y)


def main():
ap = argparse.ArgumentParser()
ap.add_argument("--input", type=str, required=True, help="CSV/Parquet with per-second features")
ap.add_argument("--output", type=str, required=True, help="npz output path")
ap.add_argument("--win", type=int, default=12)
args = ap.parse_args()


ext = Path(args.input).suffix
if ext == ".parquet":
df = pd.read_parquet(args.input)
else:
df = pd.read_csv(args.input)


X, y = make_windows(df, args.win)
np.savez_compressed(args.output, X=X, y=y)
print({"X": X.shape, "y": y.shape, "out": args.output})


if __name__ == "__main__":
main()