using System.Security.Cryptography;
using System.Text;

namespace Seans_Account_Manager.Services;

public static class CryptoService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RAM-v1-entropy-salt");

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;
        try
        {
            byte[] encryptedBytes = Convert.FromBase64String(cipherText);
            byte[] decrypted = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return string.Empty;
        }
    }
}
