namespace SafeParallelTests;

public sealed class ParallelSafetyTests
{
    private static readonly Barrier Gate = new(participantCount: 2);
#if PARALLEL_OPT_OUT_CONTROL
    private static readonly Barrier OptOutControlGate = new(participantCount: 2);
#endif
    private static int activeParallelTests;
    private static int exclusiveLease;
    private static int safeCounter;

    [Theory]
    [InlineData("first")]
    [InlineData("second")]
    public void Same_class_cases_overlap_safely(string worker)
    {
        Assert.NotEmpty(worker);
        Interlocked.Increment(ref activeParallelTests);

        try
        {
            Assert.True(Gate.SignalAndWait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
            Interlocked.Increment(ref safeCounter);
            Assert.True(Gate.SignalAndWait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));

            Assert.Equal(2, Volatile.Read(ref safeCounter));
        }
        finally
        {
            Interlocked.Decrement(ref activeParallelTests);
        }
    }

#if PARALLEL_OPT_OUT_CONTROL
    [Theory]
#else
    [Theory(DisableParallelization = true)]
#endif
    [InlineData("first")]
    [InlineData("second")]
    public void Shared_state_cases_opt_out_of_parallelism(string worker)
    {
        Assert.NotEmpty(worker);
        Assert.Equal(0, Volatile.Read(ref activeParallelTests));

#if PARALLEL_OPT_OUT_CONTROL
        Assert.True(OptOutControlGate.SignalAndWait(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken));
#endif

        var previousLease = Interlocked.CompareExchange(ref exclusiveLease, 1, 0);

#if PARALLEL_OPT_OUT_CONTROL
        Assert.True(OptOutControlGate.SignalAndWait(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken));
#endif

        try
        {
            Assert.Equal(0, previousLease);
            Assert.Equal(0, Volatile.Read(ref activeParallelTests));
        }
        finally
        {
            if (previousLease == 0)
            {
                Volatile.Write(ref exclusiveLease, 0);
            }
        }
    }
}
