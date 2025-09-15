import torch
import torch.nn as nn
import torch.nn.functional as F


class BiLSTM(nn.Module):
    def __init__(self, n_feats=5, hidden_size=64, num_layers=1, attn=True, dropout=0.1, n_classes=5):
        super().__init__()
        self.attn = attn
        self.num_layers = num_layers
        self.hidden_size = hidden_size

        # LSTM层
        self.lstm = nn.LSTM(
            input_size=n_feats,
            hidden_size=hidden_size,
            num_layers=num_layers,
            batch_first=True,
            dropout=dropout if num_layers > 1 else 0.0,
            bidirectional=True
        )

        # 注意力机制
        d_model = hidden_size * 2  # 双向LSTM的输出维度
        if attn:
            self.attn_linear = nn.Linear(d_model, 1)

        # 分类头
        self.head = nn.Linear(d_model, n_classes)
        self.dropout = nn.Dropout(dropout)

        # 初始化权重
        self._init_weights()

    def _init_weights(self):
        """初始化权重"""
        for name, param in self.named_parameters():
            if 'weight' in name:
                if 'lstm' in name:
                    # LSTM权重使用正交初始化
                    if len(param.shape) >= 2:
                        nn.init.orthogonal_(param)
                elif 'attn' in name or 'head' in name:
                    # 线性层使用Xavier初始化
                    nn.init.xavier_uniform_(param)
            elif 'bias' in name:
                nn.init.constant_(param, 0.0)

    def forward(self, x):
        """
        Args:
            x: 输入张量，形状为 [batch_size, seq_len, n_feats]
        Returns:
            输出张量，形状为 [batch_size, n_classes]
        """
        # LSTM前向传播
        lstm_out, (hidden, cell) = self.lstm(x)  # lstm_out: [batch_size, seq_len, hidden_size*2]

        # 应用注意力或使用最后一个时间步
        if self.attn:
            # 注意力权重计算
            attention_scores = self.attn_linear(lstm_out)  # [batch_size, seq_len, 1]
            attention_weights = F.softmax(attention_scores, dim=1)  # [batch_size, seq_len, 1]

            # 加权求和
            context_vector = torch.sum(lstm_out * attention_weights, dim=1)  # [batch_size, hidden_size*2]
        else:
            # 使用双向LSTM的最后一个时间步的拼接
            # 前向和后向的最后一个隐藏状态
            forward_last = hidden[-2, :, :]  # 前向的最后一个隐藏状态
            backward_last = hidden[-1, :, :]  # 后向的最后一个隐藏状态
            context_vector = torch.cat([forward_last, backward_last], dim=1)  # [batch_size, hidden_size*2]

        # Dropout和分类
        context_vector = self.dropout(context_vector)
        output = self.head(context_vector)

        return output


# 测试代码
if __name__ == "__main__":
    # 创建测试数据
    batch_size, seq_len, n_features = 32, 60, 5
    x = torch.randn(batch_size, seq_len, n_features)

    # 测试带注意力的BiLSTM
    print("Testing BiLSTM with attention:")
    model_attn = BiLSTM(n_feats=n_features, hidden_size=64, num_layers=2,
                        attn=True, dropout=0.1, n_classes=5)

    output_attn = model_attn(x)
    print(f"Input shape: {x.shape}")
    print(f"Output shape: {output_attn.shape}")
    print(f"Model parameters: {sum(p.numel() for p in model_attn.parameters()):,}")

    # 测试不带注意力的BiLSTM
    print("\nTesting BiLSTM without attention:")
    model_no_attn = BiLSTM(n_feats=n_features, hidden_size=64, num_layers=2,
                           attn=False, dropout=0.1, n_classes=5)

    output_no_attn = model_no_attn(x)
    print(f"Output shape: {output_no_attn.shape}")
    print(f"Model parameters: {sum(p.numel() for p in model_no_attn.parameters()):,}")

    # 验证输出
    assert output_attn.shape == (batch_size, 5), f"Expected (32, 5), got {output_attn.shape}"
    assert output_no_attn.shape == (batch_size, 5), f"Expected (32, 5), got {output_no_attn.shape}"
    print("✅ Both models work correctly!")