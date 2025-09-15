import json, sys
from pathlib import Path
import numpy as np, pandas as pd
import onnxruntime as ort
import time

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "outputs" / "v1"
MODEL_ONNX = OUT / "model.onnx"
MODEL_JSON = OUT / "model.json"
TEST_PARQ  = ROOT / "data" / "test.parquet"

def softmax(x):
    x = x - np.max(x, axis=-1, keepdims=True)
    e = np.exp(x)
    return e / np.sum(e, axis=-1, keepdims=True)

def load_model():
    sess = ort.InferenceSession(str(MODEL_ONNX), providers=["CPUExecutionProvider"])
    input_name = sess.get_inputs()[0].name
    output_name = sess.get_outputs()[0].name
    meta = json.loads(MODEL_JSON.read_text(encoding="utf-8"))
    feat_order = meta["feat_order"]; mean = np.array(meta["norm_mean"], dtype=np.float32)
    std = np.array(meta["norm_std"], dtype=np.float32); labels = meta["labels"]
    return sess, input_name, output_name, feat_order, mean, std, labels

def build_window_from_test(feat_order, L=12):
    df = pd.read_parquet(TEST_PARQ)
    # 选取一个 session 的最后 12 秒
    (sid, sess, act), g = next(iter(df.groupby(["subject_id","session_id","activity"])))
    g = g.sort_values("time").tail(L)
    W = g[feat_order].to_numpy("float32")
    return W, (sid, sess, act)

def run_once(sess, in_name, out_name, Wn):
    logits = sess.run([out_name], {in_name: Wn.reshape(1,*Wn.shape).astype(np.float32)})[0][0]  # (5,)
    probs = softmax(logits)
    idx = int(np.argmax(probs))
    return probs, idx

def main():
    if not MODEL_ONNX.exists() or not MODEL_JSON.exists():
        print("Please run export_onnx.py first.")
        sys.exit(1)

    sess, in_name, out_name, feat_order, mean, std, labels = load_model()
    W, meta = build_window_from_test(feat_order, L=12)

    # 归一化
    Wn = (W - mean) / (std + 1e-6)

    # 单次推理
    probs, idx = run_once(sess, in_name, out_name, Wn)
    print("Single inference:")
    print("meta:", meta)
    print("label:", labels[idx], "| confidence:", float(probs[idx]))
    print("probs:", {labels[i]: float(probs[i]) for i in range(len(labels))})

    # 1K 次延迟测试（CPU）
    N = 1000
    t0 = time.time()
    for _ in range(N):
        _ = run_once(sess, in_name, out_name, Wn)
    t1 = time.time()
    print(f"Latency median-ish: {(t1-t0)*1000/N:.3f} ms / window over {N} runs (rough estimate)")

    # 稳定性回归：静态窗口重复 10 次 → 标签不应抖动
    print("Stability check (static window x10):")
    labels_seq = []
    for _ in range(10):
        probs_i, idx_i = run_once(sess, in_name, out_name, Wn)
        labels_seq.append(labels[idx_i])
    print("labels_seq:", labels_seq)
    ok = all(l == labels_seq[0] for l in labels_seq)
    print("stable:", ok)

if __name__ == "__main__":
    main()
