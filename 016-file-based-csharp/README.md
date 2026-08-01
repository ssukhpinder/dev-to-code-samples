# 016 — File-based C# scripts: `dotnet run biggest.cs`

**My Shell Scripts Speak C# Now**

Demonstrates .NET 10 file-based programs: a complete, runnable C# utility in a single `.cs` file with no project.

- `#:package Humanizer@3.0.10` — NuGet reference as a source directive
- `#!/usr/bin/env dotnet` shebang, so `chmod +x biggest.cs && ./biggest.cs` works like any shell script
- Build artifacts land in `~/.local/share/dotnet/runfile/`, keeping the folder clean (no `bin/`, no `obj/`)
- Graduation path: `dotnet project convert biggest.cs` generates a real project and maps the directives into a `.csproj`

The script itself lists the largest files under a directory, with humanized sizes and ages.

## Run it

```bash
dotnet run biggest.cs -- /path/to/scan 10
# or
chmod +x biggest.cs && ./biggest.cs /path/to/scan 10
```

Requires the .NET 10 SDK.

📖 Article: [My Shell Scripts Speak C# Now](https://dev.to/ssukhpinder/my-shell-scripts-speak-c-now-hka)
