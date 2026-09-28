using UnityEngine;

/// <summary>操作提示共用的按键名称，跟随组件上实际配置的按键。</summary>
public static class ControlHints
{
    public static string Key(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Space: return "空格";
            case KeyCode.Return: return "Enter";
            case KeyCode.Escape: return "Esc";
            case KeyCode.Tab: return "Tab";
            default: return key.ToString();
        }
    }

    public static string Inventory(KeyCode use, KeyCode journal, KeyCode close)
    {
        return "WASD / 方向键 选择物品 · 滚轮 翻动背包\n"
            + Key(use) + " 装备武器 / 草药操作 · E 镶嵌宝石（靠近宝石门）\n"
            + Key(journal) + " 纸条列表 → W/S 选择 → E 阅读 · " + Key(close) + " 关闭背包";
    }

    public static string Notes(KeyCode journal, KeyCode close)
    {
        return "WASD / 方向键 选择 · E 阅读\n"
            + Key(journal) + " / Esc 返回物品 · " + Key(close) + " 关闭背包";
    }
}
