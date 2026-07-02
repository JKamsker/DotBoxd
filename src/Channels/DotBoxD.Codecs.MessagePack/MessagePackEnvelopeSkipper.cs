using MessagePack;

namespace DotBoxD.Codecs.MessagePack;

internal static class MessagePackEnvelopeSkipper
{
    private const int MaxUnknownFieldDepth = 64;

    public static void SkipUnknownField(ref MessagePackReader reader, string envelopeName)
        => Skip(ref reader, envelopeName, depth: 0);

    private static void Skip(ref MessagePackReader reader, string envelopeName, int depth)
    {
        if (depth > MaxUnknownFieldDepth)
        {
            throw new MessagePackSerializationException(
                $"{envelopeName} contains an unknown field deeper than {MaxUnknownFieldDepth} levels.");
        }

        switch (reader.NextMessagePackType)
        {
            case MessagePackType.Array:
                var arrayCount = reader.ReadArrayHeader();
                for (var i = 0; i < arrayCount; i++)
                {
                    Skip(ref reader, envelopeName, depth + 1);
                }

                break;
            case MessagePackType.Map:
                var mapCount = reader.ReadMapHeader();
                for (var i = 0; i < mapCount; i++)
                {
                    Skip(ref reader, envelopeName, depth + 1);
                    Skip(ref reader, envelopeName, depth + 1);
                }

                break;
            default:
                reader.ReadRaw();
                break;
        }
    }
}
