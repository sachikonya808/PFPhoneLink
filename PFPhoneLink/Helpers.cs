using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace PFPhoneLink
{
    public class Helpers
    {
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

        public static string StripNonAsci(string text)
        {
            while (text.Length > 5 && text[0] == 2)
            {
                text = text[5..];
            }
            return text;
        }
    }
}
