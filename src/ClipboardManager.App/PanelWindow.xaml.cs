using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Core.Models;
using ClipboardManager.Interop;

namespace ClipboardManager.App;

/// <summary>
/// 剪贴板历史面板（需求 §3.4）。
/// <para>
/// 激活模型（D-03）：窗口<b>正常激活</b>以获得键盘焦点；
/// 「点击面板外部自动隐藏」已按用户要求移除（附加项 B-01），关闭手段为 Esc / 热键 / 关闭按钮。
/// </para>
/// </summary>
public partial class PanelWindow : Window
{
    private const string DefaultEmptyHint = "还没有记录\n复制任意文字后会自动出现在这里";

    private readonly ObservableCollection<ClipItemViewModel> _items = [];
    private readonly DispatcherTimer _searchDebounce;

    /// <summary>创建面板。</summary>
    public PanelWindow()
    {
        InitializeComponent();
        ItemsList.ItemsSource = _items;

        // 搜索防抖：200ms 内不再输入才真正查询（技术设计 §6.3）。
        _searchDebounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            SearchRequested?.Invoke(CurrentQuery);
        };
    }

    /// <summary>请求粘贴某条记录（单击或 Enter）。</summary>
    public event Action<ClipItem>? PasteRequested;

    /// <summary>请求删除某条记录（Delete）。</summary>
    public event Action<ClipItem>? DeleteRequested;

    /// <summary>请求关闭面板（Esc / 关闭按钮）。</summary>
    public event Action? CloseRequested;

    /// <summary>搜索词变化（已防抖）。</summary>
    public event Action<string>? SearchRequested;

    /// <summary>当前搜索词（已去除首尾空白）。</summary>
    public string CurrentQuery => SearchBox.Text.Trim();

    /// <summary>刷新列表内容（全量替换，阶段一/二记录量级不大）。</summary>
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

        if (items.Count == 0)
        {
            EmptyHint.Text = string.IsNullOrEmpty(CurrentQuery)
                ? DefaultEmptyHint
                : "没有匹配的记录";
            EmptyHint.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyHint.Visibility = Visibility.Collapsed;
            ItemsList.SelectedIndex = 0;
        }
    }

    /// <summary>把键盘焦点交给列表（面板弹出后调用）。</summary>
    public void FocusList() => ItemsList.Focus();

    /// <summary>每次弹出时重置搜索（产品计划 Q-2 的既定行为）。</summary>
    public void ResetSearch()
    {
        SearchBox.Text = string.Empty;
        SearchHint.Visibility = Visibility.Visible;
        _searchDebounce.Stop();
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // WS_EX_TOOLWINDOW：不进 Alt+Tab（需求 §3.4）。
        var handle = new WindowInteropHelper(this).Handle;
        WindowStyling.HideFromAltTab(handle);
    }

    private ClipItem? SelectedItem => (ItemsList.SelectedItem as ClipItemViewModel)?.Source;

    private void OnCloseButtonClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    /// <summary>搜索框按键：Esc 逐层退出（先清搜索，再关面板）；↓ 把焦点交给列表。</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (!string.IsNullOrEmpty(SearchBox.Text))
                {
                    SearchBox.Text = string.Empty;
                    _searchDebounce.Stop();
                    SearchRequested?.Invoke(string.Empty);
                }
                else
                {
                    CloseRequested?.Invoke();
                }

                break;

            case Key.Down:
                e.Handled = true;
                ItemsList.Focus();
                if (ItemsList.SelectedIndex < 0 && ItemsList.Items.Count > 0)
                {
                    ItemsList.SelectedIndex = 0;
                }

                break;

            default:
                break;
        }
    }

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
    /// 必须用 MouseLeftButtonUp + 命中测试定位条目，因为 ListBox 自己也会处理选中。
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
