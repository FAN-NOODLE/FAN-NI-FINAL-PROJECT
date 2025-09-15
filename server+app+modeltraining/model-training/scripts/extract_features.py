"""
（可选）示例：将原始加速度 25Hz 与心率流对齐到秒级特征。
实际实现请替换为你的数据管道；此处给出占位与字段示意。
"""
import pandas as pd


def example_pipeline(acc_raw: pd.DataFrame, hr_raw: pd.DataFrame) -> pd.DataFrame:
# 假设 acc_raw: [ts, ax, ay, az], 25Hz；hr_raw: [ts, hr]
# 1) 统一到 1Hz time index
# 2) 计算 ACC 模值特征：RMS、能量、ZCR
# 3) 计算 HR_mean 与 HR_slope
# 返回列: time, HR_mean, HR_slope, ACC_rms, ACC_energy, ACC_zcr, subject_id, session_id, activity, label
raise NotImplementedError