namespace Demo.Greeting;

public static class PackageInfo
{
    public static string Version =>
        typeof(PackageInfo).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
