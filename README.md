# DashieRR Custom Stuff

This repository provides the small Windows installer for the custom DashieRR
emotes and props. Download **DashieRR-Custom-Setup.exe** from the latest
[release](https://github.com/owenleegamer/DashieRR-Custom-Stuff/releases/latest).
The small setup checks the latest release, downloads the larger custom package,
verifies its GitHub SHA-256 digest, and opens the installer.

The package requires the matching Rec Room / DashieRR game build at
`C:\Games\DashieRR`; it does not include the full game. The protected launcher
keeps backups of custom assets when checking for game updates and postpones
unverified patch changes until the custom mod is compatible.

## Publishing a new custom version

On the machine that builds the custom package, Git Credential Manager must be
signed in to the repository owner's account. After building the full package,
run:

```
python publish_release.py --publish --package path/to/DashieRR-Custom-Setup.exe --version VERSION
```

The publisher creates a draft release, uploads the full package and the small
setup, checks the GitHub asset digests, then publishes the release. Existing
assets are not overwritten or deleted. Credentials come from Git Credential
Manager and are never stored in this repository or included in the installers.

In the original workspace, enabling `auto-publish.json` also runs the publisher
after a successful custom package build. The local settings file contains no
credentials and is not committed. Set `DASHIERR_SKIP_AUTO_PUBLISH=1` only for a
build that should stay local. Publishing requires network access and valid
GitHub authorization; local build errors or unverified uploads do not produce
a public release.
