namespace ClipboardManager.Core.Models;

/// <summary>
/// 剪贴板记录的内容类型。数值直接落库到 <c>clip_items.type</c>，不可随意调整。
/// </summary>
public enum ClipContentType
{
    /// <summary>纯文本（CF_UNICODETEXT）。</summary>
    Text = 1,

    /// <summary>富文本（"HTML Format" 注册格式）。</summary>
    Html = 2,

    /// <summary>图片 / 截图（CF_DIB / CF_DIBV5）。</summary>
    Image = 3,

    /// <summary>文件路径列表（CF_HDROP）。</summary>
    FileList = 4,
}
