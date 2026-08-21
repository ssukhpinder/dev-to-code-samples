using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information);
    builder.AddJsonConsole(options =>
    {
        options.IncludeScopes = false;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "O";
    });
});

var logger = loggerFactory.CreateLogger("Orders");
var orderMoved = LoggerMessage.Define<int, string>(
    LogLevel.Information,
    new EventId(1001, "OrderMoved"),
    "Order {OrderId} moved to {Status}.");

orderMoved(logger, 42, "ready", null);
