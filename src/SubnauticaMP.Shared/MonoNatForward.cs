using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Nat;

namespace SubnauticaMP.Shared
{
    // Port forwarding through Mono.Nat, the way Nitrox does it: listen for routers on every adapter
    // (UPnP and NAT-PMP), and try each router that answers until one accepts the mapping.
    // Kept in its own class so that if Mono.Nat.dll is missing, only this part fails.
    internal static class MonoNatForward
    {
        static readonly object Lock = new object();
        static readonly List<INatDevice> Devices = new List<INatDevice>();
        static bool _listening;
        static INatDevice _mappedOn;
        static Mapping _mapping;

        static void StartListening()
        {
            lock (Lock)
            {
                if (_listening) return;
                _listening = true;
                NatUtility.DeviceFound += (s, e) =>
                {
                    lock (Lock)
                        if (!Devices.Any(d => d.DeviceEndpoint.Equals(e.Device.DeviceEndpoint) && d.NatProtocol == e.Device.NatProtocol))
                            Devices.Add(e.Device);
                };
                NatUtility.StartDiscovery(NatProtocol.Upnp, NatProtocol.Pmp);
            }
        }

        public static PortForwardResult Open(int port, string description, int timeoutMs)
        {
            StartListening();
            var result = new PortForwardResult();
            var tried = new HashSet<INatDevice>();
            var errors = new List<string>();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                List<INatDevice> fresh;
                lock (Lock) fresh = Devices.Where(d => !tried.Contains(d)).ToList();

                foreach (var device in fresh)
                {
                    tried.Add(device);
                    // same as Nitrox: longest lease first; some routers only take 0 (= forever) or short ones
                    foreach (var lifetime in new[] { int.MaxValue, 0, 7200 })
                    {
                        try
                        {
                            var mapping = new Mapping(Mono.Nat.Protocol.Tcp, port, port, lifetime, description);
                            if (Run(device.CreatePortMapAsync(mapping), 5000) == null) continue;
                            _mappedOn = device;
                            _mapping = mapping;
                            result.Success = true;
                            try { result.ExternalIp = Run(device.GetExternalIPAsync(), 5000)?.ToString(); } catch { }
                            result.BehindCgnat = Upnp.IsNonPublic(result.ExternalIp);
                            return result;
                        }
                        catch (MappingException e) when (e.ErrorCode == ErrorCode.ConflictInMappingEntry)
                        {
                            // something (maybe our last run) already forwards this port: fine if it's to us
                            result.Success = true;
                            try { result.ExternalIp = Run(device.GetExternalIPAsync(), 5000)?.ToString(); } catch { }
                            result.BehindCgnat = Upnp.IsNonPublic(result.ExternalIp);
                            return result;
                        }
                        catch (Exception e)
                        {
                            errors.Add($"{device.NatProtocol}: {e.GetBaseException().Message}");
                        }
                    }
                }
                Thread.Sleep(200);
            }

            lock (Lock) result.Error = tried.Count == 0
                ? "No router answered (UPnP / NAT-PMP)"
                : "Router said no: " + string.Join(", ", errors.Distinct().Take(3).ToArray());
            return result;
        }

        public static void Close(int port)
        {
            var device = _mappedOn;
            var mapping = _mapping;
            if (device == null || mapping == null || mapping.PrivatePort != port) return;
            try { Run(device.DeletePortMapAsync(mapping), 3000); } catch { }
            _mappedOn = null;
            _mapping = null;
        }

        static T Run<T>(Task<T> task, int timeoutMs)
        {
            if (!task.Wait(timeoutMs)) throw new TimeoutException("Router didn't answer in time");
            return task.Result;
        }
    }
}
