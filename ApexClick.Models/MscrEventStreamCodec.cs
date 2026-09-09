using System.Text;
using System.Text.Json;

namespace ApexClick.Models;

internal static class MscrEventStreamCodec
{
    private const int Version = 1;
    private static readonly JsonSerializerOptions PayloadOptions = new();

    public static void Write(Stream stream, IReadOnlyList<MacroEvent> events)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(events);

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(events.Count);

        long previousTimestamp = 0;

        foreach (var e in events)
        {
            writer.Write(e.Id.ToByteArray());

            var delta = checked(e.TimestampMs - previousTimestamp);
            if (delta < 0)
                throw new InvalidDataException("Events must be ordered by TimestampMs.");

            writer.Write(delta);
            writer.Write((int)e.Type);

            byte flags = 0;
            if (e.NormalizedX.HasValue) flags |= 1 << 0;
            if (e.NormalizedY.HasValue) flags |= 1 << 1;
            if (e.VirtualKeyCode.HasValue) flags |= 1 << 2;
            if (e.ScanCode.HasValue) flags |= 1 << 3;
            if (e.KeyboardLayoutId is not null) flags |= 1 << 4;
            if (e.Payload is not null) flags |= 1 << 5;
            writer.Write(flags);

            if (e.NormalizedX is { } x) writer.Write(x);
            if (e.NormalizedY is { } y) writer.Write(y);
            if (e.VirtualKeyCode is { } vk) writer.Write(vk);
            if (e.ScanCode is { } sc) writer.Write(sc);
            if (e.KeyboardLayoutId is { } layout) writer.Write(layout);

            if (e.Payload is not null)
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(e.Payload, PayloadOptions);
                writer.Write(json.Length);
                writer.Write(json);
            }

            previousTimestamp = e.TimestampMs;
        }
    }

    public static List<MacroEvent> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var version = reader.ReadInt32();

        if (version != Version)
            throw new InvalidDataException($"Unsupported raw-event stream version: {version}.");

        var count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException("Invalid raw-event count.");

        var result = new List<MacroEvent>(count);
        long timestamp = 0;

        for (var i = 0; i < count; i++)
        {
            var idBytes = reader.ReadBytes(16);
            if (idBytes.Length != 16)
                throw new InvalidDataException("Unexpected end of event stream.");

            var id = new Guid(idBytes);
            var delta = reader.ReadInt64();
            if (delta < 0)
                throw new InvalidDataException("Negative event timestamp delta.");

            timestamp = checked(timestamp + delta);

            var rawType = reader.ReadInt32();
            if (!Enum.IsDefined(typeof(MacroEventType), rawType))
                throw new InvalidDataException($"Unknown MacroEventType value: {rawType}.");

            var flags = reader.ReadByte();

            double? x = (flags & (1 << 0)) != 0 ? reader.ReadDouble() : null;
            double? y = (flags & (1 << 1)) != 0 ? reader.ReadDouble() : null;
            int? vk = (flags & (1 << 2)) != 0 ? reader.ReadInt32() : null;
            int? sc = (flags & (1 << 3)) != 0 ? reader.ReadInt32() : null;
            string? layout = (flags & (1 << 4)) != 0 ? reader.ReadString() : null;

            object? payload = null;
            if ((flags & (1 << 5)) != 0)
            {
                var length = reader.ReadInt32();
                if (length < 0 || length > 64 * 1024 * 1024)
                    throw new InvalidDataException("Invalid payload length.");

                var bytes = reader.ReadBytes(length);
                if (bytes.Length != length)
                    throw new InvalidDataException("Unexpected end of payload.");

                payload = JsonSerializer.Deserialize<JsonElement>(bytes, PayloadOptions);
            }

            result.Add(new MacroEvent
            {
                Id = id,
                TimestampMs = timestamp,
                Type = (MacroEventType)rawType,
                NormalizedX = x,
                NormalizedY = y,
                VirtualKeyCode = vk,
                ScanCode = sc,
                KeyboardLayoutId = layout,
                Payload = payload
            });
        }

        return result;
    }
}
