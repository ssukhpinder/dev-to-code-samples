using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

var legacyLoadError = TryLoadLegacyShape();

var values = new Int8InlineArray();
var wrapped = new WholeArrayWrapper();
var sizedElements = new SizedElementArray();

for (var index = 0; index < 8; index++)
{
    var value = (index + 1) * 10;
    values[index] = value;
    wrapped.Values[index] = value;
    sizedElements[index] = new SizedElement(value);
}

var checks = new[]
{
    new Verification(
        "legacy InlineArray plus explicit Size is rejected",
        legacyLoadError is TypeLoadException),
    new Verification(
        "plain eight-int InlineArray occupies 32 bytes",
        Unsafe.SizeOf<Int8InlineArray>() == 32),
    new Verification(
        "whole-array wrapper occupies 32 bytes",
        Unsafe.SizeOf<WholeArrayWrapper>() == 32),
    new Verification(
        "sized-element InlineArray occupies 32 bytes",
        Unsafe.SizeOf<SizedElementArray>() == 32),
    new Verification(
        "whole-array wrapper preserves all values",
        HasExpectedIntValues(wrapped.Values)),
    new Verification(
        "sized-element InlineArray preserves all values",
        HasExpectedElementValues(sizedElements)),
};

var passed = 0;
foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
    passed += check.Passed ? 1 : 0;
}

Console.WriteLine($"Legacy load result: {legacyLoadError?.GetType().Name ?? "no error"}");
Console.WriteLine($"Verifier: {passed}/{checks.Length} passed");
return passed == checks.Length ? 0 : 1;

static Exception? TryLoadLegacyShape()
{
    try
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("LegacyInlineArrayFixture"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("LegacyInlineArrayFixture");
        var type = module.DefineType(
            "LegacyInt8InlineArray",
            TypeAttributes.NotPublic |
                TypeAttributes.Sealed |
                TypeAttributes.SequentialLayout |
                TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            PackingSize.Unspecified,
            typesize: 32);

        var inlineArrayConstructor = typeof(InlineArrayAttribute)
            .GetConstructor([typeof(int)])
            ?? throw new InvalidOperationException("InlineArrayAttribute constructor was not found.");

        type.SetCustomAttribute(new CustomAttributeBuilder(inlineArrayConstructor, [8]));
        type.DefineField("_element0", typeof(int), FieldAttributes.Private);
        _ = type.CreateTypeInfo();

        return null;
    }
    catch (Exception exception)
    {
        return exception;
    }
}

static bool HasExpectedIntValues(Int8InlineArray values)
{
    for (var index = 0; index < 8; index++)
    {
        if (values[index] != (index + 1) * 10)
        {
            return false;
        }
    }

    return true;
}

static bool HasExpectedElementValues(SizedElementArray values)
{
    for (var index = 0; index < 8; index++)
    {
        if (values[index].Value != (index + 1) * 10)
        {
            return false;
        }
    }

    return true;
}

[InlineArray(8)]
internal struct Int8InlineArray
{
    private int _element0;
}

[StructLayout(LayoutKind.Sequential, Size = 32)]
internal struct WholeArrayWrapper
{
    public Int8InlineArray Values;
}

[StructLayout(LayoutKind.Sequential, Size = 4)]
internal readonly struct SizedElement(int value)
{
    public int Value { get; } = value;
}

[InlineArray(8)]
internal struct SizedElementArray
{
    private SizedElement _element0;
}

internal sealed record Verification(string Name, bool Passed);
