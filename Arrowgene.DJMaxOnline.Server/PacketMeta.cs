namespace Arrowgene.DJMaxOnline.Server;

public class PacketMeta
{
    public string Name { get; }
    public PacketId Id { get; }
    public int Size { get; }
    public PacketSource Source { get; }

    public PacketMeta(string name, PacketId id, int size, PacketSource source)
    {
        Name = name;
        Id = id;
        Size = size;
        Source = source;
    }
    
    public string ToLog()
    {
        return $"Id:{Id} Name:{Name} Size:{Size} Source:{Source}";
    }
    
    public static PacketMeta Get(PacketId packetId)
    {
        return Lookup[packetId];
    }

    public static bool TryGet(PacketId packetId, out PacketMeta packetMeta)
    {
        return Lookup.TryGetValue(packetId, out packetMeta);
    }
    
    public static readonly PacketMeta OnPingTestInf = new(
        "OnPingTestInf",
        PacketId.OnPingTestInf,
        PacketSize.OnPingTestInf,
        PacketSource.Server
    );

    public static readonly PacketMeta ConnectReq = new(
        "ConnectReq",
        PacketId.ConnectReq,
        PacketSize.ConnectReq,
        PacketSource.Client
    );

    public static readonly PacketMeta OnConnectAck = new(
        "OnConnectAck",
        PacketId.OnConnectAck,
        PacketSize.OnConnectAck,
        PacketSource.Server
    );

    public static readonly PacketMeta AuthenticateInSndAccReq = new(
        "AuthenticateInSndAccReq",
        PacketId.AuthenticateInSndAccReq,
        PacketSize.AuthenticateInSndAccReq,
        PacketSource.Client
    );

    public static readonly PacketMeta OnAuthenticateInAck = new(
        "OnAuthenticateInAck",
        PacketId.OnAuthenticateInAck,
        PacketSize.OnAuthenticateInAck,
        PacketSource.Server
    );
    
    private static readonly Dictionary<PacketId, PacketMeta> Lookup = new Dictionary<PacketId, PacketMeta>()
    {
        { PacketId.OnPingTestInf, OnPingTestInf },
        { PacketId.ConnectReq, ConnectReq },
        { PacketId.OnConnectAck, OnConnectAck },
        { PacketId.AuthenticateInSndAccReq, AuthenticateInSndAccReq },
        { PacketId.OnAuthenticateInAck, OnAuthenticateInAck },
    };
}