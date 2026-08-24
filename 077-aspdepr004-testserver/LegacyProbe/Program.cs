using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

using var server = new TestServer(
    new WebHostBuilder()
        .Configure(app =>
        {
            app.Run(context => context.Response.WriteAsync("legacy host"));
        }));

using var client = server.CreateClient();
Console.WriteLine(await client.GetStringAsync("/probe"));
