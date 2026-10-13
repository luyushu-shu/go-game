using System;

[Serializable]
public struct GoClockSnapshot
{
    public int Color;
    public double MainSeconds;
    public int ByoyomiPeriodsLeft;
    public double ByoyomiSeconds;
    public bool InByoyomi;
    public int ByoyomiSegment;
    public bool TimedOut;
}

public sealed class GoClockController
{
    public event Action<GoClockSnapshot, GoClockSnapshot> Changed;
    public event Action<int> TimedOut;
    public int ActiveColor { get; private set; }
    public bool IsRunning { get; private set; }
    public int ByoyomiPeriodsTotal => control.ByoyomiPeriods;
    public int ByoyomiSecondsTotal => control.ByoyomiSeconds;

    readonly ClockState black = new ClockState { Color = GoRules.BLACK };
    readonly ClockState white = new ClockState { Color = GoRules.WHITE };
    GoTimeControl control;
    bool started;
    bool timeoutFired;

    sealed class ClockState
    {
        public int Color;
        public double MainSeconds;
        public int ByoyomiPeriodsLeft;
        public double ByoyomiSeconds;
        public bool InByoyomi;
        public int ByoyomiSegment;
        public bool TimedOut;
    }

    public void Start(GoTimeControl timeControl, int firstColor)
    {
        ValidateColor(firstColor);
        control = timeControl;
        timeoutFired = false;
        started = true;
        IsRunning = true;
        ActiveColor = firstColor;
        ResetState(black);
        ResetState(white);

        Changed?.Invoke(default(GoClockSnapshot), GetSnapshot(firstColor));
    }

    public void SetTurn(int color)
    {
        ValidateStarted();
        ValidateColor(color);
        var before = GetSnapshot(color);
        ActiveColor = color;
        var state = StateFor(color);
        if (!state.TimedOut && state.MainSeconds <= 0 && state.ByoyomiPeriodsLeft > 0)
        {
            state.InByoyomi = true;
            state.ByoyomiSeconds = control.ByoyomiSeconds;
            state.ByoyomiSegment++;
        }
        var after = GetSnapshot(color);
        if (!SnapshotsEqual(before, after)) Changed?.Invoke(before, after);
    }

    public void Tick(double deltaSeconds)
    {
        if (!started || !IsRunning || timeoutFired || deltaSeconds <= 0 || double.IsNaN(deltaSeconds)) return;
        if (double.IsInfinity(deltaSeconds)) deltaSeconds = double.MaxValue;

        var state = StateFor(ActiveColor);
        var before = GetSnapshot(ActiveColor);
        Consume(state, deltaSeconds);
        var after = GetSnapshot(ActiveColor);
        if (!SnapshotsEqual(before, after)) Changed?.Invoke(before, after);
    }

    public void Pause()
    {
        if (!started) return;
        IsRunning = false;
    }

    public void Resume()
    {
        if (!started || timeoutFired) return;
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public GoClockSnapshot GetSnapshot(int color)
    {
        ValidateColor(color);
        var state = StateFor(color);
        return new GoClockSnapshot
        {
            Color = state.Color,
            MainSeconds = state.MainSeconds,
            ByoyomiPeriodsLeft = state.ByoyomiPeriodsLeft,
            ByoyomiSeconds = state.ByoyomiSeconds,
            InByoyomi = state.InByoyomi,
            ByoyomiSegment = state.ByoyomiSegment,
            TimedOut = state.TimedOut
        };
    }

    void ResetState(ClockState state)
    {
        state.MainSeconds = control.IsUnlimited ? double.PositiveInfinity : control.MainTimeSeconds;
        state.ByoyomiPeriodsLeft = control.ByoyomiPeriods;
        state.ByoyomiSeconds = 0;
        state.InByoyomi = false;
        state.ByoyomiSegment = 0;
        state.TimedOut = false;
    }

    void Consume(ClockState state, double remaining)
    {
        if (state.TimedOut || control.IsUnlimited) return;

        if (state.MainSeconds > 0)
        {
            double consumed = Math.Min(state.MainSeconds, remaining);
            state.MainSeconds -= consumed;
            remaining -= consumed;
            if (state.MainSeconds < 0.0000001) state.MainSeconds = 0;
            if (state.MainSeconds <= 0)
            {
                if (state.ByoyomiPeriodsLeft <= 0)
                {
                    Timeout(state);
                    return;
                }

                state.InByoyomi = true;
                state.ByoyomiSegment++;
                if (state.ByoyomiSeconds <= 0) state.ByoyomiSeconds = control.ByoyomiSeconds;
            }
            if (remaining <= 0) return;
        }

        if (state.ByoyomiPeriodsLeft <= 0)
        {
            Timeout(state);
            return;
        }

        state.InByoyomi = true;
        if (state.ByoyomiSeconds <= 0) state.ByoyomiSeconds = control.ByoyomiSeconds;

        while (remaining > 0 && !state.TimedOut)
        {
            double consumed = Math.Min(state.ByoyomiSeconds, remaining);
            state.ByoyomiSeconds -= consumed;
            remaining -= consumed;
            if (state.ByoyomiSeconds > 0) break;

            state.ByoyomiPeriodsLeft--;
            state.ByoyomiSeconds = 0;
            if (state.ByoyomiPeriodsLeft <= 0)
            {
                Timeout(state);
                break;
            }

            state.ByoyomiSeconds = control.ByoyomiSeconds;
        }
    }

    void Timeout(ClockState state)
    {
        if (timeoutFired) return;
        timeoutFired = true;
        state.TimedOut = true;
        state.InByoyomi = true;
        state.ByoyomiSeconds = 0;
        IsRunning = false;
        TimedOut?.Invoke(state.Color);
    }

    ClockState StateFor(int color)
    {
        return color == GoRules.BLACK ? black : white;
    }

    void ValidateStarted()
    {
        if (!started) throw new InvalidOperationException("The clock has not been started.");
    }

    static void ValidateColor(int color)
    {
        if (color != GoRules.BLACK && color != GoRules.WHITE)
            throw new ArgumentOutOfRangeException(nameof(color), color, "Color must be black or white.");
    }

    static bool SnapshotsEqual(GoClockSnapshot a, GoClockSnapshot b)
    {
        return a.Color == b.Color && a.MainSeconds.Equals(b.MainSeconds) &&
            a.ByoyomiPeriodsLeft == b.ByoyomiPeriodsLeft &&
            a.ByoyomiSeconds.Equals(b.ByoyomiSeconds) &&
            a.InByoyomi == b.InByoyomi && a.ByoyomiSegment == b.ByoyomiSegment
            && a.TimedOut == b.TimedOut;
    }
}
