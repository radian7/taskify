<!--
Sync Impact Report
==================
Version change: (unversioned template) → 1.0.0
Bump rationale: Initial ratification; all placeholders replaced with concrete governance.

Modified principles (template slot → new title):
  - [PRINCIPLE_1_NAME] → I. Security-First (NON-NEGOTIABLE)
  - [PRINCIPLE_2_NAME] → II. Validate All Inputs (NON-NEGOTIABLE)
  - [PRINCIPLE_3_NAME] → III. Microservices Architecture
  - [PRINCIPLE_4_NAME] → IV. Fully Documented Code
  - [PRINCIPLE_5_NAME] → removed (user supplied four principles; no fifth invented)

Added sections:
  - Security Requirements (from [SECTION_2_NAME])
  - Development Workflow & Quality Gates (from [SECTION_3_NAME])
  - Governance (filled)

Removed sections:
  - Fifth principle slot (intentionally omitted)

Deferred TODOs: none
-->

# Taskify Constitution

## Core Principles

### I. Security-First (NON-NEGOTIABLE)

Security is a primary design constraint for every feature, not a later hardening step.

- Every feature specification and plan MUST include a threat assessment covering authentication,
  authorization, data exposure, and abuse cases.
- Services MUST apply least privilege: each service, credential, and user role is granted only the
  permissions it needs.
- Secrets (keys, tokens, passwords, connection strings) MUST NOT be committed to source control
  and MUST be loaded from a secrets manager or environment configuration.
- Data MUST be encrypted in transit (TLS) between clients and services and between services.
- Sensitive data (credentials, tokens, personal data) MUST NOT appear in logs, error messages, or
  API responses beyond what the caller is authorized to see.
- Known high or critical vulnerabilities in dependencies MUST be resolved or formally risk-accepted
  before release.

**Rationale**: Taskify is a Security-First application; a defect that leaks data or grants
unauthorized access outweighs any feature benefit.

### II. Validate All Inputs (NON-NEGOTIABLE)

All user input and all data crossing a trust boundary MUST be validated before use.

- Every API endpoint, message consumer, form, and CLI entry point MUST validate input against an
  explicit schema (type, format, length, range, and allowed values).
- Validation MUST happen server-side in the receiving service; client-side validation is a
  usability aid only and never a substitute.
- Validation MUST be allow-list based: reject anything not explicitly permitted.
- Invalid input MUST be rejected with a clear, non-revealing error and MUST NOT be partially
  processed.
- Output that includes user-supplied data MUST be encoded for its context (HTML, SQL, shell, URL);
  database access MUST use parameterized queries.
- Messages received from other internal services MUST also be validated; internal origin does not
  imply trust.
- Each validation rule MUST have automated tests covering accepted and rejected cases.

**Rationale**: Unvalidated input is the root cause of injection, tampering, and most data
corruption defects.

### III. Microservices Architecture

Taskify is built as a set of independently deployable services with clear boundaries.

- Each service MUST own a single, well-defined business capability and its own data store; services
  MUST NOT read or write another service's database directly.
- Services MUST communicate only through versioned, documented contracts (HTTP/gRPC APIs or
  message schemas).
- Breaking contract changes MUST be introduced through a new contract version, with the prior
  version supported until all consumers have migrated.
- Each service MUST be independently buildable, testable, and deployable.
- Every service MUST authenticate and authorize inter-service calls (see Principle I).
- Every service MUST expose health checks and emit structured logs with correlation IDs so
  requests can be traced across services.
- Creating a new service MUST be justified in the feature plan; a capability that fits an existing
  service's boundary belongs in that service.

**Rationale**: Clear service boundaries allow independent scaling and deployment and contain the
impact of failures and security breaches.

### IV. Fully Documented Code

All code MUST be documented so that it can be understood, reviewed, and maintained without its
original author.

- Every public module, class, function, and method MUST have a documentation comment describing
  its purpose, parameters, return value, errors raised, and any security-relevant behavior.
- Non-obvious logic, security decisions, and validation rules MUST carry inline comments explaining
  why, not only what.
- Every service MUST have a README covering its responsibility, how to run and test it,
  configuration, and dependencies.
- Every API and message contract MUST be documented in a machine-readable format (e.g., OpenAPI,
  AsyncAPI, Protobuf) kept in sync with the implementation.
- Significant architectural decisions MUST be recorded as Architecture Decision Records (ADRs).
- Documentation MUST be updated in the same change as the code it describes; outdated
  documentation is treated as a defect.

**Rationale**: In a distributed system, documentation is the contract between teams and services;
undocumented code cannot be safely reviewed for security.

## Security Requirements

- Authentication MUST use an established standard (e.g., OAuth 2.0 / OpenID Connect); custom
  cryptography or home-grown authentication schemes are prohibited.
- Authorization MUST be enforced in every service on every request, not only at the gateway.
- Security-relevant events (logins, permission changes, failed authorization, validation
  rejections at abnormal rates) MUST be logged for audit.
- Public endpoints MUST implement rate limiting.
- Static analysis (SAST) and dependency vulnerability scanning MUST run in CI for every service.
- Security incidents and accepted risks MUST be documented with an owner and review date.

## Development Workflow & Quality Gates

A change MUST NOT be merged unless all of the following gates pass:

1. **Code review**: at least one approving reviewer who verifies compliance with this
   constitution, with explicit attention to security and input validation.
2. **Automated tests**: unit tests and validation-rule tests pass; contract tests pass for any
   changed service interface.
3. **Security checks**: SAST and dependency scans report no unresolved high or critical findings.
4. **Documentation check**: required doc comments, README updates, and contract specifications
   are present and current.
5. **Contract compatibility**: changes to service contracts are versioned and backward compatible,
   or a migration plan is approved.

Feature plans produced via Spec Kit MUST include a Constitution Check that evaluates the design
against each Core Principle before implementation begins.

## Governance

- This constitution supersedes all other development practices and guidelines for Taskify. Where
  another document conflicts with it, this constitution prevails.
- Amendments MUST be proposed as a pull request that modifies this file, states the rationale, and
  describes the impact on existing services and templates. Amendments require approval from the
  project maintainers before merge.
- Versioning follows semantic versioning:
  - **MAJOR**: removal or backward-incompatible redefinition of a principle or governance rule.
  - **MINOR**: a new principle or section, or materially expanded guidance.
  - **PATCH**: clarifications, wording, and typo fixes with no change in meaning.
- Every pull request review MUST verify compliance with the Core Principles. Any deviation MUST be
  justified in writing in the feature plan's Complexity Tracking section and approved by a
  reviewer.
- Compliance with this constitution MUST be reviewed at least once per quarter, and findings
  recorded.

**Version**: 1.0.0 | **Ratified**: 2026-10-04 | **Last Amended**: 2026-10-04
