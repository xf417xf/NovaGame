using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NovaGame.Application.Operations.AdminAccess;

internal static class AdminAccessCommandFingerprint
{
    public static byte[] ForAdministratorCreate(
        string normalizedUserName,
        string normalizedDisplayName,
        IReadOnlyCollection<Guid> roleIds)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "AdministratorCreate");
        AppendString(hash, normalizedUserName);
        AppendString(hash, normalizedDisplayName);
        AppendGuids(hash, roleIds);
        return hash.GetHashAndReset();
    }

    public static byte[] ForAdminRoleCreate(
        string normalizedRoleName,
        IReadOnlyCollection<string> permissionNames)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "AdminRoleCreate");
        AppendString(hash, normalizedRoleName);
        AppendStrings(hash, permissionNames);
        return hash.GetHashAndReset();
    }

    public static byte[] ForAdministratorPasswordReset(
        Guid administratorId,
        long expectedRevision)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "AdministratorPasswordReset");
        AppendString(hash, administratorId.ToString("D"));

        Span<byte> revisionBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(revisionBytes, expectedRevision);
        hash.AppendData(revisionBytes);
        return hash.GetHashAndReset();
    }

    private static void AppendGuids(
        IncrementalHash hash,
        IReadOnlyCollection<Guid> values)
    {
        var canonicalValues = values
            .Select(value => value.ToString("D"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        AppendStrings(hash, canonicalValues);
    }

    private static void AppendStrings(
        IncrementalHash hash,
        IReadOnlyCollection<string> values)
    {
        var canonicalValues = values.Order(StringComparer.Ordinal).ToArray();
        Span<byte> countBytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(countBytes, canonicalValues.Length);
        hash.AppendData(countBytes);

        foreach (var value in canonicalValues)
        {
            AppendString(hash, value);
        }
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, bytes.Length);
        hash.AppendData(lengthBytes);
        hash.AppendData(bytes);
    }
}
