#!/usr/bin/env bash
# Runs once after the dev container is created (devcontainer.json: postCreateCommand).
set -euo pipefail
cd "$(dirname "$0")/.."

# Docker creates named volumes owned by root; the build and user secrets run as "vscode".
sudo chown "$(id -u):$(id -g)" "$HOME/.microsoft/usersecrets" "$HOME/.nuget/packages" .artifacts

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
