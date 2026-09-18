using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipboardManager.Core.Settings;

/// <summary>
/// 设置的 JSON 序列化上下文（源生成器）。
/// 使用源生成而非反射，避免将来若启用裁剪 / AOT 时序列化失效（技术设计 §7.4）。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
