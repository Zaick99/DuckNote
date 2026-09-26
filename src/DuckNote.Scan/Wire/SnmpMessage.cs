using System.Text;

namespace DuckNote.Scan.Wire;

public static class SnmpMessage
{
    private const int Version2c = 1;

    public static readonly IReadOnlyList<(string Field, int[] Oid)> SystemGroup =
    [
        ("Description", [1, 3, 6, 1, 2, 1, 1, 1, 0]),
        ("ObjectId",    [1, 3, 6, 1, 2, 1, 1, 2, 0]),
        ("Uptime",      [1, 3, 6, 1, 2, 1, 1, 3, 0]),
        ("Contact",     [1, 3, 6, 1, 2, 1, 1, 4, 0]),
        ("Name",        [1, 3, 6, 1, 2, 1, 1, 5, 0]),
        ("Location",    [1, 3, 6, 1, 2, 1, 1, 6, 0])
    ];

    public static byte[] BuildGet(string community, IReadOnlyList<(string Field, int[] Oid)> oids, int requestId)
    {
        List<byte> varbinds = [];
        foreach ((_, int[] oid) in oids)
        {
            List<byte> pair = [];
            pair.AddRange(BerWriter.Tlv(BerWriter.ObjectIdentifier, BerWriter.EncodeOid(oid)));
            pair.AddRange(BerWriter.Tlv(BerWriter.Null, []));
            varbinds.AddRange(BerWriter.Tlv(BerWriter.Sequence, [.. pair]));
        }

        List<byte> pdu = [];
        pdu.AddRange(BerWriter.Tlv(BerWriter.Integer, BerWriter.EncodeUnsignedInteger(requestId)));
        pdu.AddRange(BerWriter.Tlv(BerWriter.Integer, [0]));
        pdu.AddRange(BerWriter.Tlv(BerWriter.Integer, [0]));
        pdu.AddRange(BerWriter.Tlv(BerWriter.Sequence, [.. varbinds]));

        List<byte> message = [];
        message.AddRange(BerWriter.Tlv(BerWriter.Integer, [Version2c]));
        message.AddRange(BerWriter.Tlv(BerWriter.OctetString, Encoding.ASCII.GetBytes(community)));
        message.AddRange(BerWriter.Tlv(BerWriter.GetRequest, [.. pdu]));

        return BerWriter.Tlv(BerWriter.Sequence, [.. message]);
    }

    public static IReadOnlyDictionary<string, string> ReadResponse(
        byte[] data, IReadOnlyList<(string Field, int[] Oid)> asked)
    {
        Dictionary<string, string> values = [];
        if (data.Length < 10)
        {
            return values;
        }

        IReadOnlyList<BerReader.Node> top = BerReader.Children(data, 0, data.Length);
        if (top.Count < 1)
        {
            return values;
        }

        IReadOnlyList<BerReader.Node> envelope =
            BerReader.Children(data, top[0].Offset, top[0].Offset + top[0].Length);

        BerReader.Node? pdu = FirstWithTag(envelope, BerWriter.GetResponse);
        if (pdu is null)
        {
            return values;
        }

        IReadOnlyList<BerReader.Node> pduParts =
            BerReader.Children(data, pdu.Value.Offset, pdu.Value.Offset + pdu.Value.Length);

        BerReader.Node? varbindList = FirstWithTag(pduParts, BerWriter.Sequence);
        if (varbindList is null)
        {
            return values;
        }

        IReadOnlyList<BerReader.Node> varbinds =
            BerReader.Children(data, varbindList.Value.Offset, varbindList.Value.Offset + varbindList.Value.Length);

        for (int i = 0; i < varbinds.Count && i < asked.Count; i++)
        {
            IReadOnlyList<BerReader.Node> pair =
                BerReader.Children(data, varbinds[i].Offset, varbinds[i].Offset + varbinds[i].Length);

            if (pair.Count < 2)
            {
                continue;
            }

            string value = BerReader.ReadValue(data, pair[1]);
            if (value.Length > 0)
            {
                values[asked[i].Field] = value;
            }
        }

        return values;
    }

    private static BerReader.Node? FirstWithTag(IReadOnlyList<BerReader.Node> nodes, byte tag)
    {
        foreach (BerReader.Node node in nodes)
        {
            if (node.Tag == tag)
            {
                return node;
            }
        }
        return null;
    }
}
