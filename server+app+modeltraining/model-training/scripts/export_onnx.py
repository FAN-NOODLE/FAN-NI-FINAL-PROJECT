import argparse, yaml, json, sys
from pathlib import Path
import numpy as np
import torch

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

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cfg", required=True)
    ap.add_argument("--ckpt", required=True)
    ap.add_argument("--out_dir", required=True)
    args = ap.parse_args()

    cfg = load_cfg(args.cfg)
    out_dir = Path(args.out_dir); out_dir.mkdir(parents=True, exist_ok=True)

    # 构图 & 加权
    from models.tcn import TCN
    from models.bilstm import BiLSTM

    n_classes = len(cfg["labels"])
    if cfg["model"] == "TCN":
        model = TCN(n_feats=cfg["n_feats"], channels=cfg["channels"], kernel_size=cfg["kernel_size"],
                    dropout=cfg["dropout"], n_classes=n_classes, seq_len=cfg["input_len"])
    else:
        model = BiLSTM(n_feats=cfg["n_feats"], hidden_size=cfg["hidden_size"], num_layers=cfg["num_layers"],
                       attn=cfg["attn"], dropout=cfg["dropout"], n_classes=n_classes)
    state = torch.load(args.ckpt, map_location="cpu")
    model.load_state_dict(state)
    model.eval()
    torch.set_grad_enabled(False)

    # 导出 ONNX
    onnx_path = out_dir / "model.onnx"
    dummy = torch.zeros(1, cfg["input_len"], cfg["n_feats"], dtype=torch.float32)
    opset = int(cfg.get("opset", 13))
    torch.onnx.export(
        model, dummy, str(onnx_path),
        input_names=["input"], output_names=["logits"],
        opset_version=opset, dynamic_axes=None
    )
    print("Exported:", onnx_path)

    # 生成 model.json（读取训练时保存的 model_meta.json 优先）
    meta_path = out_dir / "model_meta.json"
    if meta_path.exists():
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        norm_mean = meta["norm_mean"]; norm_std = meta["norm_std"]
        training_seed = meta.get("training_seed", cfg.get("seed", 42))
    else:
        # 兜底：若没有 meta，给个安全默认（不建议）
        norm_mean = [0.0]*cfg["n_feats"]; norm_std = [1.0]*cfg["n_feats"]; training_seed = cfg.get("seed", 42)

    model_json = {
        "version": cfg.get("version", "v1.0.0"),
        "feat_order": cfg["feat_order"],
        "norm_mean": [float(x) for x in norm_mean],
        "norm_std":  [float(x) for x in norm_std],
        "labels": cfg["labels"],
        "framework": "pytorch-2.x",
        "onnx_opset": opset,
        "onnxruntime": cfg.get("onnxruntime", "1.19.2"),
        "training_seed": int(training_seed),
    }
    with open(out_dir / "model.json", "w", encoding="utf-8") as f:
        json.dump(model_json, f, ensure_ascii=False, indent=2)
    print("Wrote:", out_dir / "model.json")

if __name__ == "__main__":
    main()
