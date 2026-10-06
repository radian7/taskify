#!/usr/bin/env bash
# T144 (research R12): generate each API's OpenAPI document at build time with
# Microsoft.Extensions.ApiDescription.Server and compare it with the committed contract in
# specs/001-taskify-kanban-board/contracts/<name>-api.yaml. The contract stays the single copy.
# The contracts are hand-written (descriptions, shared error responses), so the comparison is
# structural: operations (method, path, operationId), path/query parameters and 2xx response codes
# must match, and every status code the code can return must be documented in the contract.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
contracts="${CONTRACTS_DIR:-specs/001-taskify-kanban-board/contracts}"
cd "$root"

# Startup runs Program.cs once to read the endpoint metadata; it never opens a connection.
export ConnectionStrings__projectsdb="Host=localhost;Database=openapi" \
       ConnectionStrings__tasksdb="Host=localhost;Database=openapi" \
       ConnectionStrings__notificationsdb="Host=localhost;Database=openapi"

PY=python3; "$PY" --version >/dev/null 2>&1 || PY=python
"$PY" -c "import yaml" 2>/dev/null || "$PY" -m pip install --quiet pyyaml

# APIs whose contract describes endpoints that are not built yet; for these, operations missing from the code
# are tolerated. Empty now that every endpoint exists.
pending=""

failed=0
for api in Projects Tasks Notifications; do
  lower="$(echo "$api" | tr '[:upper:]' '[:lower:]')"
  project="src/Taskify.$api.Api"
  rm -rf "$project/obj/openapi"
  dotnet build "$project" --no-incremental -p:GenerateOpenApi=true -v q --nologo -clp:ErrorsOnly
  extra=""
  case " $pending " in *" $api "*) extra="--allow-missing";; esac
  "$PY" .github/scripts/compare-openapi.py \
    "$project/obj/openapi/openapi.json" "$contracts/$lower-api.yaml" $extra || failed=1
done

if [ "$failed" -ne 0 ]; then
  echo "OpenAPI drift detected: update the contract or the code (research R12)."
  exit 1
fi
echo "OpenAPI documents match the contracts."
