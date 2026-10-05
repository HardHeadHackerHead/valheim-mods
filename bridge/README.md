# bridge

The mod manager lives in its own repo (valheim-mod-manager) and updates itself from there. Managers older than 2.9.0 only look at this repo,
so `publish.ps1` also offers the prebuilt ModUpdater files in this folder (listed in `entries.json`) so they can update once.
When nobody runs a manager older than 2.9.0 any more, delete this folder.
