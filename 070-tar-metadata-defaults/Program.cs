using System.Formats.Tar;
using System.Globalization;
using System.Text;

const string PayloadText = "deterministic tar payload\n";

var verification = new Verification();

VerifyConstructorDefaults(verification);
VerifyExplicitGnuMetadata(verification);
VerifyExplicitPaxMetadata(verification);

verification.Complete();

static void VerifyConstructorDefaults(Verification verification)
{
    var gnuEntry = new GnuTarEntry(TarEntryType.RegularFile, "default-gnu.txt");
    var paxEntry = new PaxTarEntry(TarEntryType.RegularFile, "default-pax.txt");

#if NET10_0
    verification.Expect(
        gnuEntry.AccessTime == default,
        ".NET 10 leaves a new GNU entry's atime unset");
    verification.Expect(
        gnuEntry.ChangeTime == default,
        ".NET 10 leaves a new GNU entry's ctime unset");
    verification.Expect(
        !paxEntry.ExtendedAttributes.ContainsKey("atime"),
        ".NET 10 omits atime from new PAX attributes");
    verification.Expect(
        !paxEntry.ExtendedAttributes.ContainsKey("ctime"),
        ".NET 10 omits ctime from new PAX attributes");
#else
    verification.Expect(
        gnuEntry.AccessTime != default,
        ".NET 9 initializes a new GNU entry's atime");
    verification.Expect(
        gnuEntry.ChangeTime != default,
        ".NET 9 initializes a new GNU entry's ctime");
    verification.Expect(
        paxEntry.ExtendedAttributes.ContainsKey("atime"),
        ".NET 9 includes atime in new PAX attributes");
    verification.Expect(
        paxEntry.ExtendedAttributes.ContainsKey("ctime"),
        ".NET 9 includes ctime in new PAX attributes");
#endif
}

static void VerifyExplicitGnuMetadata(Verification verification)
{
    DateTimeOffset accessTime = DateTimeOffset.FromUnixTimeSeconds(1_704_067_200);
    DateTimeOffset changeTime = DateTimeOffset.FromUnixTimeSeconds(1_704_067_260);
    DateTimeOffset modificationTime = DateTimeOffset.FromUnixTimeSeconds(1_704_067_320);
    byte[] payload = Encoding.UTF8.GetBytes(PayloadText);

    using var payloadStream = new MemoryStream(payload, writable: false);
    var entry = new GnuTarEntry(TarEntryType.RegularFile, "explicit-gnu.txt")
    {
        AccessTime = accessTime,
        ChangeTime = changeTime,
        ModificationTime = modificationTime,
        DataStream = payloadStream,
        Gid = 1000,
        GroupName = "sample",
        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        Uid = 1000,
        UserName = "sample",
    };

    using var archive = new MemoryStream();
    using (var writer = new TarWriter(archive, TarEntryFormat.Gnu, leaveOpen: true))
    {
        writer.WriteEntry(entry);
    }

    archive.Position = 0;

    using var reader = new TarReader(archive, leaveOpen: true);
    var restored = reader.GetNextEntry() as GnuTarEntry;

    verification.Expect(restored is not null, "GNU entry round-trips as GNU");
    verification.Expect(restored?.AccessTime == accessTime, "explicit GNU atime survives round-trip");
    verification.Expect(restored?.ChangeTime == changeTime, "explicit GNU ctime survives round-trip");
    verification.Expect(
        restored?.ModificationTime == modificationTime,
        "explicit GNU mtime survives round-trip");
    verification.Expect(ReadPayload(restored) == PayloadText, "GNU payload survives round-trip");
}

static void VerifyExplicitPaxMetadata(Verification verification)
{
    const long AccessSeconds = 1_704_067_200;
    const long ChangeSeconds = 1_704_067_260;
    DateTimeOffset modificationTime = DateTimeOffset.FromUnixTimeSeconds(1_704_067_320);
    byte[] payload = Encoding.UTF8.GetBytes(PayloadText);
    KeyValuePair<string, string>[] attributes =
    [
        new("atime", AccessSeconds.ToString(CultureInfo.InvariantCulture)),
        new("ctime", ChangeSeconds.ToString(CultureInfo.InvariantCulture)),
    ];

    using var payloadStream = new MemoryStream(payload, writable: false);
    var entry = new PaxTarEntry(TarEntryType.RegularFile, "explicit-pax.txt", attributes)
    {
        ModificationTime = modificationTime,
        DataStream = payloadStream,
        Gid = 1000,
        GroupName = "sample",
        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        Uid = 1000,
        UserName = "sample",
    };

    using var archive = new MemoryStream();
    using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
    {
        writer.WriteEntry(entry);
    }

    archive.Position = 0;

    using var reader = new TarReader(archive, leaveOpen: true);
    var restored = reader.GetNextEntry() as PaxTarEntry;

    verification.Expect(restored is not null, "PAX entry round-trips as PAX");
    verification.Expect(
        HasUnixSeconds(restored, "atime", AccessSeconds),
        "explicit PAX atime survives round-trip");
    verification.Expect(
        HasUnixSeconds(restored, "ctime", ChangeSeconds),
        "explicit PAX ctime survives round-trip");
    verification.Expect(
        restored?.ModificationTime == modificationTime,
        "explicit PAX mtime survives round-trip");
    verification.Expect(ReadPayload(restored) == PayloadText, "PAX payload survives round-trip");
}

static bool HasUnixSeconds(PaxTarEntry? entry, string key, long expected)
{
    return entry is not null
        && entry.ExtendedAttributes.TryGetValue(key, out string? value)
        && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
        && parsed == expected;
}

static string? ReadPayload(TarEntry? entry)
{
    if (entry?.DataStream is null)
    {
        return null;
    }

    using var textReader = new StreamReader(
        entry.DataStream,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: false,
        bufferSize: 1024,
        leaveOpen: true);
    return textReader.ReadToEnd();
}

internal sealed class Verification
{
    private int passed;
    private int total;

    public void Expect(bool condition, string description)
    {
        total++;
        if (!condition)
        {
            Console.Error.WriteLine($"FAIL {total:00}: {description}");
            return;
        }

        passed++;
        Console.WriteLine($"PASS {total:00}: {description}");
    }

    public void Complete()
    {
        Console.WriteLine($"SUMMARY {passed}/{total}");
        if (passed != total)
        {
            throw new InvalidOperationException($"Verification failed: {passed}/{total} checks passed.");
        }
    }
}
