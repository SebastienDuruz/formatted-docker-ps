# formatted-docker-ps

Application C# (Terminal.Gui) qui affiche `docker ps` dans une fenêtre TUI et rafraîchit l'affichage toutes les secondes.

## Objectif

- Afficher les conteneurs Docker dans une table lisible.
- Rafraîchir automatiquement toutes les 1s.
- Rester utilisable même si le terminal est petit (colonnes réduites, tronquées ou masquées).

## Stack

- .NET (`net10.0`)
- [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)

## Lancer en développement

```bash
dotnet run --project DockerPsTui
```

## Contrôles

- `q` : quitter
- `r` : forcer un rafraîchissement immédiat

## Comportement responsive

- La table adapte les colonnes selon la largeur disponible.
- En terminal étroit, seules les colonnes essentielles sont affichées.
- Les valeurs trop longues sont tronquées avec `…` pour éviter la casse du layout.

## Build

```bash
dotnet build DockerPsTui
```

## Publier un exécutable Linux (single-file)

```bash
dotnet publish DockerPsTui -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
```

Binaire généré (exemple) :

```text
DockerPsTui/bin/Release/net10.0/linux-x64/publish/DockerPsTui
```

## Limites connues

- Si `docker` n'est pas installé ou inaccessible (ex: permissions sur `/var/run/docker.sock`), l'application affiche l'erreur dans la fenêtre.
