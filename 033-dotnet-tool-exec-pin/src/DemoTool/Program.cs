using System.Globalization;

var label = "none";
var exitCode = 0;

for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--label" when index + 1 < args.Length:
            label = args[++index];
            break;

        case "--exit-code" when index + 1 < args.Length:
            if (!int.TryParse(
                    args[++index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out exitCode)
                || exitCode is < 0 or > 255)
            {
                Console.Error.WriteLine("--exit-code must be an integer from 0 through 255.");
                return 64;
            }

            break;

        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[index]}");
            return 64;
    }
}

var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
Console.WriteLine($"demo-tool version={version} label={label}");
return exitCode;
