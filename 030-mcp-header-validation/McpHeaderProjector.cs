using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace McpHeaderValidation;

internal sealed record HeaderBinding(string Suffix, string ValueType, string[] InstancePath);

internal static class McpHeaderProjector
{
    private const long MaxJavaScriptSafeInteger = 9_007_199_254_740_991;

    public static IReadOnlyDictionary<string, string> Project(
        JsonElement inputSchema,
        JsonElement arguments)
    {
        List<string> errors = [];
        List<HeaderBinding> bindings = [];
        HashSet<string> suffixes = new(StringComparer.OrdinalIgnoreCase);

        ScanSchema(
            inputSchema,
            schemaPath: "$",
            instancePath: [],
            isPropertyNode: false,
            reachableByProperties: true,
            suffixes,
            bindings,
            errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid inputSchema:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        SortedDictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        foreach (HeaderBinding binding in bindings)
        {
            if (!TryResolve(arguments, binding.InstancePath, out JsonElement value) ||
                value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            string text = ConvertValue(binding, value);
            headers[$"Mcp-Param-{binding.Suffix}"] = EncodeHeaderValue(text);
        }

        return headers;
    }

    public static string EncodeHeaderValue(string value)
    {
        bool hasUnsafeCharacter = value.Any(
            character => character != '\t' && (character < 0x20 || character > 0x7e));
        bool hasEdgeWhitespace = value.Length > 0 &&
            (value[0] is ' ' or '\t' || value[^1] is ' ' or '\t');
        bool looksEncoded = value.StartsWith("=?base64?", StringComparison.Ordinal) &&
            value.EndsWith("?=", StringComparison.Ordinal);

        if (!hasUnsafeCharacter && !hasEdgeWhitespace && !looksEncoded)
        {
            return value;
        }

        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return $"=?base64?{base64}?=";
    }

    private static void ScanSchema(
        JsonElement node,
        string schemaPath,
        IReadOnlyList<string> instancePath,
        bool isPropertyNode,
        bool reachableByProperties,
        HashSet<string> suffixes,
        List<HeaderBinding> bindings,
        List<string> errors)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (node.TryGetProperty("x-mcp-header", out JsonElement annotation))
        {
            ValidateAnnotation(
                node,
                annotation,
                schemaPath,
                instancePath,
                isPropertyNode,
                reachableByProperties,
                suffixes,
                bindings,
                errors);
        }

        if (node.TryGetProperty("properties", out JsonElement properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                string[] childPath = [.. instancePath, property.Name];
                ScanSchema(
                    property.Value,
                    $"{schemaPath}.properties.{property.Name}",
                    childPath,
                    isPropertyNode: true,
                    reachableByProperties,
                    suffixes,
                    bindings,
                    errors);
            }
        }

        foreach (string keyword in SingleSubschemaKeywords)
        {
            if (node.TryGetProperty(keyword, out JsonElement subschema))
            {
                ScanSubschema(
                    subschema,
                    $"{schemaPath}.{keyword}",
                    instancePath,
                    suffixes,
                    bindings,
                    errors);
            }
        }

        foreach (string keyword in SubschemaArrayKeywords)
        {
            if (node.TryGetProperty(keyword, out JsonElement subschemas))
            {
                ScanSubschemaArray(
                    subschemas,
                    $"{schemaPath}.{keyword}",
                    instancePath,
                    suffixes,
                    bindings,
                    errors);
            }
        }

        foreach (string keyword in SubschemaMapKeywords)
        {
            if (node.TryGetProperty(keyword, out JsonElement subschemas) &&
                subschemas.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in subschemas.EnumerateObject())
                {
                    if (keyword == "dependencies" && entry.Value.ValueKind == JsonValueKind.Array)
                    {
                        continue;
                    }

                    ScanSubschema(
                        entry.Value,
                        $"{schemaPath}.{keyword}.{entry.Name}",
                        instancePath,
                        suffixes,
                        bindings,
                        errors);
                }
            }
        }
    }

    private static readonly string[] SingleSubschemaKeywords =
    [
        "additionalItems",
        "additionalProperties",
        "contains",
        "contentSchema",
        "else",
        "if",
        "items",
        "not",
        "propertyNames",
        "then",
        "unevaluatedItems",
        "unevaluatedProperties"
    ];

    private static readonly string[] SubschemaArrayKeywords =
    [
        "allOf",
        "anyOf",
        "oneOf",
        "prefixItems"
    ];

    private static readonly string[] SubschemaMapKeywords =
    [
        "$defs",
        "definitions",
        "dependencies",
        "dependentSchemas",
        "patternProperties"
    ];

    private static void ScanSubschema(
        JsonElement node,
        string schemaPath,
        IReadOnlyList<string> instancePath,
        HashSet<string> suffixes,
        List<HeaderBinding> bindings,
        List<string> errors)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            ScanSchema(
                node,
                schemaPath,
                instancePath,
                isPropertyNode: false,
                reachableByProperties: false,
                suffixes,
                bindings,
                errors);
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            ScanSubschemaArray(node, schemaPath, instancePath, suffixes, bindings, errors);
        }
    }

    private static void ScanSubschemaArray(
        JsonElement nodes,
        string schemaPath,
        IReadOnlyList<string> instancePath,
        HashSet<string> suffixes,
        List<HeaderBinding> bindings,
        List<string> errors)
    {
        if (nodes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int index = 0;
        foreach (JsonElement item in nodes.EnumerateArray())
        {
            ScanSubschema(item, $"{schemaPath}[{index}]", instancePath, suffixes, bindings, errors);
            index++;
        }
    }

    private static void ValidateAnnotation(
        JsonElement node,
        JsonElement annotation,
        string schemaPath,
        IReadOnlyList<string> instancePath,
        bool isPropertyNode,
        bool reachableByProperties,
        HashSet<string> suffixes,
        List<HeaderBinding> bindings,
        List<string> errors)
    {
        int errorCount = errors.Count;

        if (!isPropertyNode || !reachableByProperties)
        {
            errors.Add($"- {schemaPath}: x-mcp-header is not statically reachable through properties.");
        }

        string? suffix = annotation.ValueKind == JsonValueKind.String
            ? annotation.GetString()
            : null;

        if (suffix is null || !IsHttpToken(suffix))
        {
            errors.Add($"- {schemaPath}: x-mcp-header must be a non-empty HTTP token.");
        }

        string? valueType = node.TryGetProperty("type", out JsonElement typeNode) &&
            typeNode.ValueKind == JsonValueKind.String
                ? typeNode.GetString()
                : null;

        if (valueType is not ("string" or "integer" or "boolean"))
        {
            errors.Add($"- {schemaPath}: annotated type must be string, integer, or boolean.");
        }

        if (suffix is not null && IsHttpToken(suffix) && !suffixes.Add(suffix))
        {
            errors.Add($"- {schemaPath}: x-mcp-header '{suffix}' is not case-insensitively unique.");
        }

        if (errors.Count == errorCount && suffix is not null && valueType is not null)
        {
            bindings.Add(new HeaderBinding(suffix, valueType, [.. instancePath]));
        }
    }

    private static bool IsHttpToken(string value) =>
        value.Length > 0 && value.All(IsTokenCharacter);

    private static bool IsTokenCharacter(char character) =>
        character is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or
            '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or
            '^' or '_' or '`' or '|' or '~';

    private static bool TryResolve(
        JsonElement arguments,
        IReadOnlyList<string> instancePath,
        out JsonElement value)
    {
        value = arguments;

        foreach (string segment in instancePath)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static string ConvertValue(HeaderBinding binding, JsonElement value)
    {
        if (binding.ValueType == "string" && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!;
        }

        if (binding.ValueType == "integer" &&
            value.ValueKind == JsonValueKind.Number &&
            TryNormalizeInteger(value.GetRawText(), out string? integer))
        {
            return integer;
        }

        if (binding.ValueType == "boolean" &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean() ? "true" : "false";
        }

        throw new InvalidOperationException(
            $"Argument '{string.Join('.', binding.InstancePath)}' is not a valid {binding.ValueType} value.");
    }

    private static bool TryNormalizeInteger(string rawNumber, out string normalized)
    {
        normalized = string.Empty;

        int exponentMarker = rawNumber.IndexOfAny(['e', 'E']);
        ReadOnlySpan<char> significand = exponentMarker >= 0
            ? rawNumber.AsSpan(0, exponentMarker)
            : rawNumber.AsSpan();
        ReadOnlySpan<char> exponentText = exponentMarker >= 0
            ? rawNumber.AsSpan(exponentMarker + 1)
            : "0";

        if (!BigInteger.TryParse(
                exponentText,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out BigInteger exponent))
        {
            return false;
        }

        bool negative = significand.StartsWith('-');
        if (negative)
        {
            significand = significand[1..];
        }

        int decimalPoint = significand.IndexOf('.');
        int fractionalDigits = decimalPoint >= 0
            ? significand.Length - decimalPoint - 1
            : 0;
        string digits = decimalPoint >= 0
            ? string.Concat(significand[..decimalPoint], significand[(decimalPoint + 1)..])
            : significand.ToString();
        digits = digits.TrimStart('0');

        if (digits.Length == 0)
        {
            normalized = "0";
            return true;
        }

        BigInteger scale = exponent - fractionalDigits;

        if (scale.Sign < 0)
        {
            BigInteger requiredZerosValue = BigInteger.Negate(scale);
            if (requiredZerosValue > digits.Length)
            {
                return false;
            }

            int requiredZeros = (int)requiredZerosValue;
            if (digits.AsSpan(digits.Length - requiredZeros).ContainsAnyExcept('0'))
            {
                return false;
            }

            digits = digits[..(digits.Length - requiredZeros)];
        }
        else
        {
            int remainingSafeDigits = 16 - digits.Length;
            if (remainingSafeDigits < 0 || scale > remainingSafeDigits)
            {
                return false;
            }

            digits += new string('0', (int)scale);
        }

        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger integer))
        {
            return false;
        }

        if (negative)
        {
            integer = BigInteger.Negate(integer);
        }

        BigInteger max = MaxJavaScriptSafeInteger;
        if (integer < -max || integer > max)
        {
            return false;
        }

        normalized = integer.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
