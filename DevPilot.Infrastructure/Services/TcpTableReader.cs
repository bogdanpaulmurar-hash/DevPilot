using System.Net;
using System.Runtime.InteropServices;
using DevPilot.Core.Guard;
using DevPilot.Core.Interfaces;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Reads the TCP connection table with the IP Helper API (no packet capture, no driver, no administrator rights).
/// Only TCP: the UDP table has no remote endpoint.
/// </summary>
public sealed class TcpTableReader : IConnectionSource
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int ErrorInsufficientBuffer = 122;
    private const int StateSynSent = 3;
    private const int StateEstablished = 5;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int tableClass, int reserved);

    public IReadOnlyList<ConnectionInfo> Snapshot()
    {
        var all = new List<ConnectionInfo>();
        all.AddRange(Read(AfInet));
        all.AddRange(Read(AfInet6));
        return all;
    }

    private static List<ConnectionInfo> Read(int af)
    {
        int size = 0;
        int rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TcpTableOwnerPidAll, 0);
        for (int attempt = 0; attempt < 5 && rc == ErrorInsufficientBuffer; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                rc = GetExtendedTcpTable(buffer, ref size, false, af, TcpTableOwnerPidAll, 0);
                if (rc == 0) return af == AfInet ? ParseV4(buffer) : ParseV6(buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return [];
    }

    private static int Port(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);

    private static bool Wanted(int state) => state is StateSynSent or StateEstablished;

    // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid (6 x 4 bytes).
    private static List<ConnectionInfo> ParseV4(IntPtr buffer)
    {
        var list = new List<ConnectionInfo>();
        int count = Marshal.ReadInt32(buffer);
        for (int i = 0; i < count; i++)
        {
            int offset = 4 + i * 24;
            if (!Wanted(Marshal.ReadInt32(buffer, offset))) continue;
            var remote = new IPAddress(unchecked((uint)Marshal.ReadInt32(buffer, offset + 12)));
            list.Add(new ConnectionInfo(Marshal.ReadInt32(buffer, offset + 20), remote.ToString(),
                Port(Marshal.ReadInt32(buffer, offset + 16)), Port(Marshal.ReadInt32(buffer, offset + 8))));
        }
        return list;
    }

    // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, pid (56 bytes).
    private static List<ConnectionInfo> ParseV6(IntPtr buffer)
    {
        var list = new List<ConnectionInfo>();
        int count = Marshal.ReadInt32(buffer);
        for (int i = 0; i < count; i++)
        {
            int offset = 4 + i * 56;
            if (!Wanted(Marshal.ReadInt32(buffer, offset + 48))) continue;
            var bytes = new byte[16];
            Marshal.Copy(IntPtr.Add(buffer, offset + 24), bytes, 0, 16);
            list.Add(new ConnectionInfo(Marshal.ReadInt32(buffer, offset + 52), new IPAddress(bytes).ToString(),
                Port(Marshal.ReadInt32(buffer, offset + 44)), Port(Marshal.ReadInt32(buffer, offset + 20))));
        }
        return list;
    }
}
