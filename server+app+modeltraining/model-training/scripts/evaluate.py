import argparse, yaml, os, sys
from pathlib import Path
import numpy as np, pandas as pd
import torch
from sklearn.preprocessing import LabelEncoder
from sklearn.metrics import f1_score, precision_recall_fscore_support, confusion_matrix
import matplotlib.pyplot as plt
import seaborn as sns

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.append(str(ROOT))

def load_cfg(path):
    for enc in ("utf-8","utf-8-sig","gbk","latin-1"):
        try:
            with open(path, "r", encoding=enc) as f:
                return yaml.safe_load(f)
        except Exception:
            continue
    raise RuntimeError(f"Cannot read YAML: {path}")

def build_test_windows(parquet_path, feat_order, L=12):
    df = pd.read_parquet(parquet_path) if str(parquet_path).endswith(".parquet") else pd.read_csv(parquet_path)
    xs, ys, metas = [], [], []
    for (sid, sess, act), g in df.sort_values("time").groupby(["subject_id","session_id","activity"], dropna=False):
        g = g.reset_index(drop=True)
        if len(g) < L:
            continue
        for i in range(len(g)-L+1):
            seg = g.iloc[i:i+L]
            xs.append(seg[feat_order].to_numpy("float32"))
            ys.append(seg.iloc[-1]["label"])
            metas.append((sid, sess, act))
    X = np.stack(xs) if xs else np.zeros((0,L,len(feat_order)), dtype="float32")
    y = np.array(ys)
    meta = pd.DataFrame(metas, columns=["subject_id","session_id","activity"])
    return X, y, meta

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cfg", required=True)
    ap.add_argument("--test", required=True)
    ap.add_argument("--out", default="outputs/v1")
    args = ap.parse_args()

    cfg = load_cfg(args.cfg)
    out_dir = Path(args.out); out_dir.mkdir(parents=True, exist_ok=True)

    # 加载并窗口化
    X, y_raw, meta = build_test_windows(args.test, cfg["feat_order"], cfg["input_len"])
    if len(X) == 0:
        raise RuntimeError("No test samples after windowing.")

    # 标签编码（与 cfg['labels'] 顺序一致）
    le = LabelEncoder(); le.fit(cfg["labels"])
    # 可能是数字 → 按索引映射回类名
    if not np.all(np.isin(y_raw, cfg["labels"])) and np.issubdtype(y_raw.dtype, np.number):
        idx = y_raw.astype(int)
        y_raw = np.array([cfg["labels"][i] for i in idx])
    if not np.all(np.isin(y_raw, cfg["labels"])):
        raise ValueError(f"Test labels mismatch. got {sorted(set(map(str, y_raw)))} vs {cfg['labels']}")
    y = le.transform(y_raw)

    # 从训练统计读取 norm（优先 model_meta.json；否则用测试集自身统计兜底）
    meta_path = Path(args.out) / "model_meta.json"
    if meta_path.exists():
        import json
        m = json.loads(meta_path.read_text(encoding="utf-8"))
        mean = np.array(m["norm_mean"], dtype="float32")
        std  = np.array(m["norm_std"], dtype="float32")
    else:
        Xcat = X.reshape(-1, X.shape[-1])
        mean = Xcat.mean(axis=0).astype("float32")
        std  = Xcat.std(axis=0).astype("float32") + 1e-6

    # 归一化
    Xn = (X - mean) / std

    # 加载 PyTorch 模型权重并评估
    # 简化：直接用 logits = 线性判别做“占位”不可行；这里仅评估传统指标需 logits/模型。
    # 因为 evaluate.py 独立于训练脚本，这里采用 sklearn 风格：需要用户提供 ckpt → 我们加载并前向一次。
    # 为减少耦合，这里直接读取 best.pt 并复用 models 构图：
    from models.tcn import TCN
    from models.bilstm import BiLSTM
    import torch.nn as nn

    device = torch.device("cpu")
    n_classes = len(cfg["labels"])
    if cfg["model"] == "TCN":
        model = TCN(n_feats=cfg["n_feats"], channels=cfg["channels"], kernel_size=cfg["kernel_size"],
                    dropout=cfg["dropout"], n_classes=n_classes, seq_len=cfg["input_len"])
    else:
        model = BiLSTM(n_feats=cfg["n_feats"], hidden_size=cfg["hidden_size"], num_layers=cfg["num_layers"],
                       attn=cfg["attn"], dropout=cfg["dropout"], n_classes=n_classes)
    ckpt = Path(args.out) / "checkpoints" / "best.pt"
    if not ckpt.exists():
        raise FileNotFoundError(f"Missing checkpoint: {ckpt}")
    state = torch.load(ckpt, map_location=device)
    model.load_state_dict(state); model.eval()

    # 批量前向
    B = 1024
    y_pred = []
    with torch.no_grad():
        for i in range(0, len(Xn), B):
            xb = torch.from_numpy(Xn[i:i+B])  # (b,12,5)
            logits = model(xb)                 # (b,C)
            y_pred.extend(logits.argmax(dim=-1).cpu().numpy().tolist())
    y_pred = np.array(y_pred, dtype=int)

    # 总体指标
    macro_f1 = f1_score(y, y_pred, average="macro", zero_division=0)
    pr, rc, f1, _ = precision_recall_fscore_support(y, y_pred, average=None, zero_division=0)
    cm = confusion_matrix(y, y_pred, labels=list(range(n_classes)))

    # 活动分层（DEAP 默认 sitting）
    meta_eval = meta.copy()
    meta_eval["y_true"] = y
    meta_eval["y_pred"] = y_pred
    per_activity = {}
    for act, g in meta_eval.groupby("activity", dropna=False):
        yy = g["y_true"].to_numpy(); pp = g["y_pred"].to_numpy()
        if len(yy) == 0:
            continue
        per_activity[str(act)] = {
            "macro_f1": float(f1_score(yy, pp, average="macro", zero_division=0)),
        }

    # 保存图与指标
    plt.figure(figsize=(5,4))
    sns.heatmap(cm, annot=True, fmt='d', cmap='Blues',
                xticklabels=cfg["labels"], yticklabels=cfg["labels"])
    plt.xlabel("Pred"); plt.ylabel("True"); plt.tight_layout()
    (out_dir / "confusion_test.png").write_bytes(plt.gcf().canvas.buffer_rgba())
    plt.savefig(out_dir / "confusion_test.png", dpi=160); plt.close()

    import json
    metrics = {
        "macro_f1": float(macro_f1),
        "per_class_precision": pr.tolist(),
        "per_class_recall": rc.tolist(),
        "per_class_f1": f1.tolist(),
        "labels": cfg["labels"],
        "per_activity": per_activity
    }
    with open(out_dir / "metrics_test.yaml", "w", encoding="utf-8") as f:
        yaml.safe_dump(metrics, f, allow_unicode=True)
    print(f"Test macro F1: {macro_f1:.4f}")
    print("Saved:", out_dir / "confusion_test.png", "and", out_dir / "metrics_test.yaml")

if __name__ == "__main__":
    main()
