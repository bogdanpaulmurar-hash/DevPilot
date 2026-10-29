# claudepolicy.md — how personal projects reach a managed work laptop

Neutral, safe-to-publish version. It describes the workflow and the rules that
keep it clean. It contains no employer name, no internal identifiers and no
confidential details. Employer-specific rules live only in a local file on the
work laptop (`%APPDATA%\DevPilot\policy.local.json`, never synced, never in git).

## 1. The workflow (why odd things can happen)

1. A tool is written **privately**, with a personal AI subscription, on a personal
   machine, in a personal GitHub repo. Nothing from the work environment goes in.
2. The repo is the source of truth; Google Drive (personal) syncs the working copy.
3. The work laptop receives the tool through that synced folder or a `git pull`
   and runs it under DevPilot.
4. A second AI session on the work laptop finishes the integration. It never sends
   work-environment details back to the personal side.

Consequences to expect: tools arrive without any knowledge of the work network
(proxy, TLS inspection, blocked domains, restricted APIs), so they may fail to
connect, fail to update, or behave differently than at home. That is by design.
Fix it on the work side with local configuration, not by adding work details to
the repo.

## 2. Hard rules

- **Two sides, one-way information.** Code and docs flow personal → work. Nothing
  about the work environment flows work → personal (names, hosts, paths, tickets,
  policy text, screenshots, logs with internal data).
- **No confidential or customer data** in any AI tool, prompt, commit, issue or log.
- **No personal data** without a legal basis.
- **No unapproved data-sending extensions**, plugins, MCP servers or auto-updaters.
- **AI output is never the sole basis** for security, legal, compliance or HR decisions.
- **Secrets** only in keyring or environment variables; never in files in the repo.
- **Deviations** go through the employer's exception/reporting process, not around it.

## 3. What DevPilot checks (Egress Guard)

| Stage | Check |
|---|---|
| Intake (new tool / folder / pull) | Static scan: network APIs, hard-coded hosts, telemetry, auto-update, upload, clipboard/screen/keyboard capture, process spawning, persistence, embedded secrets, local deny-list terms |
| Provenance | Git remote + commit, SHA-256 manifest of files; any change after approval = re-review |
| Approval | Per-tool allowlist of hosts/ports in `devpilot.tool.json` (`network` block); unapproved tools do not start in `block` mode |
| Runtime | Per-process outbound connection monitor compared with the allowlist; log, warn, or stop the tool |
| Kill switch | One tray action: pause external access for all managed tools |
| Repo hygiene | Secret scan before commit/CI; local deny-list scan on the work side only |

Modes (`DEVPILOT_EGRESS` / setting): `off`, `monitor` (default), `block`.
Limits: DevPilot sees and controls only what it launches. It is a guard rail and an
audit aid, not a substitute for the employer's own controls, and it never tries to
bypass, disable or hide from them.

## 4. Roles

- Personal AI session: builds features from generic requirements only.
- Work AI session: adds local policy, deny-list, allowlists, approval records.
- Owner: decides, approves, reports exceptions.
