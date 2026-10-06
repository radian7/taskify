# Taskify.AppHost

The Aspire orchestration: PostgreSQL (TLS on, one database per service), the three APIs and the Web app, all over HTTPS
only ([ADR 0004](../../docs/adr/0004-encryption-in-transit.md)). It passes each service its API keys and database role
secrets (create them with `scripts/init-dev-secrets.ps1`).

## Optional settings

| Setting | Default | Meaning |
|---|---|---|
| `Taskify:PersistData` | `true` | `false` runs PostgreSQL without a data volume; the automated tests use this so every run starts from the sample data |
| `Taskify:DataVolumeName` | `taskify-postgres-data` | Name of the PostgreSQL data volume; only the persistence test overrides it, to use a throwaway volume |

Pass them on the command line, for example `--Taskify:DataVolumeName=my-volume`. The test fixture `TaskifyAppFixture`
(`tests/Taskify.TestSupport`) is not sealed and accepts `ExtraArguments`, which is how a test class passes them.
