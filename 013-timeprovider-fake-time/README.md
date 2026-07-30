# TimeProvider + FakeTimeProvider: Tests That Own the Clock

Demonstrates testing time-dependent code (exponential backoff, midnight-boundary logic) with .NET's `TimeProvider` abstraction and `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` — including the xunit deadlock you hit when a `ConfigureAwait(false)` continuation refuses to inline under xunit's `SynchronizationContext`.

What's inside:

- `TransientRetry.cs` — exponential-backoff retry helper that takes a `TimeProvider` and passes it to `Task.Delay`
- `DailyQuota.cs` — "N per day, resets at local midnight" service keyed on `GetLocalNow()`
- `RetrySleepingTests.cs` — the "before" test against the real clock: correct, and 7 real seconds per run
- `RetryFakeClockTests.cs` — same scenario with `FakeTimeProvider.Advance()`: ~36 ms, plus the load-bearing `SynchronizationContext.SetSynchronizationContext(null)` line and a comment explaining the deadlock it prevents
- `MidnightTests.cs` — reproducing a local-midnight-vs-UTC-date bug deterministically with `SetLocalTimeZone`

## Run it

```bash
dotnet test -c Release --logger "console;verbosity=detailed"
```

Watch the per-test durations: the real-clock retry test takes ~7 s (1s + 2s + 4s of genuine waiting); the identical scenario against the fake clock finishes in tens of milliseconds.

📖 Article: _link added after publish_
