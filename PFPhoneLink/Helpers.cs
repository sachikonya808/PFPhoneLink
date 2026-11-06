using Dalamud.IoC;
using Dalamud.Plugin.Services;
using PFPhoneLink.Server;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace PFPhoneLink
{
    public class Helpers
    {
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

        public record PartyMemberInfo(
            string Name,
            string Job,
            int Level
        );
        public record PartyMessage(
            List<PartyMemberInfo> Members,
            string duty
        );

        public static string getIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
            return "null";
        }

        public static readonly JsonSerializerOptions JsonOptions =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }
}
