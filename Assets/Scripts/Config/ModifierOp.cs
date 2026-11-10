// 数值修正的作用方式（强化条目 / 被动条目共用）
//
// ⚠️ Multiply 必须是 0：既有 .asset / 场景里没有这个字段，
//    反序列化后默认取 0，自动保持历史上"乘算"的语义，资产零改动。
public enum ModifierOp
{
    Multiply,   // 比例修正：value = 0.15 → +15%（改 xxxMult）
    Add         // 绝对修正：value = 2   → +2 点（改 xxxAdd）
}

// 修正模型的统一约定（PenController 落地）：
//   最终值 = (基准 + 加算和) × 乘算积
//   加算先于乘算；每小局 RestoreBaseValues() 把 Add 归 0、Mult 归 1。
public static class ModifierOpInfo
{
    // Inspector 里"方式"下拉的显示名，顺序必须与枚举声明顺序一致
    public static readonly string[] DisplayNames = { "乘算（比例）", "加算（绝对点数）" };

    public static string GetName(ModifierOp operationType) =>
        operationType == ModifierOp.Add ? "加算（绝对点数）" : "乘算（比例）";
}
