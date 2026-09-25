# formatted-docker-ps

A Linux-only terminal application to monitor and manage Docker containers, volumes
and networks. It refreshes every second and adapts to your terminal size. Start,
stop or delete containers, and remove volumes and networks from the interface.

## Installation

Requirements:

- Linux on x86_64 or ARM64.
- Docker installed and running, with the `docker` command accessible to your user.
- .NET 10 SDK to build the application.
- Git to clone this repository, and `sudo` (or root access) for installation.

```bash
git clone https://github.com/SebastienDuruz/formatted-docker-ps.git
cd formatted-docker-ps
./install.sh
```

The script builds a self-contained executable and installs it as
`/usr/local/bin/docker-ps`. No separate .NET runtime is needed to run it.

## Usage

```bash
docker-ps
```

Use the arrow keys to navigate, `Enter` or `Space` to activate a button, and `q` to
quit. Mouse clicks and wheel scrolling are also supported.

## Uninstall

From the cloned repository:

```bash
./uninstall.sh
```
