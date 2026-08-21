using System.Linq.Expressions;

namespace CSharp14Binding;

public static class ExpressionFactory
{
    public static Expression<Func<int[], int, bool>> CreateContains() =>
        (values, expected) => values.Contains(expected);

    public static Expression<Func<int[], int, bool>> CreatePinnedContains() =>
        (values, expected) => Enumerable.Contains(values, expected);

    public static Expression<Func<int[], int, bool>> CreateCastContains() =>
        (values, expected) => ((IEnumerable<int>)values).Contains(expected);
}
