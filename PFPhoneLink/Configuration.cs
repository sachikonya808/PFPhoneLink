using Dalamud.Configuration;
using System;

namespace PFPhoneLink;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public int TcpPort { get; set; } = 8089;
    public int PushingRate { get; set; } = 20;
    public bool IsConfigWindowMovable { get; set; } = true;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
