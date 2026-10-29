# Egress Guard

An opt-in layer that helps keep the tools DevPilot launches inside IT and AI policy. It checks what a tool *could* send out before it starts, and watches what it *does* connect to while it runs. It is a safety net for honest mistakes, not a security boundary: it cannot stop a determined or malicious program.

## Modes (Egress Guard page, or `Guard.Mode` in the config)

| Mode | Behaviour |
|---|---|
| `off` | Nothing is checked. |
| `monitor` (default) | Every tool starts. A tool that is not approved is noted in the log; a connection outside the tool's allowlist is logged and shown as a rate-limited notification. |
| `block` | Only an *Approved* tool with unchanged files and git remote starts. A connection outside a declared allowlist stops the tool's process tree. |

A tool with no declared allowlist is only logged in every mode: block needs an allowlist to decide against.

## What it does

1. **Scan** (static): looks through a tool folder for network clients, hard-coded URLs and IPs, telemetry, auto-update, uploads, clipboard/screen/key capture, process spawning, persistence, AI clients, embedded secrets and local deny terms. Rules are JSON data; each finding has a short why and fix. Findings never contain the matched text.
2. **Provenance and drift**: reads the git remote and commit from `.git` (no git.exe), hashes the tool's files (SHA-256). *Approve* stores that snapshot plus the allowlist; later changes show the tool as *Changed since approval*. Tools in shared folders (drive root, Windows, Program Files) are identified by their program file only.
3. **Approval gate**: Unreviewed, Approved, Blocked or Changed. Checked by the launcher before a start.
4. **Runtime monitor**: polls the TCP connection table (IP Helper API, every 5 s) for managed processes and their children. No driver, no packet capture, no admin rights. Loopback is ignored.
5. **Soft offline**: a tool with *Start offline* gets dead-proxy variables (`HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY` = `http://127.0.0.1:9`, empty `NO_PROXY`, `DEVPILOT_OFFLINE=1`). Only tools that honour proxy variables are affected.
6. **Kill switch**: tray *Pause external access* stops running tools that could use the network and makes the launcher refuse to start them until resumed (any mode). The state survives restarts.
7. **Pre-flight check**: tray *Check clipboard before pasting into AI* and *Check a file before pasting into AI…* look for deny terms, secrets, e-mail addresses, IBANs, card numbers and phone numbers. The result shows category, line and column only, never the text.

## Allowlist

Per tool: the `network` block of `devpilot.tool.json` (see INTEGRATION.md), or what was approved on the Guard page (the approved list wins at runtime). Hosts match exactly; `*.example.org` matches subdomains only.

## Machine-local policy (optional)

`%APPDATA%\DevPilot\policy.local.json` is never committed and never logged:

```json
{ "denyTerms": ["some-internal-name"], "alwaysAllowedHosts": ["updates.example.org"] }
```

A deny term is plain text (matched anywhere, ignoring case, so `acme` also catches `AcmeCorp`), or uses `*` for "up to 40 characters" (`project*alpha` catches `project-x-alpha`; a leading or trailing `*` changes nothing), or starts with `re:` for a regular expression (`re:PRJ-\d{4}`). Use a few specific names (client, project, product, internal domain, code prefix); common words cause false alarms.

`denyTerms` feed the scanner, the pre-flight check and the secret check script; matches report positions or files, never the term. `alwaysAllowedHosts` apply to all tools.

## Incidents

When a tool connects outside its allowlist, DevPilot writes a plain-text report to `%LOCALAPPDATA%\DevPilot\incidents\` (time, tool, start path, processes, destination, verdict and why, the allowlist in force, the tool's recent events, what to do). In `monitor` mode you get a notification and the tool keeps running. In `block` mode the tool is stopped and marked Blocked (so auto-restart cannot bring it back), and a dialog offers to open the report; it stays blocked until you review it on the Egress Guard page (*Reset*, then approve a corrected allowlist). The report is local: it names paths and destinations, so do not paste it outside the work PC. Detection happens after the connection opened; prevention is the approval gate plus a declared allowlist.

## Limits

- TCP only (UDP has no remote endpoint to read). Connections shorter than the 5 s poll can be missed.
- Host names come from DNS lookups of the remote address and may be wrong for shared hosting or CDNs.
- A tool that spawns a process outside its tree, or runs elsewhere (another user, a service), is not seen.
- Soft offline and the pre-flight check are aids, not guarantees.
- Not a replacement for the organisation's own controls.

## Repo checks

CI and the optional pre-commit hook (`scripts\install-hooks.cmd`) run `scripts/check-secrets.ps1`: generic secret patterns in tracked or staged files, plus the local deny terms when the policy file exists (file names only).
