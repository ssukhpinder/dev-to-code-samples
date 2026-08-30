var checks = new (string Name, Action Verify)[]
{
    ("invalid inputs match the target runtime", VerifyTargetBoundary),
    ("guard preserves a non-generic type", VerifyNonGenericFallback),
    ("guard preserves a closed generic type", VerifyClosedGenericFallback),
    ("guard creates a signature from an open generic definition", VerifyOpenGenericSignature),
    ("signature preserves generic arguments", VerifySignatureArguments),
};

Console.WriteLine($"Target: {AppContext.TargetFrameworkName}");

var passed = 0;
foreach (var (name, verify) in checks)
{
    try
    {
        verify();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"Summary: {passed}/{checks.Length} passed");
return passed == checks.Length ? 0 : 1;

static void VerifyTargetBoundary()
{
#if NET10_0_OR_GREATER
    _ = ExpectThrows<ArgumentException>(
        () => Type.MakeGenericSignatureType(typeof(string), typeof(int)));
    _ = ExpectThrows<ArgumentException>(
        () => Type.MakeGenericSignatureType(typeof(List<int>), typeof(string)));
#else
    var nonGenericSignature = Type.MakeGenericSignatureType(typeof(string), typeof(int));
    var closedGenericSignature = Type.MakeGenericSignatureType(typeof(List<int>), typeof(string));

    Require(nonGenericSignature is not null, ".NET 9 rejected the non-generic legacy input.");
    Require(closedGenericSignature is not null, ".NET 9 rejected the closed-generic legacy input.");
#endif
}

static void VerifyNonGenericFallback()
{
    var original = typeof(string);
    var result = MakeSignatureOrOriginal(original, typeof(int));

    Require(ReferenceEquals(result, original), "A non-generic type was not returned unchanged.");
}

static void VerifyClosedGenericFallback()
{
    var original = typeof(List<int>);
    var result = MakeSignatureOrOriginal(original, typeof(string));

    Require(ReferenceEquals(result, original), "A closed generic type was not returned unchanged.");
}

static void VerifyOpenGenericSignature()
{
    var signature = MakeSignatureOrOriginal(
        typeof(Dictionary<,>),
        typeof(string),
        typeof(int));

    Require(signature.IsGenericType, "The result is not generic.");
    Require(
        signature.GetGenericTypeDefinition() == typeof(Dictionary<,>),
        "The signature has the wrong generic type definition.");
}

static void VerifySignatureArguments()
{
    var signature = MakeSignatureOrOriginal(
        typeof(Dictionary<,>),
        typeof(string),
        typeof(int));
    var arguments = signature.GetGenericArguments();

    Require(arguments.Length == 2, $"Expected two generic arguments, found {arguments.Length}.");
    Require(arguments[0] == typeof(string), "The first generic argument changed.");
    Require(arguments[1] == typeof(int), "The second generic argument changed.");
}

static Type MakeSignatureOrOriginal(Type originalType, params Type[] typeArguments)
{
    return originalType.IsGenericTypeDefinition
        ? Type.MakeGenericSignatureType(originalType, typeArguments)
        : originalType;
}

#if NET10_0_OR_GREATER
static TException ExpectThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
#endif

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
