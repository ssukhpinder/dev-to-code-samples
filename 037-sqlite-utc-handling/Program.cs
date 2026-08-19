using System.Globalization;
using Microsoft.Data.Sqlite;

using SqliteConnection connection = new("Data Source=:memory:");
connection.Open();

CreateSchemaAndSeed(connection);

DateTimeOffset offsetlessText = ReadDateTimeOffset(connection, id: 1);
Equal(new DateTime(2014, 4, 15, 10, 47, 16), offsetlessText.DateTime);
Equal(TimeSpan.Zero, offsetlessText.Offset);
Pass("offsetless TEXT was interpreted as UTC");

DateTime offsetBearingText = ReadDateTime(connection, id: 2);
Equal(new DateTime(2014, 4, 15, 8, 47, 16, DateTimeKind.Utc), offsetBearingText);
Equal(DateTimeKind.Utc, offsetBearingText.Kind);
Pass("offset-bearing TEXT was converted to a UTC DateTime");

DateTimeOffset source = new(2014, 4, 15, 10, 47, 16, TimeSpan.FromHours(2));
WriteRealTimestamp(connection, source);
DateTimeOffset realValue = ReadRealTimestamp(connection);
Equal(source.UtcDateTime, realValue.UtcDateTime);
Equal(TimeSpan.Zero, realValue.Offset);
Pass("DateTimeOffset written to REAL was normalized to UTC");

long[] ambiguousIds = FindOffsetlessTextTimestampIds(connection);
SequenceEqual([1L, 4L], ambiguousIds);
Pass("audit found only parseable offsetless TEXT timestamps");

Console.WriteLine("4/4 checks passed");

static void CreateSchemaAndSeed(SqliteConnection connection)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText =
        """
        CREATE TABLE timestamps (
            id INTEGER PRIMARY KEY,
            occurred_at TEXT NOT NULL
        );

        INSERT INTO timestamps (id, occurred_at) VALUES
            (1, '2014-04-15 10:47:16'),
            (2, '2014-04-15 10:47:16+02:00'),
            (3, '2014-04-15T08:47:16Z'),
            (4, '2026-08-19T12:00:00.1234567'),
            (5, 'not-a-timestamp');

        CREATE TABLE real_timestamps (
            occurred_at REAL NOT NULL
        );
        """;
    command.ExecuteNonQuery();
}

static DateTimeOffset ReadDateTimeOffset(SqliteConnection connection, long id)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "SELECT occurred_at FROM timestamps WHERE id = $id";
    command.Parameters.AddWithValue("$id", id);

    using SqliteDataReader reader = command.ExecuteReader();
    return reader.Read()
        ? reader.GetDateTimeOffset(0)
        : throw new InvalidOperationException($"Timestamp {id} was not found.");
}

static DateTime ReadDateTime(SqliteConnection connection, long id)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "SELECT occurred_at FROM timestamps WHERE id = $id";
    command.Parameters.AddWithValue("$id", id);

    using SqliteDataReader reader = command.ExecuteReader();
    return reader.Read()
        ? reader.GetDateTime(0)
        : throw new InvalidOperationException($"Timestamp {id} was not found.");
}

static void WriteRealTimestamp(SqliteConnection connection, DateTimeOffset value)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "INSERT INTO real_timestamps (occurred_at) VALUES ($value)";
    command.Parameters.Add(new SqliteParameter("$value", SqliteType.Real) { Value = value });
    command.ExecuteNonQuery();
}

static DateTimeOffset ReadRealTimestamp(SqliteConnection connection)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "SELECT occurred_at FROM real_timestamps";

    using SqliteDataReader reader = command.ExecuteReader();
    return reader.Read()
        ? reader.GetDateTimeOffset(0)
        : throw new InvalidOperationException("The REAL timestamp was not found.");
}

static long[] FindOffsetlessTextTimestampIds(SqliteConnection connection)
{
    string[] acceptedFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"
    ];

    using SqliteCommand command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT id, occurred_at
        FROM timestamps
        WHERE typeof(occurred_at) = 'text'
        ORDER BY id
        """;

    using SqliteDataReader reader = command.ExecuteReader();
    List<long> ids = [];

    while (reader.Read())
    {
        string storedValue = reader.GetString(1);
        if (DateTime.TryParseExact(
                storedValue,
                acceptedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            ids.Add(reader.GetInt64(0));
        }
    }

    return [.. ids];
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException(
            $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }
}

static void Pass(string message) => Console.WriteLine($"PASS: {message}");
