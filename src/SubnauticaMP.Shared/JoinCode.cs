using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SubnauticaMP.Shared
{
    // Turns "86.12.200.7:11000" into something easy to read out loud like "KQ7MX-3HD2P".
    // It's just the IP + port packed into 10 letters, so no middle-man server is needed.
    public static class JoinCode
    {
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I mix-ups
        const int Length = 10;

        public static string Encode(IPAddress ip, int port)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Join codes need an IPv4 address");
            var b = ip.GetAddressBytes();
            ulong value = ((ulong)b[0] << 40) | ((ulong)b[1] << 32) | ((ulong)b[2] << 24) | ((ulong)b[3] << 16) | (ushort)port;

            var sb = new StringBuilder(Length + 1);
            for (int i = Length - 1; i >= 0; i--)
                sb.Append(Alphabet[(int)((value >> (i * 5)) & 31)]);
            sb.Insert(5, '-');
            return sb.ToString();
        }

        public static bool TryDecode(string code, out string host, out int port)
        {
            host = null;
            port = 0;
            if (code == null) return false;
            code = code.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
            if (code.Length != Length) return false;

            ulong value = 0;
            foreach (char c in code)
            {
                int digit = Alphabet.IndexOf(c);
                if (digit < 0) return false;
                value = (value << 5) | (uint)digit;
            }
            if (value >> 48 != 0) return false;

            port = (int)(value & 0xFFFF);
            if (port == 0) return false;
            host = $"{(value >> 40) & 255}.{(value >> 32) & 255}.{(value >> 24) & 255}.{(value >> 16) & 255}";
            return true;
        }

        // Accepts a join code, "1.2.3.4", "1.2.3.4:5000", "myserver.com:5000".
        public static bool TryParseAddress(string input, int defaultPort, out string host, out int port)
        {
            host = null;
            port = defaultPort;
            input = (input ?? "").Trim();
            if (input.Length == 0) return false;

            if (input.IndexOf('.') < 0 && input.IndexOf(':') < 0 && TryDecode(input, out var codeHost, out var codePort))
            {
                host = codeHost;
                port = codePort;
                return true;
            }
            if (input.Equals("localhost", StringComparison.OrdinalIgnoreCase)) { host = "127.0.0.1"; return true; }

            int colon = input.LastIndexOf(':');
            if (colon > 0 && input.IndexOf(':') == colon) // one colon = host:port (bare IPv6 has several)
            {
                if (!int.TryParse(input.Substring(colon + 1), out port) || port < 1 || port > 65535) return false;
                input = input.Substring(0, colon);
            }
            if (input.IndexOf(' ') >= 0 || input.Length == 0) return false;
            host = input;
            return true;
        }
    }
}
