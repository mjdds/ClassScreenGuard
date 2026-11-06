using System.Security.Cryptography;
using System.Text.Json;
using ClassScreenGuard.Settings;

namespace ClassScreenGuard.Services;

public enum NormalPasswordAttemptStatus
{
    Success,
    Invalid,
    Cooldown
}

public readonly record struct NormalPasswordAttempt(
    NormalPasswordAttemptStatus Status,
    int FailedAttempts,
    TimeSpan RetryAfter);

/// <summary>负责密码哈希校验以及普通密码失败次数和冷却状态的持久化。</summary>
public sealed class GuardSettingsService
{
    public const int MaxNormalPasswordAttempts = 10;
    public static readonly TimeSpan NormalPasswordCooldown = TimeSpan.FromMinutes(5);

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
                return HasPasswordLocked();
            }
        }
    }

    public bool HasAdminPassword
    {
        get
        {
            lock (_sync)
            {
                return HasAdminPasswordLocked();
            }
        }
    }

    public int AdminPasswordLength
    {
        get
        {
            lock (_sync)
            {
                return HasAdminPasswordLocked() ? _settings.AdminPasswordLength : 0;
            }
        }
    }

    public (bool Enabled, TimeSpan Start, TimeSpan End) GetLockSchedule()
    {
        lock (_sync)
        {
            return (_settings.LockScheduleEnabled, _settings.AllowedLockStart, _settings.AllowedLockEnd);
        }
    }

    public bool IsLockAllowedNow()
    {
        lock (_sync)
        {
            return IsLockAllowedAtLocked(DateTime.Now.TimeOfDay);
        }
    }

    public void SetPassword(string password)
    {
        ValidatePassword(password, "解锁密码");
        var (salt, hash) = CreateHash(password);

        lock (_sync)
        {
            _settings.PasswordSalt = salt;
            _settings.PasswordHash = hash;
            ResetFailuresLocked();
            SaveLocked();
        }
    }

    public void SetLockSchedule(bool enabled, TimeSpan start, TimeSpan end)
    {
        if (!IsValidTimeOfDay(start) || !IsValidTimeOfDay(end))
        {
            throw new ArgumentException("允许锁定时间必须是 00:00 到 23:59 之间的时间。");
        }

        lock (_sync)
        {
            _settings.LockScheduleEnabled = enabled;
            _settings.AllowedLockStart = start;
            _settings.AllowedLockEnd = end;
            SaveLocked();
        }
    }

    public bool ChangeAdminPassword(string? currentPassword, string password)
    {
        ValidatePassword(password, "管理员密码");

        lock (_sync)
        {
            if (HasAdminPasswordLocked() &&
                !VerifyHash(currentPassword, _settings.AdminPasswordSalt, _settings.AdminPasswordHash))
            {
                return false;
            }

            var (salt, hash) = CreateHash(password);
            _settings.AdminPasswordSalt = salt;
            _settings.AdminPasswordHash = hash;
            _settings.AdminPasswordLength = password.Length;
            SaveLocked();
            return true;
        }
    }

    public NormalPasswordAttempt TryNormalPassword(string? password)
    {
        lock (_sync)
        {
            var retryAfter = GetLockoutRemainingLocked();
            if (retryAfter > TimeSpan.Zero)
            {
                return new NormalPasswordAttempt(
                    NormalPasswordAttemptStatus.Cooldown,
                    _settings.FailedAttempts,
                    retryAfter);
            }

            if (VerifyHash(password, _settings.PasswordSalt, _settings.PasswordHash))
            {
                ResetFailuresLocked();
                SaveLocked();
                return new NormalPasswordAttempt(NormalPasswordAttemptStatus.Success, 0, TimeSpan.Zero);
            }

            _settings.FailedAttempts = Math.Min(
                MaxNormalPasswordAttempts,
                Math.Max(0, _settings.FailedAttempts) + 1);

            if (_settings.FailedAttempts >= MaxNormalPasswordAttempts)
            {
                _settings.LockoutUntilUtc = DateTimeOffset.UtcNow + NormalPasswordCooldown;
                SaveLocked();
                return new NormalPasswordAttempt(
                    NormalPasswordAttemptStatus.Cooldown,
                    _settings.FailedAttempts,
                    NormalPasswordCooldown);
            }

            SaveLocked();
            return new NormalPasswordAttempt(
                NormalPasswordAttemptStatus.Invalid,
                _settings.FailedAttempts,
                TimeSpan.Zero);
        }
    }

    public bool TryAdminPassword(string? password)
    {
        lock (_sync)
        {
            if (!VerifyHash(password, _settings.AdminPasswordSalt, _settings.AdminPasswordHash))
            {
                return false;
            }

            _settings.AdminPasswordLength = password!.Length;
            ResetFailuresLocked();
            SaveLocked();
            return true;
        }
    }

    public void SetResumeLockAfterLogoff(bool resume)
    {
        lock (_sync)
        {
            if (_settings.ResumeLockAfterLogoff == resume)
            {
                return;
            }

            _settings.ResumeLockAfterLogoff = resume;
            SaveLocked();
        }
    }

    public bool ConsumeResumeLockAfterLogoff()
    {
        lock (_sync)
        {
            var resume = _settings.ResumeLockAfterLogoff;
            if (resume)
            {
                _settings.ResumeLockAfterLogoff = false;
                SaveLocked();
            }
            return resume;
        }
    }

    public TimeSpan GetLockoutRemaining()
    {
        lock (_sync)
        {
            return GetLockoutRemainingLocked();
        }
    }

    private TimeSpan GetLockoutRemainingLocked()
    {
        if (_settings.LockoutUntilUtc is not { } lockoutUntil)
        {
            return TimeSpan.Zero;
        }

        var remaining = lockoutUntil - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            return remaining;
        }

        ResetFailuresLocked();
        SaveLocked();
        return TimeSpan.Zero;
    }

    private bool IsLockAllowedAtLocked(TimeSpan time)
    {
        if (!_settings.LockScheduleEnabled)
        {
            return true;
        }

        var start = _settings.AllowedLockStart;
        var end = _settings.AllowedLockEnd;
        if (!IsValidTimeOfDay(start) || !IsValidTimeOfDay(end) || start == end)
        {
            return true;
        }

        return start < end
            ? time >= start && time < end
            : time >= start || time < end;
    }

    private static bool IsValidTimeOfDay(TimeSpan time) =>
        time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);

    private void ResetFailuresLocked()
    {
        _settings.FailedAttempts = 0;
        _settings.LockoutUntilUtc = null;
    }

    private bool HasPasswordLocked() =>
        !string.IsNullOrWhiteSpace(_settings.PasswordSalt) &&
        !string.IsNullOrWhiteSpace(_settings.PasswordHash);

    private bool HasAdminPasswordLocked() =>
        !string.IsNullOrWhiteSpace(_settings.AdminPasswordSalt) &&
        !string.IsNullOrWhiteSpace(_settings.AdminPasswordHash);

    private static void ValidatePassword(string password, string displayName)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            throw new ArgumentException($"{displayName}至少需要 4 个字符。", nameof(password));
        }
    }

    private static (string Salt, string Hash) CreateHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            32);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    private static bool VerifyHash(string? password, string saltText, string hashText)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(saltText) ||
            string.IsNullOrWhiteSpace(hashText))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltText);
            var expected = Convert.FromBase64String(hashText);
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
            if (!string.IsNullOrWhiteSpace(directory))
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
