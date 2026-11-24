using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Vstavka.Core;

namespace Vstavka.Platform.Linux;

internal sealed class SecretServiceKeyStore : IHistoryKeyStore
{
    private const string Secret = "libsecret-1.so.0";
    private const string Glib = "libglib-2.0.so.0";
    private const string Gobject = "libgobject-2.0.so.0";
    private const string Schema = "io.github.xopoko.Vstavka.HistoryKey";
    private const string Entry = "entry";
    private const string EntryName = "history-v1";

    public static SecretServiceKeyStore? TryCreate()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            return null;

        try
        {
            if (Environment.GetEnvironmentVariable("SNAP_NAME") == "vstavka")
                return CanUseSecretPortal() ? new() : null;

            var service = secret_service_get_sync(0, 0, out var error);
            FreeError(error);
            if (service == 0)
                return null;
            try
            {
                var collection = secret_collection_for_alias_sync(service, "default", 0, 0, out error);
                FreeError(error);
                if (collection == 0)
                    return null;
                try
                {
                    return secret_collection_get_locked(collection) == 0 ? new() : null;
                }
                finally
                {
                    g_object_unref(collection);
                }
            }
            finally
            {
                g_object_unref(service);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static bool CanUseSecretPortal()
    {
        var schema = NewSchema();
        try
        {
            // Password lookup uses libsecret's confined backend. Collection APIs call the host keyring directly.
            var value = secret_password_lookup_sync(schema, 0, out var error, Entry, EntryName, 0);
            if (value != 0)
                secret_password_free(value);
            FreeError(error);
            return error == 0;
        }
        finally
        {
            secret_schema_unref(schema);
        }
    }

    public byte[]? TryLoadKey()
    {
        var schema = NewSchema();
        try
        {
            var value = secret_password_lookup_sync(schema, 0, out var error, Entry, EntryName, 0);
            try
            {
                CheckError(error);
                return value == 0 ? null : Convert.FromBase64String(Marshal.PtrToStringUTF8(value)!);
            }
            catch (FormatException errorFormat)
            {
                throw new CryptographicException("The Secret Service history key is invalid.", errorFormat);
            }
            finally
            {
                if (value != 0)
                    secret_password_free(value);
            }
        }
        finally
        {
            secret_schema_unref(schema);
        }
    }

    public void SaveKey(byte[] key)
    {
        using var mutex = new Mutex(false, Schema, new NamedWaitHandleOptions
        {
            CurrentUserOnly = true,
            CurrentSessionOnly = false,
        });
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(3)))
                throw new CryptographicException("Another Vstavka process is saving the history key.");
        }
        catch (AbandonedMutexException)
        {
            // The lock was acquired. Check the stored entry again before saving.
        }

        try
        {
            var existing = TryLoadKey();
            if (existing is null)
            {
                StoreNewKey(key);
                return;
            }
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(existing, key))
                    throw new CryptographicException("Another Vstavka process saved a history key. Retry saving history.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(existing);
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void StoreNewKey(byte[] key)
    {
        var schema = NewSchema();
        var password = new byte[45];
        try
        {
            if (key.Length != 32 || System.Buffers.Text.Base64.EncodeToUtf8(key, password, out _, out _) !=
                System.Buffers.OperationStatus.Done)
                throw new CryptographicException("The history key must be 32 bytes.");
            var saved = secret_password_store_sync(schema, "default", "Vstavka history encryption key",
                password, 0, out var error, Entry, EntryName, 0);
            CheckError(error);
            if (saved == 0)
                throw new CryptographicException("Secret Service could not save the history key.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            secret_schema_unref(schema);
        }
    }

    private static nint NewSchema()
    {
        var schema = secret_schema_new(Schema, 0, Entry, 0, 0); // SECRET_SCHEMA_ATTRIBUTE_STRING
        return schema != 0 ? schema : throw new CryptographicException("Could not create the Secret Service schema.");
    }

    private static void CheckError(nint error)
    {
        if (error == 0)
            return;
        FreeError(error);
        throw new CryptographicException("Secret Service could not access the history key.");
    }

    private static void FreeError(nint error)
    {
        if (error != 0)
            g_error_free(error);
    }

    // The libsecret variadic APIs take one fixed string attribute and a trailing
    // NULL. The additional arguments use integer registers on Linux x64 and arm64.
    [DllImport(Secret)]
    private static extern nint secret_schema_new([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attribute, int type, nint end);

    [DllImport(Secret)]
    private static extern nint secret_password_lookup_sync(nint schema, nint cancellable, out nint error,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attribute, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, nint end);

    [DllImport(Secret)]
    private static extern int secret_password_store_sync(nint schema,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        byte[] password, nint cancellable, out nint error,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attribute, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, nint end);

    [DllImport(Secret)] private static extern void secret_schema_unref(nint schema);
    [DllImport(Secret)] private static extern void secret_password_free(nint password);
    [DllImport(Secret)] private static extern nint secret_service_get_sync(int flags, nint cancellable, out nint error);
    [DllImport(Secret)] private static extern nint secret_collection_for_alias_sync(nint service,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string alias, int flags, nint cancellable, out nint error);
    [DllImport(Secret)] private static extern int secret_collection_get_locked(nint collection);
    [DllImport(Glib)] private static extern void g_error_free(nint error);
    [DllImport(Gobject)] private static extern void g_object_unref(nint instance);
}
