namespace ClassScreenGuard.Settings;

/// <summary>锁屏插件的持久化数据，只保存密码哈希、盐和普通密码冷却状态。</summary>
public sealed class GuardSettings
{
    public string PasswordSalt { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string AdminPasswordSalt { get; set; } = string.Empty;

    public string AdminPasswordHash { get; set; } = string.Empty;

    public int AdminPasswordLength { get; set; }

    public int FailedAttempts { get; set; }

    public DateTimeOffset? LockoutUntilUtc { get; set; }

    public bool ResumeLockAfterLogoff { get; set; }

    public bool LockScheduleEnabled { get; set; }

    public TimeSpan AllowedLockStart { get; set; } = TimeSpan.FromHours(7);

    public TimeSpan AllowedLockEnd { get; set; } = TimeSpan.FromHours(18);
}
