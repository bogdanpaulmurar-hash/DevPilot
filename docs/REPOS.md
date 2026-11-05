# Pull repositories

Update all your git clones with one click instead of opening a terminal in each folder.

## Set up
1. DevPilot > **Repositories** > *Browse…* and pick the folder that holds your clones (for example your `Dev` folder).
2. DevPilot lists every git repository it finds there (up to 3 levels deep) with its current branch.
3. Untick the repositories you do not want pulled. New clones are ticked automatically.

## Pull
- **Hub**: the sync button in the header.
- **Tray**: right-click the DevPilot icon > *Pull repositories*.
- **Repositories page**: *Pull selected*, or *Pull* next to a single repository.

A notification gives the summary (updated / up to date / skipped / failed) and names the repositories that need attention. The page shows the result of each one.

## What DevPilot does, and does not do
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
