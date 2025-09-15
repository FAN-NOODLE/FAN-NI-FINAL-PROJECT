# scripts/train.py
import argparse, json, yaml, os, sys
from pathlib import Path
import numpy as np, pandas as pd
import torch, torch.nn as nn
from torch.utils.data import Dataset, DataLoader
from sklearn.preprocessing import LabelEncoder
from sklearn.metrics import f1_score, precision_recall_fscore_support, confusion_matrix
import matplotlib.pyplot as plt
import seaborn as sns

# --------（可选）确保能找到 models/ 包--------
ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.append(str(ROOT))

# ===================== 实用函数 =====================

def set_seed(seed: int = 42):
    os.environ["PYTHONHASHSEED"] = str(seed)
    import random
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)
    try:
        torch.use_deterministic_algorithms(True)
    except Exception:
        pass
    torch.backends.cudnn.benchmark = False

def load_cfg(path: str):
    for enc in ("utf-8", "utf-8-sig", "gbk", "latin-1"):
        try:
            with open(path, 'r', encoding=enc) as f:
                return yaml.safe_load(f)
        except Exception:
            continue
    raise RuntimeError(f"Failed to read YAML config: {path}")

# ===================== 数据集与加载 =====================

class WindowDataset(Dataset):
    """从逐秒表构造 (12,5) 滑窗；窗尾标签作为监督"""
    def __init__(self, path, feat_order, input_len=12):
        self.fo = feat_order
        self.L = input_len
        if str(path).endswith('.parquet'):
            df = pd.read_parquet(path)
        else:
            df = pd.read_csv(path)
        need = set(['time','subject_id','session_id','activity','label'] + list(feat_order))
        miss = need - set(df.columns)
        if miss:
            raise ValueError(f"Missing columns in {path}: {sorted(miss)}")
        self.df = df
        self._build()

    def _build(self):
        X, y = [], []
        for _, g in self.df.sort_values('time').groupby(['subject_id', 'session_id', 'activity'], dropna=False):
            g = g.reset_index(drop=True)
            if len(g) < self.L:
                continue
            for i in range(len(g) - self.L + 1):
                seg = g.iloc[i:i + self.L]  # ← 这里必须是方括号 []
                X.append(seg[self.fo].to_numpy('float32'))
                y.append(seg.iloc[-1]['label'])
        if len(X) == 0:
            raise RuntimeError("No samples constructed. Check input_len / data continuity.")
        self.X = np.stack(X)  # (N, 12, 5)
        self.y_raw = np.array(y)  # (N,)

    def set_encoder(self, le: LabelEncoder, cfg_labels):
        # 若数据里是数字标签则尝试按索引映射回类名
        if not np.all(np.isin(self.y_raw, cfg_labels)):
            if np.issubdtype(self.y_raw.dtype, np.number):
                idx = self.y_raw.astype(int)
                if idx.min() >= 0 and idx.max() < len(cfg_labels):
                    self.y_raw = np.array([cfg_labels[i] for i in idx])
        if not np.all(np.isin(self.y_raw, cfg_labels)):
            uniq = sorted(list({str(x) for x in np.unique(self.y_raw)}))
            raise ValueError(f"Labels in data {uniq} do not match cfg labels {cfg_labels}.")
        self.le = le
        self.y = le.transform(self.y_raw)

    def compute_norm_stats(self):
        Xcat = self.X.reshape(-1, self.X.shape[-1])  # (N*12, 5)
        mean = Xcat.mean(axis=0).astype('float32')
        std  = Xcat.std(axis=0).astype('float32') + 1e-6
        return mean, std

    def __len__(self): return len(self.X)

    def __getitem__(self, i):
        x = np.nan_to_num(self.X[i], nan=0.0, posinf=0.0, neginf=0.0)
        y = int(self.y[i])
        return torch.from_numpy(x), torch.tensor(y, dtype=torch.long)

def build_loaders(train_path, val_path, cfg):
    ds_tr = WindowDataset(train_path, cfg['feat_order'], cfg['input_len'])
    ds_va = WindowDataset(val_path,   cfg['feat_order'], cfg['input_len'])

    le = LabelEncoder(); le.fit(cfg['labels'])
    ds_tr.set_encoder(le, cfg['labels']); ds_va.set_encoder(le, cfg['labels'])

    mean, std = ds_tr.compute_norm_stats()
    mean_t = torch.from_numpy(mean)  # (5,)
    std_t  = torch.from_numpy(std)   # (5,)

    def collate(batch):
        xb = torch.stack([b[0] for b in batch])  # (B,12,5)
        yb = torch.stack([b[1] for b in batch])
        xb = (xb - mean_t) / std_t               # 广播
        return xb, yb

    num_workers = int(cfg['train'].get('num_workers', 0) or 0)  # Windows 建议 0
    tr_loader = DataLoader(
        ds_tr, batch_size=cfg['train']['batch_size'], shuffle=True,
        num_workers=num_workers, collate_fn=collate, drop_last=False
    )
    va_loader = DataLoader(
        ds_va, batch_size=1024, shuffle=False,
        num_workers=num_workers, collate_fn=collate, drop_last=False
    )
    return ds_tr, tr_loader, va_loader, mean, std

# ===================== 模型（需有 models/tcn.py 或 models/bilstm.py） =====================

from models.tcn import TCN
from models.bilstm import BiLSTM

def create_model(cfg, device):
    n_classes = len(cfg['labels'])
    if cfg['model'] == 'TCN':
        model = TCN(
            n_feats=cfg['n_feats'],
            channels=cfg['channels'],
            kernel_size=cfg['kernel_size'],
            dropout=cfg['dropout'],
            n_classes=n_classes,
            seq_len=cfg['input_len'],
        )
    else:
        model = BiLSTM(
            n_feats=cfg['n_feats'],
            hidden_size=cfg['hidden_size'],
            num_layers=cfg['num_layers'],
            attn=cfg['attn'],
            dropout=cfg['dropout'],
            n_classes=n_classes,
        )
    return model.to(device)

# ===================== 损失 / 评估 =====================

class LabelSmoothingCE(nn.Module):
    """返回逐样本损失（reduction='none'），便于与类别权重逐样本相乘"""
    def __init__(self, smoothing=0.1, reduction='none'):
        super().__init__()
        self.smoothing = float(smoothing)
        self.reduction = reduction
        self.log_softmax = nn.LogSoftmax(dim=-1)

    def forward(self, logits, target):
        n = logits.size(-1)
        logp = self.log_softmax(logits)              # (B, C)
        with torch.no_grad():
            true = torch.zeros_like(logp).fill_(self.smoothing/(n-1))
            true.scatter_(1, target.unsqueeze(1), 1.0-self.smoothing)
        per_sample = torch.sum(-true * logp, dim=-1) # (B,)
        if self.reduction == 'mean':
            return per_sample.mean()
        if self.reduction == 'sum':
            return per_sample.sum()
        return per_sample

@torch.no_grad()
def evaluate(model, loader, device, labels):
    model.eval()
    y_true, y_pred = [], []
    for xb, yb in loader:
        xb, yb = xb.to(device), yb.to(device)
        logits = model(xb)
        pred = logits.argmax(dim=-1)
        y_true.extend(yb.cpu().tolist())
        y_pred.extend(pred.cpu().tolist())
    macro_f1 = f1_score(y_true, y_pred, average='macro', zero_division=0)
    pr, rc, f1, _ = precision_recall_fscore_support(y_true, y_pred, average=None, zero_division=0)
    cm = confusion_matrix(y_true, y_pred, labels=list(range(len(labels))))
    return macro_f1, pr.tolist(), rc.tolist(), f1.tolist(), cm

# ===================== 主程序 =====================

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--cfg', required=True)
    ap.add_argument('--train', required=True)
    ap.add_argument('--val', required=True)
    ap.add_argument('--out', required=True)
    args = ap.parse_args()

    cfg = load_cfg(args.cfg)
    print(f"Successfully loaded config from {args.cfg}")
    set_seed(cfg.get('seed', 42))

    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print('Using device:', device)

    out_dir = Path(args.out); (out_dir/"checkpoints").mkdir(parents=True, exist_ok=True)
    with open(out_dir/"config.yaml", 'w', encoding='utf-8') as f:
        yaml.safe_dump(cfg, f, allow_unicode=True)

    # 数据
    ds_tr, tr_loader, va_loader, mean, std = build_loaders(args.train, args.val, cfg)

    # 模型
    model = create_model(cfg, device)
    print(f"Model parameters: {sum(p.numel() for p in model.parameters()):,}")

    # 类别权重（按训练频率逆比）
    class_counts = np.bincount(ds_tr.y, minlength=len(cfg['labels'])).astype('float32')
    w_np = class_counts.sum() / (class_counts + 1e-6)
    w = torch.tensor((w_np / w_np.mean()).astype('float32'), device=device) if cfg['train'].get('class_weight', True) else None
    # 可选打印：观察权重
    # print("class_counts:", class_counts, "\nweights:", None if w is None else w.cpu().numpy())

    # 损失
    if cfg['train'].get('label_smoothing', 0.0) > 0:
        ce = LabelSmoothingCE(cfg['train']['label_smoothing'], reduction='none')
        def loss_fn(logits, target):
            l = ce(logits, target)          # (B,)
            if w is not None:
                l = l * w[target]           # 按样本类别权重
            return l.mean()
    else:
        loss_fn = nn.CrossEntropyLoss(weight=w)

    # 优化 & 调度（按 epoch 调度）
    opt = torch.optim.AdamW(model.parameters(), lr=cfg['train']['lr'], weight_decay=cfg['train']['weight_decay'])
    sch = torch.optim.lr_scheduler.CosineAnnealingLR(opt, T_max=cfg['train']['epochs']) if cfg['train'].get('cosine_decay', True) else None

    # 仅在 CUDA 且 amp=True 时启用混合精度
    use_amp = (device.type == 'cuda') and bool(cfg['train'].get('amp', False))
    scaler = torch.amp.GradScaler('cuda') if use_amp else None

    best_f1, patience = -1.0, cfg['train'].get('early_stop_patience', 10)

    # 可选：首批次 sanity check
    DEBUG_FIRST_BATCH = False  # 如需查看真实损失量级，改为 True
    if DEBUG_FIRST_BATCH:
        model.train()
        xb, yb = next(iter(tr_loader))
        xb, yb = xb.to(device), yb.to(device)
        logits = model(xb)
        main_loss = loss_fn(logits, yb).detach().item()
        ce_plain = nn.CrossEntropyLoss()(logits, yb).detach().item()
        print(f"[DEBUG] first-batch: main_loss={main_loss:.6e}, ce_plain={ce_plain:.6e}, xb_shape={tuple(xb.shape)}")

    print("Starting training...")
    for epoch in range(1, cfg['train']['epochs'] + 1):
        model.train()
        total = 0.0
        for xb, yb in tr_loader:
            xb, yb = xb.to(device), yb.to(device)
            opt.zero_grad(set_to_none=True)

            if use_amp:
                with torch.amp.autocast('cuda'):
                    logits = model(xb)
                    loss = loss_fn(logits, yb)
                scaler.scale(loss).backward()
                scaler.step(opt)
                scaler.update()
            else:
                logits = model(xb)
                loss = loss_fn(logits, yb)
                loss.backward()
                opt.step()

            total += loss.detach().item()

        if sch: sch.step()

        # 验证
        macro_f1, pr, rc, f1c, cm = evaluate(model, va_loader, device, cfg['labels'])
        avg_loss = total / max(1, len(tr_loader))
        # 用科学计数法展示，避免 0.0000 的错觉
        print(f"epoch {epoch:03d} | loss {avg_loss:.6e} | va_macro_f1 {macro_f1:.4f}")

        # 早停
        if macro_f1 > best_f1 + 1e-4:
            best_f1 = macro_f1
            patience = cfg['train'].get('early_stop_patience', 10)
            torch.save(model.state_dict(), out_dir / "checkpoints" / "best.pt")
            print(f"↳ New best model saved (F1={macro_f1:.4f})")
        else:
            patience -= 1
            if patience <= 0:
                print("Early stopping triggered.")
                break

    # 最终评估（用 best.pt）
    print("Loading best model for final evaluation...")
    model.load_state_dict(torch.load(out_dir / "checkpoints" / "best.pt", map_location=device))
    macro_f1, pr, rc, f1c, cm = evaluate(model, va_loader, device, cfg['labels'])

    metrics = {
        'macro_f1': float(macro_f1),
        'per_class_precision': pr,
        'per_class_recall': rc,
        'per_class_f1': f1c,
        'labels': cfg['labels'],
    }

    # 混淆矩阵图
    plt.figure(figsize=(5, 4))
    sns.heatmap(cm, annot=True, fmt='d', cmap='Blues',
                xticklabels=cfg['labels'], yticklabels=cfg['labels'])
    plt.xlabel('Pred'); plt.ylabel('True'); plt.tight_layout()
    out_dir.mkdir(parents=True, exist_ok=True)
    plt.savefig(out_dir / "confusion_matrix.png", dpi=160)
    plt.close()

    # 保存指标与元数据
    with open(out_dir / "metrics.yaml", 'w', encoding='utf-8') as f:
        yaml.safe_dump(metrics, f, allow_unicode=True)

    meta = {
        'version': cfg.get('version', 'v1.0.0'),
        'feat_order': cfg['feat_order'],
        'norm_mean': [float(x) for x in mean],
        'norm_std':  [float(x) for x in std],
        'labels': cfg['labels'],
        'framework': 'pytorch-2.x',
        'onnx_opset': cfg.get('opset', 13),
        'onnxruntime': cfg.get('onnxruntime', '1.19.2'),
        'training_seed': cfg.get('seed', 42),
    }
    with open(out_dir / "model_meta.json", 'w', encoding='utf-8') as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)

    print(f"Training completed! Best macro F1: {best_f1:.4f}")
    print(f"Results saved to: {out_dir}")

if __name__ == '__main__':
    main()
