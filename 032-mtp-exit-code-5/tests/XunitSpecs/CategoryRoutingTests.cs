using Xunit;

namespace XunitSpecs;

public sealed class CategoryRoutingTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Integration_filter_reaches_xunit()
    {
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Unit_test_is_not_selected_by_the_integration_filter()
    {
    }
}
