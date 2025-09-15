import argparse
import sys
import random
import json
from pathlib import Path
import pandas as pd


def read_df(file_path):
    """读取数据文件，支持parquet和csv格式"""
    path = Path(file_path)
    if path.suffix.lower() == '.parquet':
        return pd.read_parquet(file_path)
    elif path.suffix.lower() == '.csv':
        return pd.read_csv(file_path)
    else:
        raise ValueError(f"Unsupported file format: {path.suffix}")


def write_df(df, file_path):
    """写入数据文件，支持parquet和csv格式"""
    path = Path(file_path)
    path.parent.mkdir(parents=True, exist_ok=True)

    if path.suffix.lower() == '.parquet':
        df.to_parquet(file_path, index=False)
    elif path.suffix.lower() == '.csv':
        df.to_csv(file_path, index=False)
    else:
        raise ValueError(f"Unsupported file format: {path.suffix}")


def parse_subjects(args, all_subs):
    """解析被试划分方式"""
    # 如果指定了具体的被试列表
    tr = args.train_subjects or []
    va = args.val_subjects or []
    te = args.test_subjects or []

    if tr or va or te:
        # 检查是否有重叠的被试
        dup = set(tr) & set(va) | set(tr) & set(te) | set(va) & set(te)
        if dup:
            raise ValueError(f"Subjects overlap across splits: {sorted(dup)}")

        # 检查是否有未知的被试
        unknown = (set(tr) | set(va) | set(te)) - set(all_subs)
        if unknown:
            raise ValueError(f"Unknown subjects not in dataset: {sorted(unknown)}")

        return tr, va, te

    # 数量随机拆分
    n_tr, n_va, n_te = args.train_n, args.val_n, args.test_n
    if n_tr + n_va + n_te > len(all_subs):
        raise ValueError(f"Split sizes exceed available subjects ({len(all_subs)}): {n_tr}+{n_va}+{n_te}")

    subs = list(all_subs)
    rng = random.Random(args.seed)
    rng.shuffle(subs)

    tr = subs[:n_tr]
    va = subs[n_tr:n_tr + n_va]
    te = subs[n_tr + n_va:n_tr + n_va + n_te]

    return tr, va, te


def summarize(df, name):
    """生成数据摘要信息"""
    cnt = len(df)
    subj = df['subject_id'].nunique()
    sess = df['session_id'].nunique() if 'session_id' in df.columns else None

    # 标签分布（如果存在label列）
    if 'label' in df.columns:
        by_label = df['label'].value_counts().to_dict()
        label_dist = {str(k): int(v) for k, v in by_label.items()}
    else:
        label_dist = {}

    return {
        'split': name,
        'rows': int(cnt),
        'subjects': int(subj),
        'sessions': int(sess) if sess is not None else None,
        'label_dist': label_dist,
    }


def main():
    # 参数解析
    ap = argparse.ArgumentParser(description='Split subjects into train/validation/test sets')
    ap.add_argument('--input', required=True, help='Input data file (parquet or csv)')
    ap.add_argument('--train_out', required=True, help='Output file for training set')
    ap.add_argument('--val_out', required=True, help='Output file for validation set')
    ap.add_argument('--test_out', required=True, help='Output file for test set')
    ap.add_argument('--seed', type=int, default=42, help='Random seed for shuffling')
    ap.add_argument('--train_n', type=int, default=20, help='Number of training subjects')
    ap.add_argument('--val_n', type=int, default=6, help='Number of validation subjects')
    ap.add_argument('--test_n', type=int, default=6, help='Number of test subjects')
    ap.add_argument('--train_subjects', nargs='*', help='Specific training subjects (overrides random split)')
    ap.add_argument('--val_subjects', nargs='*', help='Specific validation subjects (overrides random split)')
    ap.add_argument('--test_subjects', nargs='*', help='Specific test subjects (overrides random split)')

    args = ap.parse_args()

    try:
        # 读取数据
        df = read_df(args.input)
        if 'subject_id' not in df.columns:
            print('Error: column subject_id not found in input.', file=sys.stderr)
            sys.exit(2)

        # 获取所有被试
        all_subs = sorted(df['subject_id'].unique())
        print(f"Found {len(all_subs)} unique subjects: {all_subs}")

        # 解析被试划分
        tr, va, te = parse_subjects(args, all_subs)

        # 划分数据
        df_tr = df[df.subject_id.isin(tr)].copy()
        df_va = df[df.subject_id.isin(va)].copy()
        df_te = df[df.subject_id.isin(te)].copy()

        # 保存数据
        write_df(df_tr, args.train_out)
        write_df(df_va, args.val_out)
        write_df(df_te, args.test_out)

        # 生成并打印摘要
        summary = {
            'train_subjects': tr,
            'val_subjects': va,
            'test_subjects': te,
            'splits': [
                summarize(df_tr, 'train'),
                summarize(df_va, 'val'),
                summarize(df_te, 'test')
            ],
            'all_subjects': all_subs,
            'split_config': {
                'seed': args.seed,
                'train_n': args.train_n,
                'val_n': args.val_n,
                'test_n': args.test_n
            }
        }

        print(json.dumps(summary, ensure_ascii=False, indent=2))

        print(f"\n✅ Split completed successfully!")
        print(f"   Training set: {len(df_tr)} rows, {len(tr)} subjects")
        print(f"   Validation set: {len(df_va)} rows, {len(va)} subjects")
        print(f"   Test set: {len(df_te)} rows, {len(te)} subjects")

    except Exception as e:
        print(f"❌ Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()