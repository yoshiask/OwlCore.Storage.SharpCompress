using System.Security.Cryptography;
using System.Text;

namespace OwlCore.Storage.SharpCompress;

internal static class Extensions
{
    public static string Hash(this string s, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;

        var sBytes = encoding.GetBytes(s);
        var hashBytes = SHA256.Create().ComputeHash(sBytes);

        StringBuilder sb = new(sBytes.Length * 2);
        foreach (var b in hashBytes)
            sb.Append(b.ToString("X2"));
        return sb.ToString();
    }
}
