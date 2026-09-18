using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Core.Models;
using ClipboardManager.Interop;

namespace ClipboardManager.App;

/// <summary>
/// 剪贴板历史面板（需求 §3.4）。
/// <para>
/// 激活模型（D-03）：窗口<b>正常激活</b>以获得键盘焦点（否则 ↑↓/Enter/Esc 与搜索都无法使用）；
/// 「点击面板外部自动隐藏」已按用户要求移除（附加项 B-01），关闭手段为 Esc / 热键 / 关闭按钮。
/// </para>
/// </summary>
public partial class PanelWindow : Window
{
    private readonly ObservableCollection<ClipItemViewModel> _items = [];

    /// <summary>创建面板。</summary>
    public PanelWindow()
    {
        InitializeComponent();
        ItemsList.ItemsSource = _items;
    }

    /// <summary>请求粘贴某条记录（单击或 Enter）。</summary>
    public event Action<ClipItem>? PasteRequested;

    /// <summary>请求删除某条记录（Delete）。</summary>
    public event Action<ClipItem>? DeleteRequested;

    /// <summary>请求关闭面板（Esc / 关闭按钮）。</summary>
    public event Action? CloseRequested;

    /// <summary>刷新列表内容（全量替换，阶段一记录量级不大）。</summary>
    /// <param name="items">最新记录视图模型。</param>
    public void SetItems(IReadOnlyList<ClipItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        foreach (var item in items)
        {
            _items.Add(item);
        }

        CountText.Text = items.Count == 0 ? string.Empty : $"共 {items.Count} 条";
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (items.Count > 0)
        {
            ItemsList.SelectedIndex = 0;
        }
    }

    /// <summary>把键盘焦点交给列表（面板弹出后调用）。</summary>
    public void FocusList() => ItemsList.Focus();

    /// <summary>当前选中的记录。</summary>
    private ClipItem? SelectedItem => (ItemsList.SelectedItem as ClipItemViewModel)?.Source;

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // WS_EX_TOOLWINDOW：不进 Alt+Tab（需求 §3.4）。显式设置一次，不依赖 WPF 对
        // ShowInTaskbar=false 的隐式处理。
        var handle = new WindowInteropHelper(this).Handle;
        WindowStyling.HideFromAltTab(handle);
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (SelectedItem is { } enterItem)
                {
                    e.Handled = true;
                    PasteRequested?.Invoke(enterItem);
                }

                break;

            case Key.Delete:
                if (SelectedItem is { } deleteItem)
                {
                    e.Handled = true;
                    DeleteRequested?.Invoke(deleteItem);
                }

                break;

            case Key.Escape:
                e.Handled = true;
                CloseRequested?.Invoke();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 单击即粘贴（需求 §3.4 明确要求，不要做成双击）。
    /// 注意：必须用 MouseLeftButtonUp + 命中测试定位条目，因为 ListBox 自己也会处理选中。
    /// </summary>
    private void OnListMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var container = ItemsControl.ContainerFromElement(ItemsList, source) as ListBoxItem;
        if (container?.DataContext is not ClipItemViewModel viewModel)
        {
            return;
        }

        ItemsList.SelectedItem = viewModel;
        PasteRequested?.Invoke(viewModel.Source);
    }
}
