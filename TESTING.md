# Testing

Manual test steps per version (what you do → what should happen). Automated tests run in GitHub Actions.

## 2.13.0 — Pull repositories

Update before testing: exit DevPilot from the tray → `git checkout main` + `git pull` in the DevPilot folder → `scripts\install.cmd` (or `scripts\install.ps1`) to rebuild → start DevPilot.

1. DevPilot > **Repositories** > *Browse…* → pick your Dev folder → your clones appear with their branch; the line under the title says how many were found.
2. Untick one repository → close and reopen DevPilot → it is still unticked.
3. Hub → sync button in the header → a notification "Pulling repositories…", then a summary; the page shows a result and a coloured dot for each repository.
4. In one repository change a tracked file without committing → *Pull* on its row → "Skipped: local changes"; the file is unchanged.
5. Tray right-click > *Pull repositories* → same summary as step 3.
6. *Automatic pull* = "Once, when DevPilot starts" → restart DevPilot → about 30 s later the repositories are pulled (no notification if everything was up to date and the quiet option is on).
7. A repository on another branch with no remote branch → "Skipped: no upstream".
8. Disconnect the network → *Pull selected* → "Remote unreachable", DevPilot stays responsive.
