# Dev container

`.devcontainer/` describes a ready-to-code Linux environment ([containers.dev](https://containers.dev)) for VS Code:
the .NET 10 SDK, PowerShell, Docker-in-Docker for Aspire, Python and Node for the CI checks, and Playwright's
Chromium for the E2E tests. `scripts/verify.ps1`, the AppHost and every test project run in it unchanged.

## What the container sets up

| Piece | Why |
|---|---|
| `mcr.microsoft.com/devcontainers/base:noble` + .NET feature `10.0.101` | Exactly the SDK in `global.json`. A newer 10.0 feature band would win the roll-forward and bring analyzer rules that fail the `-warnaserror` build (for example CA1873 in 10.0.4xx). When `global.json` changes, change `devcontainer.json` with it. |
| Docker-in-Docker feature | Aspire starts PostgreSQL as a container, and the services reach it on `localhost`. With the host's Docker socket the published port would sit on the host, out of the services' reach. |
| PowerShell feature, `pwsh` as the default terminal | `scripts/verify.ps1`, `scripts/init-dev-secrets.ps1` |
| Node feature, `python3-yaml` from apt | `npx @asyncapi/cli`, `.github/scripts/check-openapi-drift.sh` |
| `TASKIFY_DEVCONTAINER=true` | `Directory.Build.props` switches to [artifacts output](https://learn.microsoft.com/dotnet/core/sdk/artifacts-output) in `.artifacts/`. The Linux build never touches the Windows `bin/` and `obj/` of a checkout that is shared with the host. |
| `SSL_CERT_DIR` | The services call each other over HTTPS (ADR 0004). On Linux `dotnet dev-certs https --trust` trusts the development certificate for OpenSSL only through this folder. |
| `TASKIFY_E2E_BROWSER=chromium` | The E2E tests drive Playwright's Chromium, because Google Chrome is not installed in the container |
| Volumes | User secrets, the NuGet cache and `.artifacts/` survive a rebuild (see [Volumes](#volumes)) |

`.devcontainer/post-create.sh` runs once after the container is created: Python with PyYAML from apt, `dotnet tool restore`, the HTTPS
development certificate, `scripts/init-dev-secrets.ps1`, a full build and `playwright.ps1 install --with-deps chromium`.
The first start takes several minutes.

## Option A: Docker Engine in WSL (recommended)

Docker Desktop is not needed. The Dev Containers extension runs the `docker` CLI inside the WSL distribution.

1. Install the **Dev Containers** extension (`ms-vscode-remote.remote-containers`).
2. In the VS Code user settings, set `"dev.containers.executeInWSL": true` and leave `dev.containers.dockerPath` at its
   default (`docker`). Your WSL user has to be in the `docker` group (`docker ps` works without `sudo`).
3. Open the repository and run **Dev Containers: Reopen in Container**.

**File system speed.** A checkout on a Windows drive (`D:\git_repos\taskify`) reaches the container through
`/mnt/d`, which is slow for builds and for file watching. The build output already sits on a volume, so that part is
fast. For the best performance, clone the repository into the WSL file system (for example `~/src/taskify`, opened
from WSL with `code .`) or use **Dev Containers: Clone Repository in Container Volume**. In both cases the container
has its own checkout, separate from the Windows one.

## Option B: Podman

1. Docker-in-Docker needs a privileged container, which a rootless Podman machine does not run well. Make the
   machine rootful and give it enough memory (the AppHost, four .NET services and PostgreSQL need about 6 GB):

   ```powershell
   podman machine stop
   podman machine set --rootful --memory 8192
   podman machine start
   ```

2. In the VS Code user settings: `"dev.containers.dockerPath": "podman"` and `"dev.containers.executeInWSL": false`.
   These are user settings, so switching between Docker and Podman means changing them back.
3. **Dev Containers: Reopen in Container**.

Inside the container the runtime is always Docker (Docker-in-Docker), whatever the host uses. `verify.ps1` finds
`docker` on PATH and does not touch Podman.

## Working in the container

```powershell
./scripts/verify.ps1                                   # build + unit + bUnit
./scripts/verify.ps1 -Tests integration -Class '*CommentsContractTests'
./scripts/verify.ps1 -Tests e2e
dotnet run --project src/Taskify.AppHost               # dashboard: https://localhost:17100
```

VS Code forwards the dashboard port (17100) and opens it. The link with the login token is printed in the terminal.
Ports that the services open later are forwarded automatically and silently; the Web app's address is on the
dashboard.

The build output is in `.artifacts/bin/<project>/debug/`, not in the project's `bin/`. For example the Playwright
script is `.artifacts/bin/Taskify.E2ETests/debug/playwright.ps1`.

### HTTPS certificate in the host browser

The container creates its own development certificate. Inside the container it is trusted. The browser on Windows does
not know it, so it shows a certificate warning for the forwarded ports. You can accept the warning. To get rid of it,
use the certificate that Windows already trusts in the container as well:

```powershell
# On Windows, outside the repository:
dotnet dev-certs https -ep "$env:USERPROFILE\.aspnet\https\taskify-dev.pfx" -p <password>
```

Copy the file into the container (for example drag it into the VS Code explorer, into `~`), then in the container:

```powershell
dotnet dev-certs https --clean
dotnet dev-certs https --import ~/taskify-dev.pfx -p <password>
dotnet dev-certs https --trust
Remove-Item ~/taskify-dev.pfx
```

Never put the `.pfx` file in the repository (`.gitignore` excludes `*.pfx`).

## Volumes

| Volume | Path in the container | Contents |
|---|---|---|
| `taskify-usersecrets-<id>` | `~/.microsoft/usersecrets` | The AppHost's development secrets |
| `taskify-nuget-<id>` | `~/.nuget/packages` | NuGet cache |
| `taskify-artifacts-<id>` | `.artifacts/` | Build output |
| `dind-var-lib-docker-<id>` | `/var/lib/docker` | Docker-in-Docker images and volumes, including `taskify-postgres-data` |

The secrets volume matters: the PostgreSQL data volume survives a rebuild, and it only opens with the password it was
created with. To start over, remove both the secrets volume and the Docker-in-Docker volume, or inside the container
run `docker volume rm taskify-postgres-data` followed by `./scripts/init-dev-secrets.ps1 -Force`.

The secrets in the container are separate from the ones on Windows (`%APPDATA%\Microsoft\UserSecrets`). Data you
create in the container does not show up in a Windows run, and the other way round.

## Claude Code

The container installs the Claude Code extension. You sign in once per container. `CLAUDE.md` and `.claude/` come
from the repository. Your personal settings and memory in the Windows `~/.claude` are not available in the container.
