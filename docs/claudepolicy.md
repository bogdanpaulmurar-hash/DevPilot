# Working across a personal and a managed laptop

Neutral guide for how DevPilot work flows between two machines, and why some things behave differently on a managed (company) laptop. It contains no policy details. Anything that needs them is listed at the end as a task for the Claude that runs on the managed laptop.

## The flow
1. GitHub is the source of truth. Only one laptop works on a repo at a time.
2. Commit and push before switching laptops; let Google Drive finish syncing, then `git pull` on the other one.
3. Implementation, tests and PRs happen in the personal environment (cloud or personal laptop). CI is the compile and test check, because the cloud session has no .NET SDK and WPF tests need Windows.
4. The managed laptop pulls `main`, installs with `scripts\install.cmd` and reports results (steps are in `TESTING`/`MODIFICARI`, results only).
5. Two Claude contexts exist: the **personal** one (builds features from `docs/prompts/PERSONAL_CLAUDE_PROMPT.md`, never sees company data) and the **work** one (runs checks that need the managed environment and writes back only sanitised findings).

## Why odd behaviour can appear only on the managed laptop
These are common causes on managed Windows machines. They are hypotheses to check, not confirmed facts.

| Symptom | Likely cause class | First check |
|---|---|---|
| `install.ps1` says "not digitally signed" even with `-ExecutionPolicy Bypass` | Script execution enforced centrally | Use `scripts\install.cmd` (already documented in `docs/INSTALL.md`) |
| App starts but the tray icon or hotkeys do nothing | Application control or input restrictions | Check `%LOCALAPPDATA%\DevPilot\logs\devpilot.log` |
| New `.dll`/binaries blocked or quarantined after build | Application allow-listing or endpoint protection | Build output location and what the log reports |
| Downloads or NuGet restore fail, TLS errors | Proxy or TLS inspection with a custom root certificate | Does the same command work on the personal laptop? |
| Drive clone is slow, `.git` conflicts, duplicate nested folders | Drive sync touching `.git` | Keep one clone, let sync finish (see `scripts/fix-drive-clone.cmd`) |
| Backups or logs land somewhere unexpected | Redirected `Documents` or profile folders | Resolve the real folder path in Settings |
| A tool cannot reach a service only here | Outbound filtering for network or AI services | Motivation for the Egress Guard: it records what was attempted, not why it was blocked |

## What stays out of the repo
Employer name, company e-mails, internal IDs, host names, ticket numbers, policy text and exact security-product names. Use generic wording ("managed laptop", "policy file").

## Work-side handoff (for the Claude on the managed laptop)
Do these only there, and report back **sanitised** results (yes/no/short note, no names or values):
- [ ] Is PowerShell script execution restricted? Does `scripts\install.cmd` complete?
- [ ] Is a user-level app (no admin) allowed to start with Windows and show a tray icon?
- [ ] Is polling the TCP connection table from a user process allowed (needed by the Egress Guard sampler)?
- [ ] Where does `Documents` actually point, and is the default backup folder writable?
- [ ] Can the policy file location chosen for the Egress Guard be read by a user process?
- [ ] Which categories of destinations does the real policy distinguish (names only for the work-side policy file, never copied here)?

Open questions for the author: confirm which of the symptoms above you actually saw, so the table can be trimmed to facts.
