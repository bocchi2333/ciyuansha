# .NET SDK Requirement

The current LAN playtest package runs as a Godot Mono C# project.

Every playtest PC must install:

- .NET SDK 8.0 or later
- Download: https://dotnet.microsoft.com/download/dotnet/8.0

Important:

- Installing only the `.NET Runtime` is not enough.
- Godot Mono needs the SDK to load and build the C# project.
- After installing the SDK, close Godot and run `HostPlaytest.bat` or `JoinPlaytest.bat` again.

Common error:

```text
Unable to load .NET runtime, no compatible version was found.
Please install the .NET SDK 8.0 or later.
```

This means the machine has no compatible .NET SDK installed, or it was installed while Godot was already open and Godot needs to be restarted.
