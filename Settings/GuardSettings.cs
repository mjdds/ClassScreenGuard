namespace ClassScreenGuard.Settings;

/// <summary>锁屏插件的持久化数据，只保存密码哈希和盐。</summary>
public sealed class GuardSettings
{
    public string PasswordSalt { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
}
