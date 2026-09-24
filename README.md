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
dotnet run --project FormattedDockerPs
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

- `shift`+`q`: quit

## Responsive behavior

- The table adapts to the available width.
- In narrow terminals, only essential columns are shown.
- Long values are truncated with `…` to prevent layout breakage.

## Build

```bash
dotnet build FormattedDockerPs
```

## Publish a Linux executable (single-file)

```bash
dotnet publish FormattedDockerPs -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
```

Generated binary (example):

```text
FormattedDockerPs/bin/Release/net10.0/linux-x64/publish/FormattedDockerPs
```

## Known limitations

- If `docker` is not installed or not accessible (for example due to `/var/run/docker.sock` permissions), the app shows the error in the window.
