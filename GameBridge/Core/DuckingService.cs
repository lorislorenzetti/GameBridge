namespace GameAudioMixer.Core;

/// <summary>
/// Manages the ducking fade factor (0.0 = fully ducked, 1.0 = normal).
/// Does NOT read audio peaks internally — the caller passes the chat peak each Tick()
/// so that the COM peak meter is only read once per frame (Windows resets it on each read).
/// Does NOT touch audio session volumes directly — the caller applies the factor.
/// Both attack and release fades are driven by real wall-clock time (Environment.TickCount64)
/// so their duration is accurate regardless of the DispatcherTimer's actual fire rate.
/// </summary>
public sealed class DuckingService : IDisposable
{
    private bool _isDucked;
    private int _silentFrameCount;
    private float _duckFactor = 1f;

    // Attack fade state
    private bool _attacking;
    private float _attackFadeStart;
    private long _attackStartMs;
    private float _attackTarget;

    // Release fade state
    private bool _releasing;
    private float _releaseFadeStart;
    private long _releaseStartMs;

    public bool Enabled { get; set; } = true;
    public float DuckPercent { get; set; } = 30f;
    public float ActivationThreshold { get; set; } = 0.01f;

    /// <summary>Kept for compatibility — duration is controlled by AttackDurationMs.</summary>
    public float AttackSpeed { get; set; } = 1.0f;
    /// <summary>Kept for compatibility — duration is controlled by ReleaseDurationMs.</summary>
    public float ReleaseSpeed { get; set; } = 0.033f;

    /// <summary>How long (ms) the fade-down takes when chat starts speaking. 0 = instant snap.</summary>
    public float AttackDurationMs { get; set; } = 100f;

    /// <summary>How long (ms) the fade-back-to-normal takes after chat stops speaking.</summary>
    public float ReleaseDurationMs { get; set; } = 500f;

    /// <summary>Frames of silence before the restore begins. ~3 frames ≈ 100ms avoids false triggers.</summary>
    public int SilenceFramesBeforeRestore { get; set; } = 3;

    public bool IsDucked => _isDucked;
    public float DuckFactor => _duckFactor;
    public float LastChatPeak { get; private set; }

    public event Action<bool>? DuckingStateChanged;

    /// <summary>
    /// Advances the duck state machine by one tick.
    /// <paramref name="chatPeak"/> must be the peak value already read from the chat session
    /// this frame (before any other UpdatePeak calls consume it).
    /// Returns the current DuckFactor to apply to game volume.
    /// </summary>
    public float Tick(float chatPeak)
    {
        LastChatPeak = chatPeak;

        if (!Enabled)
        {
            if (_isDucked)
            {
                _isDucked = false;
                _silentFrameCount = 0;
                DuckingStateChanged?.Invoke(false);
            }
            _duckFactor = 1f;
            _attacking = false;
            _releasing = false;
            return 1f;
        }

        bool voiceActive = chatPeak > ActivationThreshold;

        if (voiceActive)
        {
            _silentFrameCount = 0;
            _releasing = false; // Cancel any ongoing restore

            float target = 1f - DuckPercent / 100f;

            if (!_isDucked)
            {
                _isDucked = true;
                DuckingStateChanged?.Invoke(true);

                // Begin timed attack fade
                _attacking = true;
                _attackFadeStart = _duckFactor;
                _attackTarget = target;
                _attackStartMs = Environment.TickCount64;
            }
            else
            {
                // Update target in case DuckPercent changed mid-session
                _attackTarget = target;
            }
        }
        else if (_isDucked)
        {
            _silentFrameCount++;
            if (_silentFrameCount >= SilenceFramesBeforeRestore)
            {
                _isDucked = false;
                _silentFrameCount = 0;
                _attacking = false; // Cancel ongoing attack
                DuckingStateChanged?.Invoke(false);

                // Begin timed release fade
                _releasing = true;
                _releaseFadeStart = _duckFactor;
                _releaseStartMs = Environment.TickCount64;
            }
        }

        // Apply attack fade
        if (_attacking)
        {
            float elapsed = (float)(Environment.TickCount64 - _attackStartMs);
            float t = AttackDurationMs > 0f ? Math.Clamp(elapsed / AttackDurationMs, 0f, 1f) : 1f;
            _duckFactor = _attackFadeStart + t * (_attackTarget - _attackFadeStart);
            if (t >= 1f)
            {
                _duckFactor = _attackTarget;
                _attacking = false;
            }
        }
        // Apply release fade
        else if (_releasing)
        {
            float elapsed = (float)(Environment.TickCount64 - _releaseStartMs);
            float t = ReleaseDurationMs > 0f ? Math.Clamp(elapsed / ReleaseDurationMs, 0f, 1f) : 1f;
            _duckFactor = _releaseFadeStart + t * (1f - _releaseFadeStart);
            if (t >= 1f)
            {
                _duckFactor = 1f;
                _releasing = false;
            }
        }

        return _duckFactor;
    }

    public void Reset()
    {
        _isDucked = false;
        _silentFrameCount = 0;
        _duckFactor = 1f;
        _attacking = false;
        _releasing = false;
    }

    public void Dispose() => Reset();
}
