using System.Text.Json.Serialization;

namespace PersonalToolbox.Modules;

public sealed class CursorSkinPackage
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;

    public bool IsComplete { get; set; }

    public int CursorCount { get; set; }

    public bool IsApplied { get; set; }

    public string? ImportNote { get; set; }

    public List<CursorRole> Roles { get; set; } = [];

    [JsonIgnore]
    public string? PreviewPath => Roles
        .FirstOrDefault(role => string.Equals(role.WindowsKey, "Arrow", StringComparison.OrdinalIgnoreCase))
        ?.PreviewPath;

    [JsonIgnore]
    public string CountLabel => $"{CursorCount} 个光标";

    [JsonIgnore]
    public int MissingCount => Roles.Count(role => !role.Exists || string.IsNullOrWhiteSpace(role.FilePath));

    [JsonIgnore]
    public string CompletenessLabel => MissingCount == 0
        ? $"{Roles.Count} 个角色 · 完整"
        : $"{Roles.Count} 个角色 · 缺少 {MissingCount} 项";

    [JsonIgnore]
    public bool HasImportNote => !string.IsNullOrWhiteSpace(ImportNote);
}

public sealed class CursorRole
{
    public string Role { get; set; } = string.Empty;

    public string WindowsKey { get; set; } = string.Empty;

    public string? FilePath { get; set; }

    public string? PreviewPath { get; set; }

    public bool Exists { get; set; }

    [JsonIgnore]
    public string DisplayName => WindowsKey switch
    {
        "Arrow" => "普通选择",
        "Help" => "帮助选择",
        "AppStarting" => "后台运行",
        "Wait" => "忙碌",
        "Crosshair" => "精确选择",
        "IBeam" => "文本选择",
        "NWPen" => "手写",
        "No" => "不可用",
        "SizeNS" => "垂直调整",
        "SizeWE" => "水平调整",
        "SizeNWSE" => "对角调整 ↘",
        "SizeNESW" => "对角调整 ↗",
        "SizeAll" => "移动",
        "UpArrow" => "候选选择",
        "Hand" => "链接选择",
        "Pin" => "位置选择",
        "Person" => "人员选择",
        _ => string.IsNullOrWhiteSpace(Role) ? WindowsKey : Role,
    };

    [JsonIgnore]
    public string AvailabilityLabel => Exists && !string.IsNullOrWhiteSpace(FilePath) ? "可用" : "缺失";
}
