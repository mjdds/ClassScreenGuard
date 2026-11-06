using System.Text;

namespace ClassScreenGuard.Services;

/// <summary>在内存滚动缓冲区中检测插件自身的 Admin 密码，不记录或持久化输入内容。</summary>
public sealed class AdminPasswordSequenceDetector : IDisposable
{
    private const int MaxBufferedCharacters = 256;
    private const int MaxLegacyPasswordLength = 64;
    private static readonly TimeSpan LegacyVerificationDelay = TimeSpan.FromMilliseconds(150);

    private readonly GuardSettingsService _settings;
    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();
    private CancellationTokenSource? _legacyDelay;
    private VerificationRequest? _pendingRequest;
    private bool _workerRunning;
    private bool _active;
    private int _generation;

    public AdminPasswordSequenceDetector(GuardSettingsService settings)
    {
        _settings = settings;
    }

    public event Action? UnlockRequested;

    public void SetActive(bool active)
    {
        lock (_sync)
        {
            _active = active;
            _generation++;
            _buffer.Clear();
            _pendingRequest = null;
            CancelLegacyDelayLocked();
        }
    }

    public void Append(string text)
    {
        lock (_sync)
        {
            if (!_active || string.IsNullOrEmpty(text) || !_settings.HasAdminPassword)
            {
                return;
            }

            _buffer.Append(text);
            if (_buffer.Length > MaxBufferedCharacters)
            {
                _buffer.Remove(0, _buffer.Length - MaxBufferedCharacters);
            }

            var passwordLength = _settings.AdminPasswordLength;
            if (passwordLength >= 4 && passwordLength <= MaxBufferedCharacters)
            {
                CancelLegacyDelayLocked();
                if (_buffer.Length < passwordLength)
                {
                    return;
                }

                var candidate = _buffer.ToString(_buffer.Length - passwordLength, passwordLength);
                QueueRequestLocked(new VerificationRequest(candidate, false, _generation));
                return;
            }

            RestartLegacyDelayLocked();
        }
    }

    public void Interrupt()
    {
        lock (_sync)
        {
            _buffer.Clear();
            CancelLegacyDelayLocked();
        }
    }

    private void RestartLegacyDelayLocked()
    {
        CancelLegacyDelayLocked();
        _legacyDelay = new CancellationTokenSource();
        var generation = _generation;
        _ = QueueLegacyVerificationAfterDelayAsync(generation, _legacyDelay.Token);
    }

    private async Task QueueLegacyVerificationAfterDelayAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(LegacyVerificationDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_sync)
        {
            if (!_active || generation != _generation || cancellationToken.IsCancellationRequested ||
                _buffer.Length < 4)
            {
                return;
            }

            QueueRequestLocked(new VerificationRequest(_buffer.ToString(), true, generation));
        }
    }

    private void QueueRequestLocked(VerificationRequest request)
    {
        _pendingRequest = request;
        if (_workerRunning)
        {
            return;
        }

        _workerRunning = true;
        _ = Task.Run(ProcessQueue);
    }

    private void ProcessQueue()
    {
        while (true)
        {
            VerificationRequest request;
            lock (_sync)
            {
                if (_pendingRequest is not { } pending)
                {
                    _workerRunning = false;
                    return;
                }

                request = pending;
                _pendingRequest = null;
            }

            var verified = request.IsLegacy
                ? VerifyLegacySuffixes(request.Text)
                : _settings.TryAdminPassword(request.Text);
            if (!verified)
            {
                continue;
            }

            bool shouldNotify;
            lock (_sync)
            {
                shouldNotify = _active && request.Generation == _generation;
                if (shouldNotify)
                {
                    _active = false;
                    _buffer.Clear();
                    _pendingRequest = null;
                    CancelLegacyDelayLocked();
                }
            }

            if (shouldNotify)
            {
                UnlockRequested?.Invoke();
            }
            return;
        }
    }

    private bool VerifyLegacySuffixes(string text)
    {
        var maximumLength = Math.Min(MaxLegacyPasswordLength, text.Length);
        for (var length = 4; length <= maximumLength; length++)
        {
            if (_settings.TryAdminPassword(text[^length..]))
            {
                return true;
            }
        }

        return false;
    }

    private void CancelLegacyDelayLocked()
    {
        _legacyDelay?.Cancel();
        _legacyDelay?.Dispose();
        _legacyDelay = null;
    }

    public void Dispose() => SetActive(false);

    private readonly record struct VerificationRequest(string Text, bool IsLegacy, int Generation);
}
