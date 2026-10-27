using System.Security.Cryptography;
using System.Text;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace Workspace1C.Services;

/// <summary>
/// Статус операции шифрования/расшифровки секрета заметки базы (п.8, 1.0.0.90):
/// <see cref="Ok"/> — значение получено, <see cref="KeyUnavailable"/> — ключ недоступен
/// (переезд между компьютерами/профилями, повреждённый ключовой файл), 
/// <see cref="NonPortable"/> — значение зашифровано в другом окружении (Windows ↔ Linux)
/// и не может быть расшифровано здесь. Приложение НЕ падает ни в одном из статусов:
/// UI показывает понятный текст («зашифровано, ключ недоступен»).
/// </summary>
public enum NoteCryptoStatus
{
    Ok,
    KeyUnavailable,
    NonPortable
}

/// <summary>Результат операции шифрования/расшифровки секрета.</summary>
public readonly record struct NoteCryptoResult(NoteCryptoStatus Status, string? Value)
{
    public static NoteCryptoResult Ok(string value) => new(NoteCryptoStatus.Ok, value);

    public static NoteCryptoResult Fail(NoteCryptoStatus status) => new(status, null);
}

/// <summary>
/// Источник ключа шифрования секретов заметок. Разделение по платформам:
/// Windows — DPAPI (ключ пользователя, файлы на диск не пишутся); Linux — случайный
/// AES-256 ключ, сохранённый в каталоге данных профиля (<c>notes_crypto.key</c>,
/// права 0600). Инжектируется для юнит-тестов (фиксированный ключ).
/// </summary>
public interface INoteCryptoKeyProvider
{
    /// <summary>Ключ AES-256 (32 байта) или null, если ключ недоступен.</summary>
    byte[]? GetKey();

    /// <summary>Краткое описание источника (для диагностики без раскрытия секретов).</summary>
    string Describe();
}

/// <summary>
/// Шифрование секретов заметок баз (п.8 «Заметки к базе», 1.0.0.90): пароли и ключи
/// внешних сервисов хранятся в <c>infobase_notes.json</c> ТОЛЬКО в зашифрованном виде.
/// Формат конверта — префикс + Base64:
/// <list type="bullet">
/// <item><c>v1:</c> — DPAPI (Windows, учётная запись пользователя);</item>
/// <item><c>v2:</c> — AES-256-GCM ключом из профиля (Linux) либо инжектированным
/// ключом (юнит-тесты).</item>
/// </list>
/// Чистые функции AES-GCM (<see cref="AesGcmBox"/>) вынесены статическими — покрыты
/// юнит-тестами без платформенных зависимостей.
/// </summary>
public sealed class NoteCrypto
{
    /// <summary>Префикс значения, зашифрованного DPAPI (Windows).</summary>
    public const string PrefixDpapi = "v1:";

    /// <summary>Префикс значения, зашифрованного AES-256-GCM ключом профиля (Linux/тесты).</summary>
    public const string PrefixAesProfile = "v2:";

    private readonly INoteCryptoKeyProvider? _keyProvider;

    /// <param name="keyProvider">
    /// Источник ключа шифрования. null — платформенный по умолчанию
    /// (Windows: DPAPI; Linux: ключовой файл профиля).
    /// </param>
    public NoteCrypto(INoteCryptoKeyProvider? keyProvider = null)
    {
        _keyProvider = keyProvider;
    }

    /// <summary>Экземпляр с платформенным источником ключа по умолчанию.</summary>
    public static NoteCrypto CreateDefault() => new(DefaultKeyProvider());

    /// <summary>Платформенный источник ключа по умолчанию (см. <see cref="INoteCryptoKeyProvider"/>).</summary>
    public static INoteCryptoKeyProvider DefaultKeyProvider()
    {
#if WINDOWS
        return new DpapiKeyProvider();
#else
        return new ProfileFileKeyProvider(DataDirectory);
#endif
    }

    private static string DataDirectory
    {
        get
        {
            try { return FeatureDataStore.DataDirectory; }
            catch { return PlatformPaths.AppDataDirectory; }
        }
    }

    /// <summary>
    /// Шифрует открытое значение. Пустая строка → Ok("") без шифрования (секретов нет).
    /// Любой сбой (ключ недоступен, платформа не поддерживается) возвращает
    /// <see cref="NoteCryptoStatus.KeyUnavailable"/> — без исключений наружу.
    /// </summary>
    public NoteCryptoResult Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return NoteCryptoResult.Ok(string.Empty);
        try
        {
            // Инжектированный ключ (юнит-тесты) — явный AES-путь на любой платформе.
            if (_keyProvider is not null)
                return EncryptWithAes(plaintext);

#if WINDOWS
            var protectedBytes = DpapiProtector.Protect(Encoding.UTF8.GetBytes(plaintext));
            return NoteCryptoResult.Ok(PrefixDpapi + Convert.ToBase64String(protectedBytes));
#else
            return EncryptWithAes(plaintext);
#endif
        }
        catch
        {
            return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
        }
    }

    /// <summary>AES-256-GCM ключом профиля (Linux) либо инжектированным ключом (тесты).</summary>
    private NoteCryptoResult EncryptWithAes(string plaintext)
    {
        var key = (_keyProvider ?? DefaultKeyProvider()).GetKey();
        if (key is null)
            return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
        var boxed = AesGcmBox.Encrypt(plaintext, key);
        if (boxed is null)
            return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
        return NoteCryptoResult.Ok(PrefixAesProfile + boxed);
    }

    /// <summary>
    /// Расшифровывает зашифрованное значение. Распознаёт префиксы формата; значение
    /// другой платформы (v1: на Linux, v2: на Windows) возвращает
    /// <see cref="NoteCryptoStatus.NonPortable"/> — «зашифровано, ключ недоступен».
    /// Неизвестный формат — тоже <see cref="NoteCryptoStatus.NonPortable"/>.
    /// </summary>
    public NoteCryptoResult Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return NoteCryptoResult.Ok(string.Empty);

        if (ciphertext.StartsWith(PrefixDpapi, StringComparison.Ordinal))
        {
#if WINDOWS
            try
            {
                var bytes = Convert.FromBase64String(ciphertext.Substring(PrefixDpapi.Length));
                var result = DpapiProtector.Unprotect(bytes);
                if (result is null)
                    return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
                return NoteCryptoResult.Ok(Encoding.UTF8.GetString(result));
            }
            catch
            {
                return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
            }
#else
            // DPAPI-значение создано на Windows (ключ пользователя Windows) — здесь его нет.
            return NoteCryptoResult.Fail(NoteCryptoStatus.NonPortable);
#endif
        }

        if (ciphertext.StartsWith(PrefixAesProfile, StringComparison.Ordinal))
        {
            // Инжектированный ключ (юнит-тесты) работает на любой платформе.
            if (_keyProvider is not null)
            {
                var injectedKey = _keyProvider.GetKey();
                if (injectedKey is null)
                    return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
                var injected = AesGcmBox.Decrypt(ciphertext.Substring(PrefixAesProfile.Length), injectedKey);
                if (injected is null)
                    return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
                return NoteCryptoResult.Ok(injected);
            }

#if WINDOWS
            return NoteCryptoResult.Fail(NoteCryptoStatus.NonPortable);
#else
            var key = DefaultKeyProvider().GetKey();
            if (key is null)
                return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
            var text = AesGcmBox.Decrypt(ciphertext.Substring(PrefixAesProfile.Length), key);
            if (text is null)
                return NoteCryptoResult.Fail(NoteCryptoStatus.KeyUnavailable);
            return NoteCryptoResult.Ok(text);
#endif
        }

        return NoteCryptoResult.Fail(NoteCryptoStatus.NonPortable);
    }

    /// <summary>
    /// Ключ локализации текста статуса (для UI): «зашифровано, ключ недоступен» и т.п.
    /// Пустая строка — для <see cref="NoteCryptoStatus.Ok"/>.
    /// </summary>
    public static string StatusTextKey(NoteCryptoStatus status) => status switch
    {
        NoteCryptoStatus.KeyUnavailable => "Notes.SecretLocked",
        NoteCryptoStatus.NonPortable => "Notes.SecretNonPortable",
        _ => string.Empty
    };
}

/// <summary>
/// Чистый AES-256-GCM контейнер (nonce 12 байт + шифртекст + тег 16 байт) без
/// зависимостей от платформы и внешних библиотек. Покрыт юнит-тестами
/// (round-trip и отказ при неверном ключе).
/// </summary>
public static class AesGcmBox
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    /// <summary>Генерирует случайный ключ AES-256 (для ключового файла профиля Linux).</summary>
    public static byte[] GenerateKey()
    {
        var key = new byte[KeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Шифрует строку: вызывает исключения наружу не должен — фактически никакие
    /// исключения здесь не выбрасываются (аргументы проверяются), возвращает
    /// null только при неверной длине ключа.
    /// </summary>
    public static string? Encrypt(string plaintext, byte[] key)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;
        if (key is null || key.Length != KeySize)
            return null;

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[NonceSize + plainBytes.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes,
            result.AsSpan(NonceSize, plainBytes.Length),
            result.AsSpan(NonceSize + plainBytes.Length, TagSize));

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Расшифровывает строку, созданную <see cref="Encrypt"/>. Возвращает null при
    /// неверном ключе/битых данных (классический сбой — источник статуса
    /// «ключ недоступен»).
    /// </summary>
    public static string? Decrypt(string ciphertext, byte[] key)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;
        if (key is null || key.Length != KeySize)
            return null;

        byte[] data;
        try
        {
            data = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException)
        {
            return null;
        }

        if (data.Length <= NonceSize + TagSize)
            return null;

        var nonce = data.AsSpan(0, NonceSize).ToArray();
        var payloadLength = data.Length - NonceSize - TagSize;
        var payload = data.AsSpan(NonceSize, payloadLength);
        var tag = data.AsSpan(NonceSize + payloadLength, TagSize);

        var plainBytes = new byte[payloadLength];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, payload, tag, plainBytes);
        }
        catch (CryptographicException)
        {
            return null;
        }

        return Encoding.UTF8.GetString(plainBytes);
    }
}

#if WINDOWS
/// <summary>
/// DPAPI-обёртка (криптозащита данных текущего пользователя Windows): шифрование без
/// сохранения ключа на диск — расшифровать может только тот же пользователь на той же
/// машине. Чистый P/Invoke (crypt32.dll), без внешних пакетов.
/// </summary>
internal static class DpapiProtector
{
    private const int CrpUiForbidden = 0x1; // CRYPTPROTECT_UI_FORBIDDEN: без диалога пользователю

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct,
        int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, out IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct,
        int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var input = AllocateBlob(data);
        var output = new DATA_BLOB();
        try
        {
            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CrpUiForbidden, ref output))
                throw new InvalidOperationException("CryptProtectData failed (Win32 error " + Marshal.GetLastWin32Error() + ")");
            return ReadBlob(output);
        }
        finally
        {
            FreeBlob(input);
            if (output.pbData != IntPtr.Zero)
                LocalFree(output.pbData);
        }
    }

    public static byte[]? Unprotect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var input = AllocateBlob(data);
        var output = new DATA_BLOB();
        try
        {
            if (!CryptUnprotectData(ref input, out _, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CrpUiForbidden, ref output))
                return null; // Неверные данные/другой пользователь — статус «ключ недоступен».
            return ReadBlob(output);
        }
        finally
        {
            FreeBlob(input);
            if (output.pbData != IntPtr.Zero)
                LocalFree(output.pbData);
        }
    }

    private static DATA_BLOB AllocateBlob(byte[] data)
    {
        var ptr = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, ptr, data.Length);
            return new DATA_BLOB { cbData = data.Length, pbData = ptr };
        }
        catch
        {
            Marshal.FreeHGlobal(ptr);
            throw;
        }
    }

    private static byte[] ReadBlob(DATA_BLOB blob)
    {
        if (blob.pbData == IntPtr.Zero || blob.cbData <= 0)
            return Array.Empty<byte>();
        var result = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, result, 0, blob.cbData);
        return result;
    }

    private static void FreeBlob(DATA_BLOB blob)
    {
        if (blob.pbData != IntPtr.Zero)
            Marshal.FreeHGlobal(blob.pbData);
    }
}

/// <summary>
/// Источник ключа для Windows: «ключ» отсутствует как файл — вместо него используется
/// DPAPI текущего пользователя (сам <see cref="NoteCrypto"/> на Windows шифрует через
/// DPAPI, provider здесь только описывает источник для единообразия).
/// </summary>
internal sealed class DpapiKeyProvider : INoteCryptoKeyProvider
{
    public byte[]? GetKey() => null; // DPAPI не требует отдельного ключа (см. NoteCrypto.Encrypt).

    public string Describe() => "Windows DPAPI (текущий пользователь)";
}
#else
/// <summary>
/// Ключовой файл профиля (Linux): 32 байта AES-256 в каталоге данных профиля.
/// Файл создаётся при первом обращении (права 0600); недоступность (нет прав на запись,
/// повреждённый каталог) возвращает null — секреты не создаются, UI показывает
/// «зашифровано, ключ недоступен». Переезд на другую машину/профиль без копирования
/// ключового файла делает старые секреты недоступными — это осознанное ограничение
/// (данные не расшифровываются на чужих компьютерах).
/// </summary>
internal sealed class ProfileFileKeyProvider : INoteCryptoKeyProvider
{
    private const string KeyFileName = "notes_crypto.key";
    private readonly string _directory;

    public ProfileFileKeyProvider(string directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? PlatformPaths.AppDataDirectory : directory;
    }

    public byte[]? GetKey()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, KeyFileName);
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing is not null && existing.Length == 32)
                    return existing;
                // Повреждённый/чужой ключовой файл не перезаписываем — «ключ недоступен».
                return null;
            }

            var key = AesGcmBox.GenerateKey();
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, key);
            try
            {
                // Блок компилируется только в Linux-сборку (#else ветка #if WINDOWS выше),
                // но анализатор считает файл кросс-платформенным — глушим CA1416.
#pragma warning disable CA1416
                File.SetUnixFileMode(tmp,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
#pragma warning restore CA1416
            }
            catch (PlatformNotSupportedException)
            {
                // На не-UNIX системах режима нет; файл и так в приватном каталоге профиля.
            }
            File.Move(tmp, path);
            return key;
        }
        catch
        {
            return null;
        }
    }

    public string Describe() => $"AES-256, ключ профиля ({KeyFileName})";
}
#endif
