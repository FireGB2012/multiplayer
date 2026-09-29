using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace SubnauticaMP.Shared
{
    public sealed class PortForwardResult
    {
        public bool Success;
        public string ExternalIp; // what friends connect to
        public string LocalIp;
        public string Error;
        public bool BehindCgnat; // ISP-level NAT: port forwarding can't work, need a VPN/tunnel
        public bool DoubleNat;   // router opened the port, but another box (ISP modem) sits in front of it
        public string PublicIp;  // what websites see us as
        public System.Collections.Generic.List<string> Details = new System.Collections.Generic.List<string>();

        public string Report() => string.Join(" | ", Details.ToArray());
    }

    // Asks the home router (via UPnP) to forward our port so people on other networks can connect.
    // Most routers support this; if it's turned off, the host has to forward the port by hand.
    public static class Upnp
    {
        static readonly string[] SearchTargets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
        };

        sealed class Gateway
        {
            public string ControlUrl;
            public string ServiceType;
            public string Host;
        }

        static Gateway _cached;

        public static PortForwardResult OpenPort(int port, string description, int discoveryTimeoutMs = 4000)
        {
            // 1) Mono.Nat, the library Nitrox uses: searches on every network adapter, UPnP and NAT-PMP.
            PortForwardResult result;
            try { result = MonoNatForward.Open(port, description, 12000); }
            catch (Exception e) { result = new PortForwardResult { Error = "Mono.Nat unavailable: " + e.GetBaseException().Message }; }

            // 2) our own UPnP code as a backup
            if (!result.Success)
            {
                var ours = OpenPortOurselves(port, description, discoveryTimeoutMs);
                ours.Details.InsertRange(0, result.Details);
                if (!ours.Success) ours.Error = result.Error + "; " + ours.Error;
                result = ours;
            }

            // 3) is the router the only box between us and the internet?
            result.PublicIp = LookUpPublicIp();
            result.Details.Add("public IP (web): " + (result.PublicIp ?? "unknown") + ", router's outside IP: " + (result.ExternalIp ?? "unknown"));
            Classify(result);
            return result;
        }

        // Asks an outside website to try connecting to our port: the only real proof friends can get in.
        // true = reachable, false = closed, null = couldn't check.
        public static bool? CheckReachable(int port)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://ifconfig.co/port/" + port);
                req.Accept = "application/json";
                req.Timeout = 15000;
                req.UserAgent = "SubnauticaMP";
                using (var resp = req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                {
                    var json = reader.ReadToEnd().Replace(" ", "");
                    if (json.Contains("\"reachable\":true")) return true;
                    if (json.Contains("\"reachable\":false")) return false;
                }
            }
            catch { }
            return null;
        }

        static PortForwardResult OpenPortOurselves(int port, string description, int discoveryTimeoutMs)
        {
            var result = new PortForwardResult();
            try
            {
                var locations = Discover(discoveryTimeoutMs);
                result.Details.Add("our UPnP search found " + locations.Count + " router(s): " + string.Join(", ", locations.ToArray()));
                if (locations.Count == 0)
                {
                    result.Error = "No UPnP router found (UPnP may be turned off in your router settings)";
                    return result;
                }

                string lastError = null;
                foreach (var location in locations)
                {
                    var r = OpenPortAt(location, port, description);
                    result.Details.Add($"{location}: " + (r.Success ? "opened, outside IP " + r.ExternalIp : r.Error));
                    if (r.Success)
                    {
                        r.Details.InsertRange(0, result.Details);
                        return r;
                    }
                    lastError = r.Error;
                }
                result.Error = lastError;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return result;
        }

        // Skips discovery: talks to the router description at this URL. Public for tests.
        public static PortForwardResult OpenPortAt(string location, int port, string description)
        {
            var result = new PortForwardResult();
            try
            {
                var gw = ReadGateway(location);
                if (gw == null)
                {
                    result.Error = "Router doesn't offer port forwarding over UPnP";
                    return result;
                }

                result.LocalIp = LocalIpToward(gw.Host);
                string lastError = null;
                foreach (var lease in new[] { 0, 86400 }) // some routers refuse permanent mappings
                {
                    lastError = Soap(gw, "AddPortMapping",
                        ("NewRemoteHost", ""),
                        ("NewExternalPort", port.ToString()),
                        ("NewProtocol", "TCP"),
                        ("NewInternalPort", port.ToString()),
                        ("NewInternalClient", result.LocalIp),
                        ("NewEnabled", "1"),
                        ("NewPortMappingDescription", description),
                        ("NewLeaseDuration", lease.ToString())).error;
                    if (lastError == null) break;
                }
                if (lastError != null)
                {
                    result.Error = "Router refused the port forward: " + lastError;
                    return result;
                }

                var ip = Soap(gw, "GetExternalIPAddress");
                if (ip.error == null) result.ExternalIp = FindValue(ip.body, "NewExternalIPAddress");
                result.Success = true;
                _cached = gw;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return result;
        }

        public static void ClosePort(int port)
        {
            try { MonoNatForward.Close(port); } catch { }
            var gw = _cached;
            if (gw == null) return;
            try
            {
                Soap(gw, "DeletePortMapping", ("NewRemoteHost", ""), ("NewExternalPort", port.ToString()), ("NewProtocol", "TCP"));
            }
            catch { }
        }

        // Fallback when UPnP is off: ask a website what our public IP is.
        public static string LookUpPublicIp()
        {
            foreach (var url in new[] { "http://api.ipify.org", "http://checkip.amazonaws.com", "http://icanhazip.com" })
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 4000;
                    using (var resp = req.GetResponse())
                    using (var reader = new StreamReader(resp.GetResponseStream()))
                    {
                        var text = reader.ReadToEnd().Trim();
                        if (IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork) return text;
                    }
                }
                catch { }
            }
            return null;
        }

        // 100.64/10 = the ISP shares one internet IP between customers (CGNAT): only a VPN/tunnel helps.
        // Any other private address, or a different IP than websites see = a second box (ISP modem) in front
        // of the router: forward the port on that box too, or put it in bridge mode.
        public static void Classify(PortForwardResult r)
        {
            r.BehindCgnat = r.DoubleNat = false;
            if (!r.Success || r.ExternalIp == null) return;
            if (IsCgnat(r.ExternalIp)) r.BehindCgnat = true;
            else if (IsNonPublic(r.ExternalIp) || (r.PublicIp != null && r.PublicIp != r.ExternalIp)) r.DoubleNat = true;
        }

        public static bool IsCgnat(string ipText)
        {
            if (!IPAddress.TryParse(ipText ?? "", out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
        }

        // Private ranges + 100.64/10 (carrier-grade NAT): the router's "external" IP isn't the real internet one.
        public static bool IsNonPublic(string ipText)
        {
            if (!IPAddress.TryParse(ipText ?? "", out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || b[0] == 0;
        }

        // ---------- internals ----------

        // Asks for routers from every network adapter (VPNs and virtual adapters can otherwise
        // swallow the request), repeating a few times since the packets are easy to lose.
        static List<string> Discover(int timeoutMs)
        {
            var found = new List<string>();
            var sockets = new List<UdpClient>();
            foreach (var local in LocalIPv4Addresses())
            {
                try { sockets.Add(new UdpClient(new IPEndPoint(local, 0))); } catch { }
            }
            if (sockets.Count == 0) sockets.Add(new UdpClient(new IPEndPoint(IPAddress.Any, 0)));

            try
            {
                var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                var nextSend = DateTime.MinValue;
                while (DateTime.UtcNow < deadline)
                {
                    if (DateTime.UtcNow >= nextSend)
                    {
                        nextSend = DateTime.UtcNow.AddMilliseconds(1000);
                        foreach (var udp in sockets)
                            foreach (var st in SearchTargets)
                            {
                                var msg = Encoding.ASCII.GetBytes(
                                    "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + st + "\r\n\r\n");
                                try { udp.Send(msg, msg.Length, target); } catch { }
                            }
                    }

                    foreach (var udp in sockets)
                    {
                        while (true)
                        {
                            try { if (udp.Available <= 0) break; } catch { break; }
                            byte[] data;
                            try
                            {
                                var from = new IPEndPoint(IPAddress.Any, 0);
                                data = udp.Receive(ref from);
                            }
                            catch (SocketException) { break; }
                            var location = HeaderValue(Encoding.ASCII.GetString(data), "LOCATION");
                            if (location != null && !found.Contains(location)) found.Add(location);
                        }
                    }
                    if (found.Count > 0 && DateTime.UtcNow.AddMilliseconds(timeoutMs / 2) > deadline) break; // got one, don't wait forever
                    System.Threading.Thread.Sleep(50);
                }
            }
            finally
            {
                foreach (var udp in sockets) udp.Close();
            }
            return found;
        }

        static List<IPAddress> LocalIPv4Addresses()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var a in nic.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(a.Address);
                }
            }
            catch { }
            return list;
        }

        static string HeaderValue(string response, string header)
        {
            foreach (var line in response.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && line.Substring(0, colon).Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        static Gateway ReadGateway(string location)
        {
            var doc = new XmlDocument();
            doc.LoadXml(HttpGet(location));
            var baseUri = new Uri(location);

            string urlBase = null;
            foreach (XmlNode n in doc.GetElementsByTagName("URLBase")) urlBase = n.InnerText.Trim();
            if (!string.IsNullOrEmpty(urlBase) && Uri.TryCreate(urlBase, UriKind.Absolute, out var ub)) baseUri = ub;

            foreach (XmlNode service in doc.GetElementsByTagName("service"))
            {
                string type = null, control = null;
                foreach (XmlNode child in service.ChildNodes)
                {
                    if (child.LocalName == "serviceType") type = child.InnerText.Trim();
                    if (child.LocalName == "controlURL") control = child.InnerText.Trim();
                }
                if (type == null || control == null) continue;
                if (type.IndexOf("WANIPConnection", StringComparison.Ordinal) < 0 &&
                    type.IndexOf("WANPPPConnection", StringComparison.Ordinal) < 0) continue;

                return new Gateway
                {
                    ServiceType = type,
                    ControlUrl = new Uri(baseUri, control).ToString(),
                    Host = new Uri(location).Host,
                };
            }
            return null;
        }

        static string LocalIpToward(string host)
        {
            using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                s.Connect(host, 1900); // UDP "connect" sends nothing, just picks the right network card
                return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
            }
        }

        static (string body, string error) Soap(Gateway gw, string action, params (string name, string value)[] args)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?>\r\n<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>");
            sb.Append("<u:").Append(action).Append(" xmlns:u=\"").Append(gw.ServiceType).Append("\">");
            foreach (var (name, value) in args)
                sb.Append('<').Append(name).Append('>').Append(SecurityElementEscape(value)).Append("</").Append(name).Append('>');
            sb.Append("</u:").Append(action).Append("></s:Body></s:Envelope>");
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            var req = (HttpWebRequest)WebRequest.Create(gw.ControlUrl);
            req.Method = "POST";
            req.Timeout = 4000;
            req.ContentType = "text/xml; charset=\"utf-8\"";
            req.Headers.Add("SOAPACTION", "\"" + gw.ServiceType + "#" + action + "\"");
            req.ContentLength = bytes.Length;
            using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

            try
            {
                using (var resp = req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                    return (reader.ReadToEnd(), null);
            }
            catch (WebException e) when (e.Response != null)
            {
                using (var reader = new StreamReader(e.Response.GetResponseStream()))
                {
                    var body = reader.ReadToEnd();
                    var code = FindValue(body, "errorCode");
                    var desc = FindValue(body, "errorDescription");
                    var err = $"{code} {desc}".Trim();
                    return (body, err.Length > 0 ? err : e.Message);
                }
            }
        }

        static string FindValue(string xml, string tag)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                foreach (XmlNode n in doc.GetElementsByTagName("*"))
                    if (n.LocalName == tag) return n.InnerText.Trim();
            }
            catch { }
            return null;
        }

        static string HttpGet(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 4000;
            using (var resp = req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream()))
                return reader.ReadToEnd();
        }

        static string SecurityElementEscape(string s) => System.Security.SecurityElement.Escape(s ?? "");
    }
}
