using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PFPhoneLink.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("Phone Link Settings###With a constant ID")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(286, 120);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Can't ref a property, so use a local copy
        var tcpPort = configuration.TcpPort;
        if (ImGui.InputInt("TCP Port", ref tcpPort))
        {
            configuration.TcpPort = Math.Clamp(tcpPort, 1024, 49151);
            // Can save immediately on change if you don't want to provide a "Save and Close" button
            configuration.Save();
        }
        var pushingRate = configuration.PushingRate;
        if (ImGui.InputInt("Pushing Rate", ref pushingRate))
        {
            configuration.PushingRate = Math.Clamp(pushingRate, 1, 69420);
            configuration.Save();
        }
    }
}
