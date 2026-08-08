using System.Security.Cryptography;
using System.Text;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public static class PermissionId
{
    public static Guid FromName(string permissionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionName);

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"agency.permission:{permissionName}"));

        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);

        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);

        return new Guid(bytes);
    }
}
