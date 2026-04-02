# formatted-docker-ps

C# Terminal.Gui application that displays `docker ps` in a TUI window and refreshes every second.

## Goal

- Display Docker containers in a readable table.
- Refresh automatically every second.
- Stay usable even when the terminal is small (columns are resized, truncated, or hidden).

## Stack

- .NET (`net10.0`)
- [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)

## Run in development

```bash
dotnet run --project DockerPsTui
```

## Install (Linux)

Install globally to `/usr/local/bin`:

```bash
./install.sh
```

After installation, run from anywhere:

```bash
docker-ps
```

## Uninstall (Linux)

```bash
./uninstall.sh
```

## Controls

- `q`: quit

## Responsive behavior

- The table adapts to the available width.
- In narrow terminals, only essential columns are shown.
- Long values are truncated with `…` to prevent layout breakage.

## Build

```bash
dotnet build DockerPsTui
```

## Publish a Linux executable (single-file)

```bash
dotnet publish DockerPsTui -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
```

Generated binary (example):

```text
DockerPsTui/bin/Release/net10.0/linux-x64/publish/DockerPsTui
```

## Known limitations

- If `docker` is not installed or not accessible (for example due to `/var/run/docker.sock` permissions), the app shows the error in the window.
