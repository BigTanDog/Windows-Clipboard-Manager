using ClipboardManager.Core.Sensitive;

namespace ClipboardManager.Tests;

/// <summary>
/// D-13 敏感信息脱敏测试。用例表与《技术设计文档》§6.5 一一对应。
/// </summary>
public class SensitiveMaskerTests
{
    private readonly SensitiveMasker _masker = new();

    [Theory]
    [InlineData("联系电话 13800138000 请查收", "联系电话 138****** 请查收")]
    [InlineData("手机号：13800138000。", "手机号：138******。")]
    public void 手机号_保留前三位(string input, string expected) =>
        Assert.Equal(expected, _masker.Mask(input));

    [Theory]
    // 前后紧邻数字时不应误伤（边界判定）
    [InlineData("138001380001", "138001380001")]
    [InlineData("113800138000", "113800138000")]
    public void 手机号_相邻数字不脱敏(string input, string expected) =>
        Assert.Equal(expected, _masker.Mask(input));

    [Fact]
    public void 身份证_脱敏()
    {
        var result = _masker.Mask("身份证 11010119900307123X 已登记");
        Assert.Equal("身份证 110****** 已登记", result);
    }

    [Theory]
    // 两个卡号都经手工验算通过 Luhn（16 位银联/Visa 测试号 + 19 位长卡号）
    [InlineData("卡号 6222021234567890128", "卡号 6222******")]
    [InlineData("卡号 4242424242424242", "卡号 4242******")]
    public void 银行卡_通过Luhn校验时脱敏(string input, string expected) =>
        Assert.Equal(expected, _masker.Mask(input));

    [Fact]
    public void 银行卡_Luhn不通过时不脱敏_避免订单号误报()
    {
        // 16 位纯数字但 Luhn 不通过：应保持原样（误报控制）
        const string orderNumber = "1234567890123456";
        Assert.Equal(orderNumber, _masker.Mask(orderNumber));
    }

    [Theory]
    [InlineData("alice@example.com", "a****@*****.com")]
    [InlineData("a.b+tag@mail.example.co.uk", "a****@*****.co.uk")]
    public void 邮箱_保留首字符与顶级域(string input, string expected) =>
        Assert.Equal(expected, _masker.Mask(input));

    [Theory]
    [InlineData("key=sk-abcdef123456", "key=sk-******")]
    [InlineData("token: ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ", "token: ghp******")]
    public void 密钥令牌_保留前三位(string input, string expected) =>
        Assert.Equal(expected, _masker.Mask(input));

    [Theory]
    [InlineData("密码: hunter2x", "密码: ******")]
    [InlineData("Password = MySecret99", "Password = ******")]
    public void 密码键值_只保留键名(string input, string expected) =>
        Assert.Equal(expected, _masker.Mask(input));

    [Fact]
    public void 嵌套场景_HTML片段中的手机号也被脱敏()
    {
        var result = _masker.Mask("<div>call 13800138000 now</div>");
        Assert.Equal("<div>call 138****** now</div>", result);
    }

    [Fact]
    public void 普通文本不改变()
    {
        const string input = "这是一段完全正常的中文文本，含数字 42 与英文 hello world。";
        Assert.Equal(input, _masker.Mask(input));
    }

    [Fact]
    public void 超长输入_不超时且尾部有省略号()
    {
        var input = new string('a', SensitiveMasker.MaxScanChars + 5000);
        var result = _masker.Mask(input);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
        Assert.True(result.Length <= SensitiveMasker.MaxScanChars + 1);
    }

    [Fact]
    public void 开关关闭时原样返回()
    {
        const string input = "手机 13800138000";
        Assert.Equal(input, SensitiveMasker.Disabled.Mask(input));
    }

    [Fact]
    public void 空输入安全()
    {
        Assert.Equal(string.Empty, _masker.Mask(null));
        Assert.Equal(string.Empty, _masker.Mask(string.Empty));
    }

    [Fact]
    public void 规则清单包含六类()
    {
        Assert.Equal(6, SensitiveMasker.RuleNames.Count);
        Assert.Contains("手机号", SensitiveMasker.RuleNames);
        Assert.Contains("银行卡", SensitiveMasker.RuleNames);
    }
}
