using System;
using System.Security.Cryptography;
using System.Text;

namespace MaxTweaks.Core.ObsRecording;

/// <summary>
/// obs-websocket 5.x authentication string (protocol.md, "Creating an authentication string"):
/// base64(sha256(base64(sha256(password + salt)) + challenge)), with salt and challenge taken from
/// the server's Hello message.
/// </summary>
public static class ObsAuth
{
    public static string Compute(string? password, string salt, string challenge)
    {
        var secret = Sha256Base64((password ?? string.Empty) + salt);
        return Sha256Base64(secret + challenge);
    }

    private static string Sha256Base64(string text)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }
}
