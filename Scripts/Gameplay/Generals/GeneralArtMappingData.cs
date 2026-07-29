using System.Collections.Generic;

namespace CiyuanSha.Gameplay.Generals;

/// <summary>
/// Serializable mapping file for general id to image file name.
/// </summary>
public class GeneralArtMappingData
{
    public Dictionary<string, string> Mappings { get; set; } = new();
}
