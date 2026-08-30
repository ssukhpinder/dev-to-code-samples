using Xunit;

namespace StrictAll.Tests;

public sealed class AccountContractTests
{
    [Fact]
    public void Returned_accounts_are_active()
    {
#if LEGACY_CONTROL || STRICT_FAILURE
        Account[] accounts = [];
#else
        Account[] accounts = [new("primary", true), new("backup", true)];
#endif

#if LEGACY_CONTROL
        Assert.All(accounts, account => Assert.True(account.IsActive, account.Id));
#else
        Assert.All(
            accounts,
            account => Assert.True(account.IsActive, account.Id),
            throwIfEmpty: true);
#endif
    }

    private sealed record Account(string Id, bool IsActive);
}
