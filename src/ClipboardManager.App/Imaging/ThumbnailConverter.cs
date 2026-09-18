using System.Globalization;
using System.Windows.Data;

namespace ClipboardManager.App.Imaging;

/// <summary>
/// 把缩略图文件路径转换为图像源。
/// <para>
/// 使用转换器而不是直接绑定路径的原因：① 直接绑定会让 WPF 默认加载方式<b>锁定文件</b>，
/// 导致删除记录时删不掉；② 转换器配合列表虚拟化，只有可见行才真正解码，
/// 避免一次载入数百张缩略图。
/// </para>
/// </summary>
internal sealed class ThumbnailConverter : IValueConverter
{
    /// <summary>列表缩略图解码宽度（小图足够，省内存）。</summary>
    private const int DecodeWidth = 72;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PngEncoder.TryLoadFrozen(value as string, DecodeWidth);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
