using System.Security.Cryptography;

namespace Vstavka.Core;

/// <summary>Encrypts the history file for the current user. Each platform supplies its own implementation.</summary>
public interface IHistoryProtector
{
    byte[] Protect(byte[] clearBytes);

    /// <exception cref="CryptographicException">The payload cannot be decrypted with the current key.</exception>
    /// <exception cref="IOException">The key could not be loaded.</exception>
    byte[] Unprotect(byte[] protectedBytes);
}

/// <summary>Keeps the history key in the operating system's credential store (Keychain, Secret Service).</summary>
public interface IHistoryKeyStore
{
    /// <returns>A copy of the stored key, or null when no key has been saved yet.</returns>
    byte[]? TryLoadKey();

    void SaveKey(byte[] key);
}

/// <summary>Windows DPAPI for the current Windows account. The file format is unchanged from earlier releases.</summary>
public sealed class WindowsHistoryProtector : IHistoryProtector
{
    public byte[] Protect(byte[] clearBytes) => WindowsDataProtection.Protect(clearBytes);
    public byte[] Unprotect(byte[] protectedBytes) => WindowsDataProtection.Unprotect(protectedBytes);
}

/// <summary>
/// AES-256-GCM with a random key held by <see cref="IHistoryKeyStore"/>.
/// Layout: 16-byte format marker, 12-byte nonce, 16-byte tag, ciphertext. The marker is authenticated.
/// </summary>
public sealed class AesGcmHistoryProtector(IHistoryKeyStore keyStore) : IHistoryProtector
{
    private const int KeySize = 32, NonceSize = 12, TagSize = 16;
    private static ReadOnlySpan<byte> Marker => "VSTAVKA-AESGCM-1"u8;
    private static int HeaderSize => Marker.Length + NonceSize + TagSize;

    public byte[] Protect(byte[] clearBytes)
    {
        ArgumentNullException.ThrowIfNull(clearBytes);
        var key = keyStore.TryLoadKey();
        try
        {
            if (key is null)
            {
                key = RandomNumberGenerator.GetBytes(KeySize);
                keyStore.SaveKey(key);
            }
            if (key.Length != KeySize)
                throw new CryptographicException("The stored history key has an unexpected length.");
            var output = new byte[HeaderSize + clearBytes.Length];
            Marker.CopyTo(output);
            var nonce = output.AsSpan(Marker.Length, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, clearBytes, output.AsSpan(HeaderSize), output.AsSpan(Marker.Length + NonceSize, TagSize), Marker);
            return output;
        }
        finally
        {
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Unprotect(byte[] protectedBytes)
    {
        ArgumentNullException.ThrowIfNull(protectedBytes);
        if (protectedBytes.Length < HeaderSize || !protectedBytes.AsSpan(0, Marker.Length).SequenceEqual(Marker))
            throw new CryptographicException("The history file is not in the expected encrypted format.");
        byte[]? key;
        try
        {
            key = keyStore.TryLoadKey();
        }
        catch (CryptographicException error)
        {
            throw new IOException("Could not read the history key from the system credential store.", error);
        }
        if (key is null)
            throw new IOException("The history key is missing from the system credential store.");
        try
        {
            if (key.Length != KeySize)
                throw new IOException("The stored history key has an unexpected length.");
            var clear = new byte[protectedBytes.Length - HeaderSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(protectedBytes.AsSpan(Marker.Length, NonceSize), protectedBytes.AsSpan(HeaderSize),
                protectedBytes.AsSpan(Marker.Length + NonceSize, TagSize), clear, Marker);
            return clear;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
