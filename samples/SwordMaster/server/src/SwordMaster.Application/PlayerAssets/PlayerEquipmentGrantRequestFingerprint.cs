using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace SwordMaster.Application.PlayerAssets;

public sealed class PlayerEquipmentGrantRequestFingerprint
{
    public string CreateGrant(
        Guid playerId,
        long expectedRevision,
        string configuredItemId,
        string reason)
    {
        return Create(writer =>
        {
            writer.WriteString("command", "grant");
            writer.WriteString("playerId", playerId.ToString("D"));
            writer.WriteNumber("expectedRevision", expectedRevision);
            writer.WriteString("configuredItemId", configuredItemId);
            writer.WriteString("reason", reason);
        });
    }

    public string CreateRollback(
        Guid playerId,
        Guid grantId,
        long expectedRevision,
        string reason)
    {
        return Create(writer =>
        {
            writer.WriteString("command", "rollback");
            writer.WriteString("playerId", playerId.ToString("D"));
            writer.WriteString("grantId", grantId.ToString("D"));
            writer.WriteNumber("expectedRevision", expectedRevision);
            writer.WriteString("reason", reason);
        });
    }

    private static string Create(Action<Utf8JsonWriter> writeProperties)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeProperties(writer);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
    }
}
