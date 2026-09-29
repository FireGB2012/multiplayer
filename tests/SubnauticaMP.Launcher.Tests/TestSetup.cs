using System.Runtime.CompilerServices;

namespace SubnauticaMP.Launcher.Tests;

static class TestSetup
{
    // keep tests away from the real %AppData%\SubnauticaMP
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("SNMP_DATA_DIR", Path.Combine(Path.GetTempPath(), "snmp-launcher-tests"));
}
