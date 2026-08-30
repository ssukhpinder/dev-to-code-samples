using System.Numerics;

var runtimeMajor = Environment.Version.Major;
if (runtimeMajor is not (9 or 10))
{
    Console.Error.WriteLine($"This comparison expects .NET 9 or .NET 10, not .NET {runtimeMajor}.");
    return 2;
}

var expectedSmallIntegerOvershift = runtimeMajor >= 10 ? 1 : 0;
var expectedSmallIntegerOvershiftPlusOne = runtimeMajor >= 10 ? 2 : 0;

var checks = new List<Check>
{
    new("byte-left-8", ShiftLeft((byte)1, 8), expectedSmallIntegerOvershift),
    new("byte-left-9", ShiftLeft((byte)1, 9), expectedSmallIntegerOvershiftPlusOne),
    new("byte-unsigned-right-8", UnsignedShiftRight((byte)128, 8), runtimeMajor >= 10 ? 128 : 0),
    new("ushort-left-16", ShiftLeft((ushort)1, 16), expectedSmallIntegerOvershift),
    new("ushort-left-17", ShiftLeft((ushort)1, 17), expectedSmallIntegerOvershiftPlusOne),
    new("int-left-32-control", ShiftLeft(1, 32), 1),
    new("explicit-mask-byte-9", ShiftLeftModulo((byte)1, 9, 8), 2),
};

var rejection = CaptureException(() => ShiftLeftReject((byte)1, 8, 8));
var failures = checks.Where(check => check.Actual != check.Expected).ToArray();

Console.WriteLine($"runtime-major={runtimeMajor}");
foreach (var check in checks)
{
    Console.WriteLine($"{check.Name}={check.Actual}");
}

Console.WriteLine($"explicit-reject-byte-8={rejection}");

if (failures.Length != 0 || rejection != nameof(ArgumentOutOfRangeException))
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(
            $"FAIL: {failure.Name} expected {failure.Expected}, got {failure.Actual}");
    }

    if (rejection != nameof(ArgumentOutOfRangeException))
    {
        Console.Error.WriteLine(
            $"FAIL: explicit rejection expected {nameof(ArgumentOutOfRangeException)}, got {rejection}");
    }

    return 1;
}

Console.WriteLine("PASS: 8/8");
return 0;

static T ShiftLeft<T>(T value, int count)
    where T : IShiftOperators<T, int, T> => value << count;

static T UnsignedShiftRight<T>(T value, int count)
    where T : IShiftOperators<T, int, T> => value >>> count;

static T ShiftLeftModulo<T>(T value, int count, int width)
    where T : IShiftOperators<T, int, T>
{
    ArgumentOutOfRangeException.ThrowIfNegative(count);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
    return value << (count % width);
}

static T ShiftLeftReject<T>(T value, int count, int width)
    where T : IShiftOperators<T, int, T>
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
    ArgumentOutOfRangeException.ThrowIfNegative(count);

    if (count >= width)
    {
        throw new ArgumentOutOfRangeException(
            nameof(count),
            count,
            $"Shift count must be smaller than the {width}-bit value width.");
    }

    return value << count;
}

static string CaptureException(Action action)
{
    try
    {
        action();
        return "none";
    }
    catch (Exception exception)
    {
        return exception.GetType().Name;
    }
}

internal sealed record Check(string Name, int Actual, int Expected)
{
    public Check(string name, byte actual, int expected) : this(name, (int)actual, expected)
    {
    }

    public Check(string name, ushort actual, int expected) : this(name, (int)actual, expected)
    {
    }
}
