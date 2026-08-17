namespace UnsafeRaceDemo;

public sealed class SharedCounterRaceTests
{
    private static readonly Barrier Gate = new(participantCount: 2);
    private static int sharedCounter;

    [Theory]
    [InlineData("first")]
    [InlineData("second")]
    public void Non_atomic_update_loses_one_increment(string worker)
    {
        Assert.NotEmpty(worker);

        var snapshot = Volatile.Read(ref sharedCounter);
        Assert.True(Gate.SignalAndWait(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken));

        Volatile.Write(ref sharedCounter, snapshot + 1);
        Assert.True(Gate.SignalAndWait(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken));

        Assert.Equal(2, Volatile.Read(ref sharedCounter));
    }
}
