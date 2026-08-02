using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// ---- BEFORE: the factory era ------------------------------------------------
// Every concrete type registered so the factory can pull it back out,
// plus the factory itself. Four registrations of pure ceremony.
builder.Services.AddScoped<EmailSender>();
builder.Services.AddScoped<SmsSender>();
builder.Services.AddScoped<PushSender>();
builder.Services.AddScoped<NotificationSenderFactory>();

// ---- AFTER: keyed services --------------------------------------------------
builder.Services.AddKeyedScoped<INotificationSender, EmailSender>(Channels.Email);
builder.Services.AddKeyedScoped<INotificationSender, SmsSender>(Channels.Sms);
builder.Services.AddKeyedScoped<INotificationSender, PushSender>(Channels.Push);

// A normal class that depends on one specific keyed implementation.
builder.Services.AddScoped<DailyDigestService>();

var app = builder.Build();
app.Urls.Add("http://127.0.0.1:5199");

// Old way: route through the hand-rolled factory.
app.MapPost("/old/notify/{channel}", (string channel, NotificationSenderFactory factory) =>
    Results.Ok(new { channel, result = factory.Create(channel).Send("build is green") }));

// New way, fixed key: the endpoint declares which implementation it wants.
app.MapPost("/notify/email", ([FromKeyedServices(Channels.Email)] INotificationSender sender) =>
    Results.Ok(new { channel = Channels.Email, result = sender.Send("build is green") }));

// New way, runtime key: the route decides.
app.MapPost("/notify/{channel}", (string channel, IServiceProvider sp) =>
    Results.Ok(new { channel, result = sp.GetRequiredKeyedService<INotificationSender>(channel).Send("build is green") }));

// Keyed injection works in plain constructors too, not just endpoints.
app.MapPost("/digest", (DailyDigestService digest) => Results.Ok(new { result = digest.SendDigest() }));

await app.StartAsync();

// ---- Self-probe: this is the output quoted in the article -------------------
using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5199") };

async Task Probe(string path)
{
    var resp = await http.PostAsync(path, null);
    Console.WriteLine($"POST {path,-18} -> {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
}

Console.WriteLine("== the factory way ==");
await Probe("/old/notify/sms");

Console.WriteLine();
Console.WriteLine("== keyed services ==");
await Probe("/notify/email");
await Probe("/notify/push");
await Probe("/digest");

Console.WriteLine();
Console.WriteLine("== what the container actually sees ==");
await using (var scope = app.Services.CreateAsyncScope())
{
    var sp = scope.ServiceProvider;

    var unkeyed = sp.GetServices<INotificationSender>().ToList();
    Console.WriteLine($"GetServices<INotificationSender>()          -> {unkeyed.Count} implementations");

    var keyed = sp.GetKeyedServices<INotificationSender>(KeyedService.AnyKey).ToList();
    Console.WriteLine($"GetKeyedServices(KeyedService.AnyKey)       -> {string.Join(", ", keyed.Select(s => s.GetType().Name))}");

    try
    {
        sp.GetRequiredKeyedService<INotificationSender>("fax");
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"GetRequiredKeyedService(\"fax\")              -> {ex.GetType().Name}: {ex.Message}");
    }
}

await app.StopAsync();

// ---- Types ------------------------------------------------------------------

public static class Channels
{
    public const string Email = "email";
    public const string Sms = "sms";
    public const string Push = "push";
}

public interface INotificationSender
{
    string Send(string message);
}

public sealed class EmailSender(ILogger<EmailSender> logger) : INotificationSender
{
    public string Send(string message)
    {
        logger.LogInformation("smtp handshake, pretend it happened");
        return $"email queued: \"{message}\"";
    }
}

public sealed class SmsSender(ILogger<SmsSender> logger) : INotificationSender
{
    public string Send(string message)
    {
        logger.LogInformation("sms gateway, pretend it happened");
        return $"sms queued: \"{message}\"";
    }
}

public sealed class PushSender(ILogger<PushSender> logger) : INotificationSender
{
    public string Send(string message)
    {
        logger.LogInformation("push relay, pretend it happened");
        return $"push queued: \"{message}\"";
    }
}

// The class this whole post is about deleting.
public sealed class NotificationSenderFactory(IServiceProvider sp)
{
    public INotificationSender Create(string channel) => channel switch
    {
        Channels.Email => sp.GetRequiredService<EmailSender>(),
        Channels.Sms => sp.GetRequiredService<SmsSender>(),
        Channels.Push => sp.GetRequiredService<PushSender>(),
        _ => throw new ArgumentException($"Unknown channel '{channel}'.", nameof(channel)),
    };
}

// Depends on one specific keyed implementation — no factory, no service locator.
public sealed class DailyDigestService([FromKeyedServices(Channels.Email)] INotificationSender email)
{
    public string SendDigest() => email.Send("your daily digest");
}
