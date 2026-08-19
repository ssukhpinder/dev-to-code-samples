namespace MSTestSpecs;

[TestClass]
public sealed class CategoryRoutingTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_filter_reaches_mstest()
    {
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Unit_test_is_not_selected_by_the_integration_filter()
    {
    }
}
