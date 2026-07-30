# MLPS Level 3 Phase 1 Closeout

You are closing the remaining release-blocking items for the already-implemented MLPS Level 3 Phase 1 kernel capabilities in this repository. Other uncommitted changes belong to other contributors. Preserve them and do not revert unrelated files.

## Scope

Implement only these two review findings:

1. `web-react/src/views/mfa/BindPage.tsx`: after `mfaApi.bindComplete`, reject an absent or empty `recoveryCodes` response before transitioning to the recovery-code display. Show the existing localized response-integrity error. The account has already been bound server-side, so never present an empty successful recovery-code screen.
2. `web/src/api/index.ts`: replace manually duplicated MFA request/response interfaces with types derived from `components['schemas']` in `web/src/api/schema.d.ts`, matching the typed-contract approach used by the React client. Do not hand-edit generated schema files.

## Regression Coverage

Add or extend focused tests where the existing test conventions make this practical, especially for the React empty-recovery-code response. Do not add dependencies.

## Verification

Run and report each command separately so one long-running backend command cannot hide frontend results:

- `npm run typecheck`, `npm test`, and `npm run build` in `web-react`
- `npm run typecheck`, `npm test`, and `npm run build` in `web`
- `dotnet build TenonAdmin.slnx --no-restore` in `backend`
- Run the smallest relevant backend test subset if feasible. If a command times out, report the exact command, timeout, and any observed output; do not claim it passed.

Do not expand scope, edit generated OpenAPI schema declarations, commit changes, or claim that the product itself has passed MLPS Level 3 certification. Finish with changed files, test/build results, and residual risks.
