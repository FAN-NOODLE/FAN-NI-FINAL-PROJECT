# ipconfig
# python -m uvicorn simple_server:app --host 0.0.0.0 --port 8000

# simple_server.py
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field, conlist
from typing import List, Dict, Optional
from pathlib import Path
import time, os, json
import numpy as np
import onnxruntime as ort

# =========================
# 配置 & 环境变量
# =========================
DEFAULT_MODEL_DIR = Path(__file__).resolve().parent / "outputs" / "v1"
MODEL_DIR = Path(os.getenv("EMOTION_MODEL_DIR", str(DEFAULT_MODEL_DIR)))
MODEL_JSON = MODEL_DIR / "model.json"
MODEL_ONNX = MODEL_DIR / "model.onnx"

# Softmax 温度（>1 变平滑；<1 变尖锐）
SOFTMAX_TEMPERATURE = float(os.getenv("SOFTMAX_TEMPERATURE", "2.0"))

# EMA 平滑（0~1；0=不用EMA，0.2=轻微平滑，0.5=较强平滑）
EMA_ALPHA = float(os.getenv("EMA_ALPHA", "0.2"))

# 是否启用归一化热修（线上止血），默认关闭；设为 "1" 开启
ENABLE_NORM_HOTFIX = os.getenv("ENABLE_NORM_HOTFIX", "0") == "1"

# 限幅阈值，防止极端 z 值导致饱和
Z_CLIP = float(os.getenv("Z_CLIP", "6.0"))

# onnxruntime providers
PROVIDERS = (
    ["CUDAExecutionProvider", "CPUExecutionProvider"]
    if os.getenv("ORT_USE_CUDA", "0") == "1"
    else ["CPUExecutionProvider"]
)

# CORS
ALLOW_ORIGINS = os.getenv("CORS_ALLOW_ORIGINS", "*").split(",") if os.getenv("CORS_ALLOW_ORIGINS") else ["*"]

# 调试输出 z 值（关闭可减少日志）
DEBUG_Z = os.getenv("DEBUG_Z", "0") == "1"

# =========================
# 读取模型 & 元数据
# =========================
if not MODEL_JSON.exists() or not MODEL_ONNX.exists():
    raise RuntimeError(f"Missing model files in {MODEL_DIR}")

_meta = json.loads(MODEL_JSON.read_text(encoding="utf-8"))
FEAT_ORDER: List[str] = _meta["feat_order"]
LABELS: List[str] = _meta["labels"]
VERSION: str = _meta.get("version", "v1.0.0")
_NORM_MEAN = np.array(_meta["norm_mean"], dtype=np.float32)
_NORM_STD  = np.array(_meta["norm_std"],  dtype=np.float32)

# 可选：归一化参数热修（仅线上止血，需 ENABLE_NORM_HOTFIX=1）
OVERRIDE_NORM: Optional[Dict[str, Dict[str, float]]] = {
    # 示例：根据你之前的线上诊断，可以在这里微调。不开启不会生效。
    # "HR_mean":   {"mean": 75.0, "std": 15.0},
    # "HR_slope":  {"mean":  0.0, "std":  2.0},
    # "ACC_rms":   {"mean":  0.2, "std":  0.2},
    # "ACC_energy":{"mean":  0.3, "std":  0.3},
    # "ACC_zcr":   {"mean":  0.04,"std":  0.04},
} if ENABLE_NORM_HOTFIX else None

if OVERRIDE_NORM:
    for i, name in enumerate(FEAT_ORDER):
        if name in OVERRIDE_NORM:
            _NORM_MEAN[i] = OVERRIDE_NORM[name]["mean"]
            _NORM_STD[i]  = OVERRIDE_NORM[name]["std"]
    print("[norm-hotfix] Applied:", OVERRIDE_NORM)

print(f"[model] dir={MODEL_DIR}")
print(f"[model] feat_order={FEAT_ORDER}")
print(f"[model] labels={LABELS}")
print(f"[model] norm_mean={_NORM_MEAN}")
print(f"[model] norm_std={_NORM_STD}")
print(f"[runtime] providers={PROVIDERS}")
print(f"[runtime] temperature={SOFTMAX_TEMPERATURE}  ema_alpha={EMA_ALPHA}  z_clip={Z_CLIP}")

# onnxruntime 会话
_SESSION = ort.InferenceSession(str(MODEL_ONNX), providers=PROVIDERS)
_INPUT_NAME = _SESSION.get_inputs()[0].name
_OUTPUT_NAME = _SESSION.get_outputs()[0].name

# 预热
_ = _SESSION.run([_OUTPUT_NAME], {_INPUT_NAME: np.zeros((1, 12, len(FEAT_ORDER)), np.float32)})

# =========================
# FastAPI app
# =========================
app = FastAPI(title="Emotion Server", version=VERSION)
app.add_middleware(
    CORSMiddleware,
    allow_origins=ALLOW_ORIGINS,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# =========================
# Pydantic 模型
# =========================
class SecondFeature(BaseModel):
    HR_mean: float = 0.0
    HR_slope: float = 0.0
    ACC_rms: float = 0.0
    ACC_energy: float = 0.0
    ACC_zcr: float = 0.0

class PredictRequest(BaseModel):
    ts: float = Field(default_factory=lambda: time.time())
    feature_window: conlist(SecondFeature, min_length=12, max_length=12)
    quality_hint: float = Field(ge=0.0, le=1.0, default=1.0)

class PredictResponse(BaseModel):
    ts: float
    probs: Dict[str, float]
    label: str
    confidence: float
    quality: float

# =========================
# 工具
# =========================
def softmax_np(x: np.ndarray, T: float = 1.0) -> np.ndarray:
    z = x / max(T, 1e-6)
    z = z - np.max(z, keepdims=True)
    e = np.exp(z).astype(np.float64)
    return (e / e.sum(keepdims=True)).astype(np.float32)

LOG_DIR = "Logs"
os.makedirs(LOG_DIR, exist_ok=True)

def append_log(ts: float, probs: Dict[str, float], label: str, conf: float, quality: float):
    path = os.path.join(LOG_DIR, time.strftime("session_%Y%m%d.csv"))
    with open(path, "a", encoding="utf-8") as f:
        row = [f"{ts}"] + [str(probs[k]) for k in LABELS] + [label, str(conf), str(quality)]
        f.write(",".join(row) + "\n")

# =========================
# 全局状态（给 /latest 用）
# =========================
_LAST_RESPONSE: Optional[Dict] = None
_LAST_EMA_PROBS: Optional[np.ndarray] = None  # EMA 平滑

# =========================
# 路由
# =========================
@app.get("/health")
def health():
    return {
        "status": "ok",
        "feat_order": FEAT_ORDER,
        "labels": LABELS,
        "version": VERSION,
        "providers": PROVIDERS,
        "norm_mean": _NORM_MEAN.tolist(),
        "norm_std": _NORM_STD.tolist(),
        "softmax_temperature": SOFTMAX_TEMPERATURE,
        "ema_alpha": EMA_ALPHA,
        "z_clip": Z_CLIP,
        "norm_hotfix": bool(OVERRIDE_NORM is not None),
        "cors_allow_origins": ALLOW_ORIGINS,
    }

@app.post("/predict_from_features", response_model=PredictResponse)
def predict(req: PredictRequest):
    global _LAST_RESPONSE, _LAST_EMA_PROBS

    # 控制台观测
    try:
        last = req.feature_window[-1]
        print(f"[recv] ts={req.ts} q={req.quality_hint:.2f} "
              f"HR_last={last.HR_mean:.2f} ACC_rms_last={last.ACC_rms:.3f}")
    except Exception:
        pass

    # 1) 组装 (12,5)
    try:
        W = np.array([[getattr(s, k) for k in FEAT_ORDER] for s in req.feature_window], dtype=np.float32)  # (12,5)
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"feature_window parse error: {e}")

    # 2) 清洗 + 归一化 + 限幅
    W = np.nan_to_num(W, nan=0.0, posinf=0.0, neginf=0.0)
    Z = (W - _NORM_MEAN) / (_NORM_STD + 1e-6)  # (12,5)

    if DEBUG_Z:
        for j, name in enumerate(FEAT_ORDER):
            col = Z[:, j]
            print(f"[z] {name:<10} min={col.min():+.3f} max={col.max():+.3f} mean={col.mean():+.3f}")

    Z = np.clip(Z, -Z_CLIP, Z_CLIP)
    if DEBUG_Z:
        print("[win] Wn stats: min=", float(Z.min()), "max=", float(Z.max()), "mean=", float(Z.mean()))

    X = Z.reshape(1, 12, len(FEAT_ORDER)).astype(np.float32)  # (1,12,5)

    # 3) 推理 + 温度缩放 +（可选）EMA 平滑
    logits = _SESSION.run([_OUTPUT_NAME], {_INPUT_NAME: X})[0][0]  # (5,)
    probs = softmax_np(logits, T=SOFTMAX_TEMPERATURE)               # (5,)

    if EMA_ALPHA > 0.0:
        if _LAST_EMA_PROBS is None:
            _LAST_EMA_PROBS = probs.copy()
        else:
            _LAST_EMA_PROBS = EMA_ALPHA * probs + (1.0 - EMA_ALPHA) * _LAST_EMA_PROBS
        probs_use = _LAST_EMA_PROBS
    else:
        probs_use = probs

    idx = int(np.argmax(probs_use))
    label = LABELS[idx]
    confidence = float(probs_use[idx])

    resp = {
        "ts": float(req.ts),
        "probs": {LABELS[i]: float(probs_use[i]) for i in range(len(LABELS))},
        "label": label,
        "confidence": round(confidence, 6),
        "quality": round(float(req.quality_hint), 6),
    }

    # 控制台输出预测
    print(
        f"[pred] label={label} conf={confidence:.3f} "
        + "probs=" + " ".join(f"{LABELS[i]}={probs_use[i]:.3f}" for i in range(len(LABELS)))
    )

    # 保存“最近一次”结果（给 /latest 用）
    _LAST_RESPONSE = resp

    # 可选：本地落盘
    append_log(resp["ts"], resp["probs"], resp["label"], resp["confidence"], resp["quality"])
    return resp

@app.get("/latest", response_model=PredictResponse)
def latest():
    if _LAST_RESPONSE is None:
        raise HTTPException(status_code=404, detail="No prediction yet")
    return _LAST_RESPONSE
