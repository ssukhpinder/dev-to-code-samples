# Keyed DI Services: Deleting the Factory Class

Minimal API demo of keyed services in Microsoft.Extensions.DependencyInjection (.NET 8+, run here on .NET 10): three `INotificationSender` implementations registered under string keys, resolved by attribute at the injection site, by runtime key from a route value, and inside a plain class constructor — replacing a hand-rolled `NotificationSenderFactory`.

Also demonstrates the two gotchas: keyed registrations are invisible to `GetServices<T>()` / `IEnumerable<T>`, and an unknown key throws `InvalidOperationException` at resolution time (without telling you which key missed).

## Run it

```bash
dotnet run -c Release
```

The app starts on `127.0.0.1:5199`, probes its own endpoints with `HttpClient`, prints the exchanges plus the container diagnostics, and exits.

📖 Article: _link added after publish_
