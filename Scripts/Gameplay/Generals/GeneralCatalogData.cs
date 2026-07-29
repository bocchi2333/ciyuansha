using System.Collections.Generic;

namespace CiyuanSha.Gameplay.Generals;

/// <summary>
/// Serializable general catalog file.
/// </summary>
public sealed class GeneralCatalogData
{
    public List<GeneralDefinition> Generals { get; set; } = new();
}
