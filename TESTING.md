# Testing

Manual test steps per version (what you do → what should happen). Automated tests run in GitHub Actions.

## 2.14.0 — Repositories panel and commands

Update before testing: exit DevPilot from the tray → `git checkout main` + `git pull` in the DevPilot folder → `scripts\install.cmd` (or `scripts\install.ps1`) to rebuild → start DevPilot.

1. Hub → the sync button in the header → a panel opens with your repositories, a tick each; the number found and the selection are in the header line.
2. Panel → *Pull selected* → each row shows a result as it finishes; no console window appears; a summary notification at the end.
3. DevPilot > Repositories > gear on a repository > *Add command* → name `Hello`, command `echo hello & ping -n 3 127.0.0.1 > nul`, leave *In a window* off → the row button now reads *Run…* (pull plus a command).
4. Panel → that row's button → a menu with two buttons (git pull, Hello). Click *Hello* → the row says "Running Hello…", then "Done" with `hello`-style output below it, and a notification.
5. Add a command `Fail` = `echo oops & exit /b 3` → run it → the row shows "Failed (exit code 3)" in red with `oops`, a notification says failed; no window to close.
6. Add a command `Win` = `echo shown here & dir`, tick *In a window* → run it → a console opens and stays open after it ends.
7. On a repository: switch off *Offer git pull*, delete all commands but one → the row button shows that command's name and runs it directly without a menu.
8. Start a long command (`ping -n 30 127.0.0.1 > nul`) → while it runs press *Pull selected* → that repository says "Skipped: command running", the others pull.
9. Restart DevPilot → the commands are still there.


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
