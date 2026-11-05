# Repositories: pull and run commands

Update all your git clones with one click instead of opening a terminal in each folder, and run a repository's own commands (a build, an install script) from the same place.

## Set up
1. DevPilot > **Repositories** > *Browse…* and pick the folder that holds your clones (for example your `Dev` folder).
2. DevPilot lists every git repository it finds there (up to 3 levels deep) with its current branch.
3. Untick the repositories you do not want pulled. New clones are ticked automatically.

## Pull
- **Hub**: the sync button in the header opens the repositories panel: tick the repositories, then *Pull selected*. The panel stays open (X or Esc closes it) and shows each result as it arrives.
- **Tray**: right-click the DevPilot icon > *Pull repositories* (pulls the ticked ones at once) or *Repositories…* (opens the panel).
- **Repositories page**: *Pull selected*, or the button on a single row.

A notification gives the summary (updated / up to date / skipped / failed) and names the repositories that need attention. The page shows the result of each one.

## Commands per repository
On the Repositories page, click the gear on a row:
- **Add command**: a name and a command line, for example `dotnet build -c Release` or `Install.bat`. It runs through `cmd.exe` in the repository folder, so `.bat` files, `&&` and pipes work.
- **In a window** off (default): the command runs hidden. The end of its output appears on the row and a notification says *done* or *failed* with the exit code and the last line. It is stopped after 10 minutes, and it cannot wait for keyboard input.
- **In a window** on: a console window opens and stays open when the command ends, so you can read an error. DevPilot does not wait for it.
- **Plain git pull button**: *Automatic* shows it only when the repository has no Update; *Always* shows it next to Update; *Never* hides it.

The button on a row depends on what the repository has:
- one action: it runs at once, and the label says which (*Update*, *Pull* or the command's name);
- several actions (pull plus commands, or several commands): the button reads *Run…* and opens a menu with one button per action.

A command and a pull never run on the same repository at the same time: *Pull selected* skips a repository whose command is still running, and a command is refused while a pull of all runs.

Commands are the ones you type; DevPilot runs nothing else. Do not put passwords in a command line (the config file is plain text).

## Update: pull, close, install, start again
For a repository where DevPilot can tell how to install it, or manages a tool that runs from it, the row button is **Update**:
1. `git pull` (same safety rules as below). Already up to date: it stops here and closes nothing.
2. The running tools of that repository are closed (tools DevPilot manages whose program or working folder is inside the repository).
3. The install step runs, hidden:
   - **Python**: the install script (`Install.bat`, `install.cmd`, `scripts\install.cmd` or a `.ps1`); without a script, `pip install -r requirements.txt` into the repository's `.venv` when it has both.
   - **C#**: the install script if there is one, otherwise `dotnet build <solution or project> -c Release`.
   - Anything else: no install step; the tools are still closed and started again so they load the new files.
4. The tools that were running are started again.

If the install fails, the tools stay closed and the row shows the last lines of the output, so a half-installed program is never started. The gear on the row lets you switch Update off, stop it from closing/starting the tools, type your own install command, or run **Reinstall now** (steps 2 to 4 without the pull). The Egress Guard still applies when the tools start again: in *block* mode a tool whose files changed must be approved again.

DevPilot's own repository is not updated this way: close DevPilot and run `scripts\install.cmd`.

## What a pull does, and does not do
- Runs `git status` first. If tracked files have uncommitted changes, the repository is **skipped** and nothing is touched.
- Runs `git pull --ff-only`. If your branch and the remote have both moved on, nothing changes and the repository is reported as *diverged*; pull or rebase it by hand.
- Never merges, rebases, stashes, resets or deletes anything.
- Pulls one repository at a time.

## Automatic pull
*Automatic pull* on the page: off (default), once when DevPilot starts, or at start and then every N minutes. Automatic runs can stay quiet when everything was already up to date.

## Results and what to do
| Result | Meaning | What to do |
|---|---|---|
| Updated | New commits were pulled | Restart the tool if it was running |
| Up to date | Nothing new | — |
| Skipped: local changes | Uncommitted changes in tracked files | Commit, stash or discard them, then pull again |
| Skipped: no upstream | The branch does not track a remote branch (or HEAD is detached) | `git branch -u origin/<branch>` or switch branch |
| Skipped: diverged | Local and remote commits differ | Pull or rebase by hand |
| Not trusted by git | "dubious ownership": the folder belongs to another account (common on synced drives) | Run once: `git config --global --add safe.directory "<folder>"` |
| Sign-in failed | The remote needs a sign-in | Run `git pull` once in a terminal to sign in |
| Remote unreachable | Offline, VPN or proxy | Try again later |
| git not found | Git for Windows is not installed | Install it and restart DevPilot |

## Synced drives (Google Drive, OneDrive)
Let the sync finish before pulling, and work on a repository from one PC at a time; a pull while the drive is still syncing `.git` can damage the clone. DevPilot cannot tell when the sync has finished.
