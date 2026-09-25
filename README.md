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

- `q`: quit
- `Left` / `Right`: select the previous/next button on the same row
- `Up` / `Down`: select the closest button on the previous/next action row
- `Enter` or `Space`: activate the focused button
- Mouse wheel: scroll vertically (three lines per tick), independently of selection
- Left click: select and activate a button

Arrow navigation stops at the edges and keeps the selected button visible. After
scrolling with the mouse, the next arrow resumes from the selection and brings its
destination into view. Refreshes preserve selection by resource identity.

## Responsive behavior

- The table adapts to the available width.
- In narrow terminals, only essential columns are shown.
- Long values are truncated with `…` to prevent layout breakage.
- Tables use continuous Unicode borders; there is no horizontal scrolling.
- Action columns retain enough space for complete buttons. Below 46 terminal
  columns, a message asks you to enlarge the terminal.

## Build

```bash
dotnet build FormattedDockerPs
```

Run the interaction regression checks (Terminal.Gui's fake driver, no Docker calls):

```bash
dotnet run --project tests/InteractionChecks
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
