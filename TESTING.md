# Testing

Manual test steps per version (what you do → what should happen). Automated tests run in GitHub Actions.

## 2.18.0 — Fix… for sign-in errors

Update before testing: DevPilot > Repositories > *Update* on the DevPilot row (DevPilot closes, installs and starts again); check v2.18.0 bottom-left under *Guide*.

1. A repository whose remote address has a user in it (`https://user@github.com/…`) with the hidden `git pull` command → it fails fast; the row shows *Full output* and *Fix…*.
2. *Fix…* → a dialog shows the address now and the new one without the user. *Yes* → the address changes and the action runs again; check with `git remote -v`. *Cancel* changes nothing.
3. *Fix…* → *No* (or on a repository without a user in the address) → a window opens and runs the command; sign in there; the next hidden run works.
4. *Full output* → long lines wrap, no horizontal scrolling.

## 2.17.2 — Output window, sign-in hint, and the 2.17.1 fixes

Update before testing: DevPilot > Repositories > *Update* on the DevPilot row (the installed 2.17.0 does the pull; then run `scripts\install.cmd` once if it did not install by itself), or exit from the tray → `git pull` → `scripts\install.cmd`. Running version: Settings page, bottom-left under *Guide*.

1. Hub panel → a row with a message shows a *Full output* button; click it (or the message) → a window opens on top of the panel with the whole text, *Copy all* works.
2. A hidden `git pull` command on a repository that needs a sign-in → fails within seconds; the output ends with the hint to run it *In a window*.
3. The same command with *In a window* → the console opens; when the pull brings a new version the row's version changes within a few seconds.
4. *Pull* or *Pull selected* on the DevPilot repository with a new version on GitHub → DevPilot closes, the install window runs, the new version starts. Nothing new → nothing happens.
5. Automatic pull (schedule) on DevPilot's repository → only pulls; DevPilot is not restarted.

## 2.17.0 — Full output, linked tools, DevPilot updates itself

Update before testing: exit DevPilot from the tray → `git checkout main` + `git pull` in the DevPilot folder → `scripts\install.cmd` → start DevPilot. (After this version the Update button on the DevPilot row does these steps itself.)

1. Run *git pull* or *Update* on a row → click the grey message under it → a window opens with the full text (`$ git pull --ff-only` and what git printed; for Update also the install output). *Copy all* works.
2. A repository whose tool starts through a shortcut (so only *git pull* was offered): open its gear → under *Tools to close and start again* tick the tool → the button becomes *Update*. With the tool running and something new on GitHub → the tool closes, installs, starts again.
3. Gear > the same list shows tools found by path ticked and greyed.
4. DevPilot repository: the row now offers *Update*. Push a new version, click it → DevPilot closes, a console window builds and installs, DevPilot starts again with the new version (check the version on the Repositories page and, if start with Windows was on, that it still is).
5. *Update* on DevPilot with nothing new → "Already up to date", DevPilot stays open.

## 2.16.0 — Version of each repository

Update before testing: exit DevPilot from the tray → `git checkout main` + `git pull` in the DevPilot folder → `scripts\install.cmd` (or `scripts\install.ps1`) to rebuild → start DevPilot.

1. DevPilot > Repositories → each row shows the version next to the branch (DevPilot `2.16.0`, ScreenPilot its `__version__`); hover it → "Version … from <file> · commit …".
2. Hub > sync button → the panel shows the same versions.
3. A repository without a version file → the row shows a 7-character commit instead.
4. Bump the version of a tool on the other PC (or on GitHub), push, then *Update* (or *Pull*) on that row → the result reads "old → new · … files changed", the version on the row changes, and the notification shows the same line.
5. Push a change that does not touch the version, then *Pull* → "x.y.z (unchanged) · …".
6. *Update* when nothing is new → "Already up to date (x.y.z). Nothing was closed or installed."; *Reinstall now* → the result starts with "Version: x.y.z".

## 2.15.0 — Update (pull, close, install, start again)

Update before testing: exit DevPilot from the tray → `git checkout main` + `git pull` in the DevPilot folder → `scripts\install.cmd` (or `scripts\install.ps1`) to rebuild → start DevPilot.

1. DevPilot > Repositories > open the gear of a Python repository that has `Install.bat` (for example ScreenPilot) → the text under *Update* names the install script and the tools that will be closed.
2. Same for a C# repository without an install script → it says `dotnet build <solution> -c Release`.
3. Hub panel → that repository's button reads *Update*. Click it with the tool running and nothing new on GitHub → "Already up to date", the tool stays open.
4. Push a small change to that repository from the other PC (or commit on GitHub), click *Update* → the row shows "Closing …", "Installing…", "Starting …"; the tool closes, reinstalls and starts again; a notification says done; no console window appears.
5. Make the install fail (rename `Install.bat` temporarily, or type a wrong *Install command* in the gear) and update again → the result says failed with the last lines of output, and the tool stays closed.
6. Change a tracked file in the repository without committing → *Update* → "Skipped: local changes", nothing is closed.
7. Gear > *Close the running tools…* off → Update installs without touching the running tool.
8. A tool with auto-restart on: use *Stop* in the hub → it stays stopped (it is not started again by the monitor).
9. *Reinstall now* → closes, installs, starts again without a pull.
10. (Changed in 2.17.0: DevPilot's own repository now updates itself.)


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
