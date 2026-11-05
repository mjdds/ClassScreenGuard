using System.Security.Cryptography;
using System.Text.Json;
using ClassScreenGuard.Settings;

namespace ClassScreenGuard.Services;

/// <summary>负责锁屏密码的哈希校验与配置持久化。</summary>
public sealed class GuardSettingsService
{
    private const int Iterations = 120_000;
    private readonly string _filePath;
    private readonly object _sync = new();
    private GuardSettings _settings;

    public GuardSettingsService(string configFolder)
    {
        _filePath = Path.Combine(configFolder, "guard.json");
        _settings = Load();
    }

    public string ConfigFilePath => _filePath;

    public bool HasPassword
    {
        get
        {
            lock (_sync)
            {
                return !string.IsNullOrWhiteSpace(_settings.PasswordSalt) &&
                       !string.IsNullOrWhiteSpace(_settings.PasswordHash);
            }
        }
    }

    public void SetPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            throw new ArgumentException("解锁密码至少需要 4 个字符。", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            32);

        lock (_sync)
        {
            _settings = new GuardSettings
            {
                PasswordSalt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(hash)
            };
            SaveLocked();
        }
    }

    public bool VerifyPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        lock (_sync)
        {
            try
            {
                if (!HasPassword)
                {
                    return false;
                }

                var salt = Convert.FromBase64String(_settings.PasswordSalt);
                var expected = Convert.FromBase64String(_settings.PasswordHash);
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    private GuardSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                return JsonSerializer.Deserialize<GuardSettings>(File.ReadAllText(_filePath))
                       ?? new GuardSettings();
            }
        }
        catch
        {
            // 配置损坏时使用未设置密码状态，避免自动锁死。
        }

        return new GuardSettings();
    }

    private void SaveLocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, true);
        }
        catch
        {
            // 保存失败不导致 ClassIsland 崩溃。
        }
    }
}
