using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Vstavka.Core;

namespace Vstavka.Platform.MacOS;

internal sealed partial class KeychainHistoryKeyStore : IHistoryKeyStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
#if VSTAVKA_MAC_APP_STORE
    private const string Service = "io.github.xopoko.Vstavka.AppStore";
#else
    private const string Service = "io.github.xopoko.Vstavka";
#endif
    private const string Account = "history-key";
    private const int KeySize = 32;
    private const int Success = 0;
    private const int DuplicateItem = -25299;
    private const int ItemNotFound = -25300;

    private static nint Library { get; } = NativeLibrary.Load(Security);

    internal void EnsureKey()
    {
        var key = TryLoadKey();
        if (key is not null)
        {
            CryptographicOperations.ZeroMemory(key);
            return;
        }

        key = RandomNumberGenerator.GetBytes(KeySize);
        try
        {
            var status = Add(key);
            if (status != DuplicateItem)
                ThrowIfFailed(status, "add");

            // Another instance may have created its own key after the lookup. Keep that key.
            var existing = TryLoadKey() ?? throw new CryptographicException("The Keychain history key disappeared.");
            CryptographicOperations.ZeroMemory(existing);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[]? TryLoadKey()
    {
        using var pool = new AutoreleasePool();
        var query = CreateItemQuery();
        try
        {
            Set(query, "kSecReturnData", CoreFoundation.True);
            var status = SecItemCopyMatching(query, out var data);
            try
            {
                if (status == ItemNotFound)
                    return null;
                ThrowIfFailed(status, "read");
                if (ObjC.Get(data, "length") != KeySize)
                    throw new CryptographicException("The Keychain history key has an unexpected length.");
                return ObjC.ReadData(data, KeySize);
            }
            finally
            {
                CoreFoundation.Release(data);
            }
        }
        finally
        {
            CoreFoundation.Release(query);
        }
    }

    public void SaveKey(byte[] key)
    {
        if (key.Length != KeySize)
            throw new CryptographicException("The new history key has an unexpected length.");
        var status = Add(key);
        if (status != DuplicateItem)
        {
            ThrowIfFailed(status, "add");
            return;
        }

        // AesGcmHistoryProtector encrypts with the bytes it passed in. Replacing
        // another instance's key here could make that instance's history unreadable.
        var existing = TryLoadKey() ?? throw new CryptographicException("The Keychain history key disappeared.");
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(existing, key))
                throw new CryptographicException("A different Keychain history key already exists.");
            Update(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(existing);
        }
    }

    private static int Add(byte[] key)
    {
        using var pool = new AutoreleasePool();
        var query = CreateItemQuery();
        try
        {
            Set(query, "kSecValueData", ObjC.Data(key));
            return SecItemAdd(query, 0);
        }
        finally
        {
            CoreFoundation.Release(query);
        }
    }

    private static void Update(byte[] key)
    {
        using var pool = new AutoreleasePool();
        var query = CreateItemQuery();
        var changes = CoreFoundation.CreateDictionary();
        try
        {
            Set(changes, "kSecValueData", ObjC.Data(key));
            ThrowIfFailed(SecItemUpdate(query, changes), "update");
        }
        finally
        {
            CoreFoundation.Release(changes);
            CoreFoundation.Release(query);
        }
    }

    private static nint CreateItemQuery()
    {
        var query = CoreFoundation.CreateDictionary();
        try
        {
            Set(query, "kSecClass", Constant("kSecClassGenericPassword"));
            Set(query, "kSecAttrService", ObjC.String(Service));
            Set(query, "kSecAttrAccount", ObjC.String(Account));
            Set(query, "kSecUseAuthenticationUI", Constant("kSecUseAuthenticationUIFail"));
            return query;
        }
        catch
        {
            CoreFoundation.Release(query);
            throw;
        }
    }

    private static nint Constant(string name) => CoreFoundation.ReadConstant(Library, name);
    private static void Set(nint dictionary, string key, nint value) => CoreFoundation.SetValue(dictionary, Constant(key), value);

    private static void ThrowIfFailed(int status, string operation)
    {
        if (status != Success)
            throw new CryptographicException($"Could not {operation} the macOS Keychain history key (OSStatus {status}).");
    }

    [LibraryImport(Security)]
    private static partial int SecItemCopyMatching(nint query, out nint result);
    [LibraryImport(Security)]
    private static partial int SecItemAdd(nint attributes, nint result);
    [LibraryImport(Security)]
    private static partial int SecItemUpdate(nint query, nint attributesToUpdate);
}
