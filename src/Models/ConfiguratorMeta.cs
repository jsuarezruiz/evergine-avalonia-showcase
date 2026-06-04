using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutomotiveConfigurator.AvaloniaEvergine.Models;

public sealed class ConfiguratorMeta
{
    [JsonPropertyName("body_colors")]
    public ColorGroup BodyColors { get; set; } = new();

    [JsonPropertyName("mirror_colors")]
    public ColorGroup MirrorColors { get; set; } = new();

    [JsonPropertyName("wheel_designs")]
    public DesignGroup WheelDesigns { get; set; } = new();

    [JsonPropertyName("wheel_colors")]
    public ColorGroup WheelColors { get; set; } = new();

    [JsonPropertyName("caliper_colors")]
    public ColorGroup CaliperColors { get; set; } = new();
}

public sealed class ColorGroup
{
    [JsonPropertyName("colors")]
    public List<ColorOption> Colors { get; set; } = new();

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("default")]
    public int Default { get; set; }
}

public sealed class ColorOption
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = "#000000";
}

public sealed class DesignGroup
{
    [JsonPropertyName("designs")]
    public List<DesignOption> Designs { get; set; } = new();

    [JsonPropertyName("default")]
    public int Default { get; set; }
}

public sealed class DesignOption
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("thumb")]
    public string Thumb { get; set; } = string.Empty;
}
