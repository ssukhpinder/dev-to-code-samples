using System.Linq.Expressions;

namespace CSharp13Binding;

public static class ExpressionFactory
{
    public static Expression<Func<int[], int, bool>> CreateContains() =>
        (values, expected) => values.Contains(expected);
}
