#!/usr/bin/env bash
# Runs once after the dev container is created (devcontainer.json: postCreateCommand).
set -euo pipefail
cd "$(dirname "$0")/.."

# Docker creates named volumes, and the missing parent folders of their mount points, owned by root; the build
# and user secrets run as "vscode".
sudo chown "$(id -u):$(id -g)" "$HOME/.microsoft" "$HOME/.microsoft/usersecrets" "$HOME/.nuget" "$HOME/.nuget/packages" .artifacts

# .github/scripts/check-openapi-drift.sh needs Python with PyYAML.
echo '==> python3-yaml'
sudo apt-get update -qq
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends python3 python3-yaml > /dev/null

echo '==> dotnet tool restore'
dotnet tool restore

# HTTPS-only service traffic (R16). On Linux this trusts the certificate for OpenSSL through the folder in
# SSL_CERT_DIR (devcontainer.json); browsers on the host still show a warning (docs/devcontainer.md).
echo '==> HTTPS development certificate'
dotnet dev-certs https --trust

# Kept across rebuilds on a volume; the script leaves existing values alone.
echo '==> development secrets'
pwsh ./scripts/init-dev-secrets.ps1

echo '==> build'
dotnet build Taskify.slnx -v q --nologo -clp:ErrorsOnly

echo '==> Playwright Chromium for the E2E tests'
pwsh .artifacts/bin/Taskify.E2ETests/debug/playwright.ps1 install --with-deps chromium

echo ''
echo 'Ready. Build and test: ./scripts/verify.ps1   Run: dotnet run --project src/Taskify.AppHost'
