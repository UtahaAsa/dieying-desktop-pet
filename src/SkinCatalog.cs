namespace DieYing
{
    /** <summary>稳定衣装编号；名称按身体区分，灵魂身份单独展示。仅含不可变数据。</summary> */
    internal static class SkinCatalog
    {
        internal const int Count = 4;
        internal static readonly string[] Names = { "黄玲琳 · 常服", "黄玲琳 · 打歌服", "朱慧月 · 常服", "朱慧月 · 打歌服" };
        internal static string Name(int skin) { return Names[System.Math.Max(0,System.Math.Min(Count-1,skin))]; }
    }
}
