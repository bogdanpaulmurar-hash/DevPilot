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

## Cloud and local copy
A repository can live in two places: the **cloud copy** on the synced drive (Google Drive, OneDrive), and a **local copy** on the PC that the application runs from. Set them in the gear of the row:
- **Cloud path**: empty = the clone found in the folder above (usually the one on the drive). Type another folder only when the cloud copy is elsewhere.
- **Local path**: empty = no local copy. Point it at the folder the application should run from, for example `C:\Apps\ScreenPilot`.

With a local path:
- **Pull** pulls the cloud copy, then the local copy (*Pull selected* and the automatic pull do the same).
- **Update** pulls both; the install step runs in the local copy and only the tools that run from the local copy are closed and started again. A problem on the cloud copy is reported but does not stop the install of the local copy.
- **Reinstall** installs the local copy again, without pulling.
- A local path that does not exist yet, or an empty folder, is **cloned** from the cloud copy's `origin` and current branch on the next Pull or Update. A folder with files that is not a repository is never written to.
- **Clone from repository**: a repository without a local copy shows this button (and a ⋯ menu entry). DevPilot never clones silently: it lists the local drives of this PC (C first, then D, then others; the synced drive is left out) with the folder each would get (`D:\Dev\<name>`) and the free space, or lets you choose any folder. The folder you pick is saved as the Local path, then the clone starts. Drives are read at run time, so another laptop with other drives gets its own list.
- A fresh clone has only the files in git: a `.venv`, a local config or a `.env` that are not committed have to be created there once (for example by running the install script).
- Point the DevPilot tool at the local copy so Update finds it by path.

## More actions (⋯)
The ⋯ button of a row lists everything the repository can do: pull both copies, pull only the cloud or only the local copy (or clone it), Update, Reinstall, its commands, open the cloud or local folder, open a terminal in the copy that runs, and its settings.

## Version
Each row shows the version of the clone next to its branch (hover for where it came from). DevPilot reads it from the files, without running anything, in this order: `devpilot.tool.json` (`"version"`), `Directory.Build.props` or the csproj `<Version>` (the app project before libraries), `pyproject.toml`, Python `__version__` / `VERSION` in `version.py` or a package `__init__.py` (top folder, one folder down, or under `src\`), `package.json`, a `VERSION` / `version.txt` file. Without any of these it shows the short commit.

After a pull or an Update that brought something new, the result reads "2.14.0 → 2.15.0 · 3 files changed…"; when the files kept the same version it says "2.15.0 (unchanged)". This is the version in the clone, not of the program that is running: they differ until the tool is installed again.

## What a pull does, and does not do
- Runs `git status` first. If tracked files have uncommitted changes, the repository is **skipped** and nothing is touched.
- Runs `git pull --ff-only`. If your branch and the remote have both moved on, nothing changes and the repository is reported as *diverged*; pull or rebase it by hand.
- Never merges, rebases, resets or deletes anything, and never stashes on its own. Only *Fix…* (after you choose) sets changes aside with `git stash` and can put them back.
- Pulls one repository at a time.

## Full output
Click the message under a repository to open everything the last action printed (git, install, a command), with *Copy all*.

## Update: which tools are closed
Tools found by path (working folder, program or arguments inside the repository) are closed and started again. A tool that starts from elsewhere (a shortcut, a launcher) is added by ticking it in the gear of the repository.

## DevPilot's own repository
*Update* pulls, closes DevPilot, runs `scripts\install.cmd` in a window of its own and the new version starts by itself.

## Automatic pull
*Automatic pull* on the page: off (default), once when DevPilot starts, or at start and then every N minutes. Automatic runs can stay quiet when everything was already up to date.

## Results and what to do
| Result | Meaning | What to do |
|---|---|---|
| Updated | New commits were pulled | Restart the tool if it was running |
| Up to date | Nothing new | — |
| Skipped: local changes | Uncommitted changes in tracked files | Click *Fix…* to set them aside, pull and put them back, or commit them yourself |
| Skipped: no upstream | The branch does not track a remote branch (or HEAD is detached) | `git branch -u origin/<branch>` or switch branch |
| Skipped: diverged | Local and remote commits differ | Pull or rebase by hand |
| Not trusted by git | "dubious ownership": the folder belongs to another account (common on synced drives) | Run once: `git config --global --add safe.directory "<folder>"` |
| Sign-in failed | The remote needs a sign-in git cannot ask for in the background (the message says which kind) | Click *Fix…*: see *Sign-in* below |
| Remote unreachable | Offline, VPN or proxy | Try again later |
| git not found | Git for Windows is not installed | Install it and restart DevPilot |

## Sign-in
Background runs never ask for a sign-in (nobody would see the question), so a missing one ends as "Sign-in failed". *Fix…* opens a console window where git may ask (Git Credential Manager can open a browser page). The window runs `git fetch` in each existing copy, which signs in and changes no file, and clones a local copy that is still missing. DevPilot waits for the window, then runs the pull or Update again by itself. If a step fails, the window stays open until a key is pressed, so the reason can be read. A clone that stops at a sign-in opens the same question right away and keeps the chosen folder as the Local path.

**Another account on this PC.** If the git configuration of the PC names an account for the site (`credential.https://<site>.username`, or `credential.username`) and the repository belongs to a different one, git keeps asking for the first account and a browser sign-in with the second does not help. The row then says which account git tried, and *Fix…* offers to use the repository owner's account for this repository only. That is saved in the repository's own git settings (each copy, and a new clone), where it wins over the setting of the PC. To undo it, in the repository: `git config --unset credential.https://<site>.username`.

If the sign-in works in the window but the next background run still fails, git is not saving it: `git config --global credential.helper` should print `manager`. "The repository does not exist for the account" means git is signed in with an account that cannot open that repository.

## Log
The bottom of the Repositories page shows the path of the log file (`%LOCALAPPDATA%\DevPilot\logs\devpilot.log`) and *Show the file*. Failed pulls and clones are logged with what git answered; addresses are written without user names or tokens. Send this file when something does not work.

## Synced drives (Google Drive, OneDrive)
Let the sync finish before pulling, and work on a repository from one PC at a time; a pull while the drive is still syncing `.git` can damage the clone. DevPilot cannot tell when the sync has finished.
