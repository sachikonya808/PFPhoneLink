using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using PFPhoneLink.Windows;
using System.Net.Sockets;
using PFPhoneLink.Server;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina;

namespace PFPhoneLink
{
    public sealed class Plugin : IDalamudPlugin
    {
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;

        private const string OpenWindow = "/pflink";
        public bool IsActive => _notifyServer.isRunning;

        public Configuration Configuration { get; init; }

        public readonly WindowSystem WindowSystem = new("PFPhoneLink");
        private ConfigWindow ConfigWindow { get; init; }
        private MainWindow MainWindow { get; init; }

        internal static GameData GameDataHelper { get; set; } = null!;

        public record PartyMemberInfo(string Name, string Job, int Level);
        public record PartyMessage(List<PartyMemberInfo> Members);

        private NotifyServer _notifyServer { get; init; }
        private readonly CancellationTokenSource cToken = new();
        private Task? loopPacketRelay;

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            _notifyServer = new NotifyServer();

            // You might normally want to embed resources and load them from the manifest stream
            var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

            ConfigWindow = new ConfigWindow(this);
            MainWindow = new MainWindow(this);

            loopPacketRelay = RunLoopAsync(cToken.Token);

            WindowSystem.AddWindow(ConfigWindow);
            WindowSystem.AddWindow(MainWindow);

            CommandManager.AddHandler(OpenWindow, new CommandInfo(OnStartCommand)
            {
                HelpMessage = "Start PF Link"
            });

            // Tell the UI system that we want our windows to be drawn through the window system
            PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

            // This adds a button to the plugin installer entry of this plugin which allows
            // toggling the display status of the configuration ui
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

            // Adds another button doing the same but for the main ui of the plugin
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

            // Add a simple message to the log with level set to information
            // Use /xllog to open the log window in-game
            // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
            Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
        }

        public void Dispose()
        {
            // Unregister all actions to not leak anything during disposal of plugin
            PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

            cToken.Cancel();
            try
            {
                loopPacketRelay?.Wait(TimeSpan.FromSeconds(2));
            }
            catch(AggregateException) { Log.Information("Loop has been closed"); }

            _notifyServer.Dispose();
            cToken.Dispose();

            WindowSystem.RemoveAllWindows();

            ConfigWindow.Dispose();
            MainWindow.Dispose();

            CommandManager.RemoveHandler(OpenWindow);
        }

        private void OnStartCommand(string command, string args)
        {
            ToggleMainUi();
        }
    
        public void ToggleConfigUi() => ConfigWindow.Toggle();
        public void ToggleMainUi() => MainWindow.Toggle();

        public void StartServer()
        {
            if (IsActive)
            {
                return;
            }

            try
            {
                _notifyServer.Start(Configuration.TcpPort);
            }
            catch (SocketException ex)
            {
                Log.Error(ex, $"Couldn't start server on port {Configuration.TcpPort}");
            }
        }

        private static readonly JsonSerializerOptions JsonOptions =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        
        public async Task SendJsonAsync<T>(T packet)
        {
            var json = JsonSerializer.Serialize(packet, JsonOptions);
            await SendPacket(json);
        }

        public async Task SendPacket(string message)
        {
            if (IsActive == true)
            {
                await _notifyServer.SendPacketAsync(message);
            }
        }

        public void ShutdownServer()
        {
            _notifyServer.Stop();
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            if (PlayerState.IsLoaded)
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Configuration.PushingRate));
                try
                {
                    while (await timer.WaitForNextTickAsync(cancellationToken))
                    {
                        try
                        {
                            await SendPartyInfo();
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Couldn't send data!");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Information("Shutting down tasks");
                }
            } else
            {
                ShutdownServer();
            }
        }

        private string GetJobAbbreviation(uint jobId) =>
            DataManager.GetExcelSheet<ClassJob>().TryGetRow(jobId, out var sheet)
            ? sheet.Abbreviation.ToString()
            : "???";

        private string GetWorldName(ushort worldID) =>
            DataManager.GetExcelSheet<World>().TryGetRow(worldID, out var sheet)
            ? sheet.Name.ToString() 
            : "???";

        private unsafe List<PartyMemberInfo> ReadParty()
        {
            var members = new List<PartyMemberInfo>();

            var proxy = InfoProxyCrossRealm.Instance();
            if (proxy->IsInCrossRealmParty)
            {
                var crGroup = proxy->CrossRealmGroups[proxy->LocalPlayerGroupIndex];
                for (var i = 0; i < crGroup.GroupMemberCount; i++)
                {
                    var m = crGroup.GroupMembers[i];
                    members.Add(new PartyMemberInfo($"{m.NameString} @ {GetWorldName((ushort)m.HomeWorld)}", GetJobAbbreviation(m.ClassJobId), m.Level));
                }

                return members;
            }

            return members;
        }

        private List<PartyMemberInfo> ReadyPartyInfo()
        {
            var members = ReadParty();

            var selfID = $"{PlayerState.CharacterName} @ {PlayerState.HomeWorld.Value.Name}";
            var index = members.FindIndex(m => m.Name == selfID);

            if (index > 0)
            {
                var self = members[index];
                members.RemoveAt(index);
                members.Insert(0, self);
            }

            return members;
        }

        public async Task SendPartyInfo()
        {
            var message = await Framework.RunOnFrameworkThread(() =>
                new PartyMessage(ReadyPartyInfo())
            );
            await SendJsonAsync(message);
        }
    }
}
