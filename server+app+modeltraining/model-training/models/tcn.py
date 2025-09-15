import torch
import torch.nn as nn


class Chomp1d(nn.Module):
    def __init__(self, chomp_size):
        super().__init__()
        self.chomp_size = chomp_size

    def forward(self, x):
        return x[:, :, :-self.chomp_size].contiguous() if self.chomp_size > 0 else x


class TemporalBlock(nn.Module):
    def __init__(self, in_ch, out_ch, kernel_size, dilation, dropout=0.1):
        super().__init__()
        # 计算padding以确保输出长度不变
        padding = (kernel_size - 1) * dilation

        self.conv1 = nn.Conv1d(in_ch, out_ch, kernel_size,
                               padding=padding, dilation=dilation)
        self.chomp1 = Chomp1d(padding)
        self.relu1 = nn.ReLU()
        self.dropout1 = nn.Dropout(dropout)

        self.conv2 = nn.Conv1d(out_ch, out_ch, kernel_size,
                               padding=padding, dilation=dilation)
        self.chomp2 = Chomp1d(padding)
        self.relu2 = nn.ReLU()
        self.dropout2 = nn.Dropout(dropout)

        self.downsample = nn.Conv1d(in_ch, out_ch, 1) if in_ch != out_ch else nn.Identity()
        self.relu = nn.ReLU()

    def forward(self, x):
        out = self.conv1(x)
        out = self.chomp1(out)
        out = self.relu1(out)
        out = self.dropout1(out)

        out = self.conv2(out)
        out = self.chomp2(out)
        out = self.relu2(out)
        out = self.dropout2(out)

        res = self.downsample(x)
        return self.relu(out + res)


class TCN(nn.Module):
    def __init__(self, n_feats=5, channels=(32, 64, 64), kernel_size=3,
                 dropout=0.1, n_classes=5, seq_len=60):
        super().__init__()
        layers = []
        in_ch = n_feats

        # 为每一层创建TemporalBlock，每层dilation翻倍
        for i, out_ch in enumerate(channels):
            dilation = 2 ** i  # 膨胀因子：1, 2, 4, 8, ...
            layers.append(TemporalBlock(in_ch, out_ch, kernel_size, dilation, dropout))
            in_ch = out_ch

        self.tcn = nn.Sequential(*layers)
        self.pool = nn.AdaptiveAvgPool1d(1)
        self.head = nn.Linear(in_ch, n_classes)

        # 初始化权重
        self._init_weights()

    def _init_weights(self):
        for m in self.modules():
            if isinstance(m, nn.Conv1d):
                nn.init.kaiming_normal_(m.weight, nonlinearity='relu')
                if m.bias is not None:
                    nn.init.constant_(m.bias, 0)
            elif isinstance(m, nn.Linear):
                nn.init.xavier_normal_(m.weight)
                nn.init.constant_(m.bias, 0)

    def forward(self, x):  # x: B×T×F (batch_size, seq_len, n_features)
        x = x.transpose(1, 2)  # B×F×T (batch_size, n_features, seq_len)
        h = self.tcn(x)
        h = self.pool(h).squeeze(-1)  # B×C (batch_size, channels[-1])
        return self.head(h)  # B×n_classes


# 测试代码
if __name__ == "__main__":
    # 创建测试数据
    batch_size, seq_len, n_features = 32, 60, 5
    x = torch.randn(batch_size, seq_len, n_features)

    # 创建模型
    model = TCN(n_feats=n_features, n_classes=5)

    # 前向传播测试
    output = model(x)
    print(f"Input shape: {x.shape}")
    print(f"Output shape: {output.shape}")
    print(f"Model parameters: {sum(p.numel() for p in model.parameters()):,}")

    # 验证输出
    assert output.shape == (batch_size, 5), f"Expected output shape (32, 5), got {output.shape}"
    print("✅ Model works correctly!")