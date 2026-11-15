namespace AxDeck;

// 分组之间拖动用的数据格式。
// 分组里的项拖动时不放在剪贴板格式里：放上去的话，Windows 桌面会当场把它收走，我们就来不及按「拖出」的规则处理它。
// 有了自己的格式，只有 AxDeck 的分组接得住；落在别处时，拖动结束，我们再按自己的规则把它放到桌面上。
internal static class BoxDrag
{
    // 被拖动的那一项：真的文件是它的路径，系统项是 "shell::" 加它的编号。
    public const string Format = "AxDeck.BoxItem";
    // 它是从哪个分组里被拖出来的。
    public const string BoxId = "AxDeck.BoxId";
}
