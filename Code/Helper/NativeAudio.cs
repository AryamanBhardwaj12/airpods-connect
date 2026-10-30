using System.Runtime.InteropServices;

namespace AirPodsConnect;

// ABI declarations follow Microsoft's mmdeviceapi.h, devicetopology.h, propsys.h and ksmedia.h.
[StructLayout(LayoutKind.Sequential)] internal struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
[StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public IntPtr Pointer;
    [FieldOffset(8)] public uint Number;
}
[StructLayout(LayoutKind.Sequential)] internal struct KsProperty(Guid set, uint id, uint flags)
{ public Guid Set = set; public uint Id = id; public uint Flags = flags; }
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class DeviceEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid id, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}
[ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig] int GetConnectorCount(out uint count);
    [PreserveSig] int GetConnector(uint index, out IConnector connector);
    [PreserveSig] int GetSubunitCount(out uint count);
    [PreserveSig] int GetSubunit(uint index, out IntPtr unit);
    [PreserveSig] int GetPartById(uint id, out IntPtr part);
    [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetSignalPath(IntPtr from, IntPtr to, [MarshalAs(UnmanagedType.Bool)] bool reject, out IntPtr parts);
}
[ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    [PreserveSig] int GetConnectorType(out int type);
    [PreserveSig] int GetDataFlow(out int flow);
    [PreserveSig] int ConnectTo(IConnector connector);
    [PreserveSig] int Disconnect();
    [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
    [PreserveSig] int GetConnectedTo(out IConnector connector);
    [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
}
[ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsControl
{
    [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint returned);
    [PreserveSig] int KsMethod(IntPtr method, uint length, IntPtr data, uint dataLength, out uint returned);
    [PreserveSig] int KsEvent(IntPtr evt, uint length, IntPtr data, uint dataLength, out uint returned);
}
internal sealed class Audio : IDisposable
{
    private readonly IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
    internal static readonly Guid BtSet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private static readonly PropertyKey ContainerKey = new(new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_DevNode_PropertyW(uint node, ref PropertyKey key, out uint type, byte[] buffer, ref uint size, uint flags);
    internal static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    internal static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose() => Release(enumerator);
    private static T Property<T>(IPropertyStore store, PropertyKey key, Func<PropVariant, T> decode)
    {
        PropVariant value = default;
        try { Check(store.GetValue(ref key, out value)); return decode(value); }
        finally { PropVariantClear(ref value); }
    }
    public static Guid ParentContainer(string instance)
    {
        if (CM_Locate_DevNodeW(out uint node, instance, 0) != 0) throw new ArgumentException("Paired Bluetooth parent is missing. Reconfigure the app after pairing.");
        var key = ContainerKey;
        byte[] data = new byte[16]; uint size = 16;
        if (CM_Get_DevNode_PropertyW(node, ref key, out uint type, data, ref size, 0) != 0 || size != 16 || type != 13)
            throw new ArgumentException("Cannot verify paired Bluetooth container.");
        return new Guid(data);
    }
    public static void ValidateParent(Settings s)
    {
        s.Validate();
        if (ParentContainer(s.BluetoothInstanceId) != s.ContainerId) throw new ArgumentException("AirPods pairing identity changed. Run Install.ps1 again.");
    }
    public Endpoint[] List(Settings? target = null, bool resolve = false)
    {
        IMMDeviceCollection? collection = null;
        try
        {
            Check(enumerator.EnumAudioEndpoints(0, 15, out collection));
            Check(collection.GetCount(out uint count));
            var items = new List<Endpoint>();
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null; IPropertyStore? store = null;
                try
                {
                    Check(collection.Item(i, out device)); Check(device.GetId(out string id)); Check(device.GetState(out uint state));
                    Check(device.OpenPropertyStore(0, out store));
                    string name = Property(store, new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14), p => p.Type == 31 ? Marshal.PtrToStringUni(p.Pointer) ?? "Unknown" : "Unknown");
                    Guid container = Property(store, ContainerKey, p => p.Type == 72 && p.Pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(p.Pointer) : Guid.Empty);
                    string? filter = null;
                    if (resolve && (target == null || container == target.ContainerId) && state is 1 or 8)
                    {
                        try { filter = FilterId(device); }
                        catch (COMException) when (target == null) { }
                        catch (ArgumentException) when (target == null) { }
                    }
                    items.Add(new(id, name, container, state, filter));
                }
                finally { Release(store); Release(device); }
            }
            return items.ToArray();
        }
        finally { Release(collection); }
    }
    private static string FilterId(IMMDevice device)
    {
        object? topologyObject = null; IConnector? connector = null;
        try
        {
            Guid iid = typeof(IDeviceTopology).GUID;
            Check(device.Activate(ref iid, 23, IntPtr.Zero, out topologyObject));
            var topology = (IDeviceTopology)topologyObject;
            Check(topology.GetConnectorCount(out uint count));
            if (count != 1) throw new ArgumentException("Unexpected endpoint topology; refusing ambiguous target.");
            Check(topology.GetConnector(0, out connector));
            Check(connector.GetDeviceIdConnectedTo(out string id));
            return id;
        }
        finally { Release(connector); Release(topologyObject); }
    }
    public string? DefaultOutput()
    {
        IMMDevice? device = null;
        try { if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) < 0) return null; Check(device.GetId(out string id)); return id; }
        finally { Release(device); }
    }
    public Result Status(Settings s, bool resolve = false)
    {
        ValidateParent(s);
        Endpoint[] all = List(s, resolve);
        Endpoint[] own = all.Where(x => x.ContainerId == s.ContainerId).ToArray();
        Endpoint? stereo = all.SingleOrDefault(x => x.Id == s.StereoEndpointId && x.ContainerId == s.ContainerId);
        string? output = DefaultOutput();
        if (stereo == null) return new(3, "Missing", "Configured AirPods audio endpoint is missing. Run Install.ps1 again.", own, output);
        if (stereo.Active) return new(0, "Connected", output == stereo.Id ? "AirPods connected · selected for audio" : "AirPods connected · another output is selected", own, output);
        if (!stereo.Available) return new(3, "Unavailable", "AirPods audio endpoint is disabled or not present.", own, output);
        if (own.Any(x => x.Active)) return new(0, "CallsOnly", "AirPods call audio only · click to connect stereo audio", own, output);
        return new(0, "Disconnected", "AirPods disconnected · left-click to connect", own, output);
    }
    public static Endpoint SelectStereo(Settings s, Endpoint[] endpoints)
    {
        s.Validate();
        var matches = endpoints.Where(e => e.ContainerId == s.ContainerId && e.Id == s.StereoEndpointId && e.Available).ToArray();
        if (matches.Length != 1) throw new ArgumentException("The bound stereo endpoint is missing, unavailable or ambiguous.");
        return matches[0];
    }
    public int Send(Settings s, Endpoint endpoint, bool disconnect)
    {
        ValidateParent(s);
        IMMDevice? device = null; IMMDevice? filter = null; object? control = null;
        try
        {
            // Refresh exact membership and availability immediately before every dispatch.
            var current = List(s).Single(x => x.Id == endpoint.Id && x.ContainerId == s.ContainerId && x.Available);
            Check(enumerator.GetDevice(current.Id, out device));
            string id = FilterId(device);
            // HFP adapter paths can omit the Bluetooth address. Its exact endpoint membership
            // and the adapter's container must then independently bind it to this pairing.
            if (!id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) && !id.Contains("BTHHFENUM", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The audio filter does not match the configured Bluetooth address.");
            Check(enumerator.GetDevice(id, out filter));
            IPropertyStore? filterStore = null;
            try
            {
                Check(filter.OpenPropertyStore(0, out filterStore));
                Guid filterContainer = Property(filterStore, ContainerKey, p => p.Type == 72 && p.Pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(p.Pointer) : Guid.Empty);
                if (filterContainer != s.ContainerId) throw new ArgumentException("Bluetooth filter container does not match the pairing.");
            }
            finally { Release(filterStore); }
            if (!disconnect && (!id.Contains("0000110b", StringComparison.OrdinalIgnoreCase) || !id.Contains(s.BluetoothAddress, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("The configured stereo filter is not this device's A2DP audio sink.");
            Guid iid = typeof(IKsControl).GUID;
            Check(filter.Activate(ref iid, 23, IntPtr.Zero, out control));
            // No read-only method sends this GET. It is the actual explicit command, once.
            var property = new KsProperty(BtSet, disconnect ? 1u : 0u, 1);
            return ((IKsControl)control).KsProperty(ref property, 24, IntPtr.Zero, 0, out _);
        }
        finally { Release(control); Release(filter); Release(device); }
    }
}
