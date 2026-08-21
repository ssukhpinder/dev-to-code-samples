using System.Linq.Expressions;

var checks = 0;
var values = new[] { 3, 5, 8 };

var csharp13 = CSharp13Binding.ExpressionFactory.CreateContains();
var csharp14 = CSharp14Binding.ExpressionFactory.CreateContains();
var pinned = CSharp14Binding.ExpressionFactory.CreatePinnedContains();
var cast = CSharp14Binding.ExpressionFactory.CreateCastContains();

Check(DeclaringType(csharp13) == typeof(Enumerable), "C# 13 binds Enumerable.Contains");
Check(DeclaringType(csharp14) == typeof(MemoryExtensions), "C# 14 binds MemoryExtensions.Contains");
Check(csharp13.Compile(preferInterpretation: true)(values, 5), "C# 13 expression runs with interpretation");
Check(csharp14.Compile()(values, 5), "C# 14 expression runs with IL compilation");

Exception? interpretationFailure = null;
try
{
    _ = csharp14.Compile(preferInterpretation: true)(values, 5);
}
catch (Exception exception)
{
    interpretationFailure = exception;
}

Check(interpretationFailure is ArgumentException, "C# 14 span expression rejects interpretation");
Check(DeclaringType(pinned) == typeof(Enumerable), "explicit static call pins Enumerable.Contains");
Check(pinned.Compile(preferInterpretation: true)(values, 5), "pinned expression runs with interpretation");
Check(DeclaringType(cast) == typeof(Enumerable), "IEnumerable cast pins Enumerable.Contains");
Check(cast.Compile(preferInterpretation: true)(values, 5), "cast expression runs with interpretation");
Check(!pinned.Compile(preferInterpretation: true)(values, 13), "pinned expression preserves false results");

Console.WriteLine($"PASS: {checks}/10 checks");

static Type? DeclaringType(LambdaExpression expression) =>
    expression.Body is MethodCallExpression call
        ? call.Method.DeclaringType
        : throw new InvalidOperationException("Expected a method-call expression body.");

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {description}");
    }

    checks++;
    Console.WriteLine($"PASS: {description}");
}
