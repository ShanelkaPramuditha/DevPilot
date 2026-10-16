# Releasing DevPilot

Releases are built and published by GitHub Actions when a version tag is pushed
(`.github/workflows/release.yml`). You never edit version numbers in the code: the tag
is the version.

## Version numbers

DevPilot uses [Semantic Versioning](https://semver.org): `vMAJOR.MINOR.PATCH`, with a `v` prefix.

| Increase | When the release... | Example |
|---|---|---|
| **MAJOR** | breaks something users rely on: removes a feature, changes settings incompatibly, drops a Windows version | `v1.4.2` → `v2.0.0` |
| **MINOR** | adds a feature, nothing breaks | `v1.4.2` → `v1.5.0` |
| **PATCH** | only fixes bugs | `v1.4.2` → `v1.4.3` |

When a part increases, the parts to its right go back to 0.

### Pre-releases

Add a hyphen and a label to publish a GitHub **pre-release** (not shown as "Latest"):

| Tag | Meaning |
|---|---|
| `v1.5.0-alpha.1` | Early build, may be unstable |
| `v1.5.0-beta.1`, `v1.5.0-beta.2` | Feature-complete, being tested |
| `v1.5.0-rc.1` | Release candidate: becomes `v1.5.0` unless a problem is found |

Always put a dot before the number (`beta.1`, not `beta1`) so `beta.10` sorts after `beta.2`.

### Tags the workflow accepts

| Tag | Result |
|---|---|
| `v1.2.0` | Release, marked **Latest** |
| `v1.3.0-beta.1` | Pre-release |
| `1.2.0`, `v1.2`, `v1.2.0.1`, `release-1.2` | Rejected: the workflow fails with an explanation |

## Before releasing

- [ ] Everything for the release is merged into `main`, and **CI passed** on the last commit.
- [ ] Merged pull requests have a label, so the release notes group them correctly:
      `feature`, `bug`, `ui`, `dependencies`, `maintenance` (or `skip-changelog` to leave one out).
      Categories are defined in `.github/release.yml`.
- [ ] For a new MINOR or MAJOR version, consider a pre-release (`-rc.1`) first and try the
      installer on both an Intel/AMD and, if possible, an ARM PC.

## Publishing a release

Release from an up-to-date `main`:

```powershell
git switch main
git pull
git tag -a v1.5.0 -m "DevPilot 1.5.0"
git push origin v1.5.0
```

- Use an **annotated** tag (`-a`): it records who released it, when, and why.
- `git push` alone does not send tags; push the tag explicitly as above.

The **Release** workflow then (about 15 minutes):

1. checks the tag format and reads the version from it;
2. builds and runs all tests at that tag;
3. builds a self-contained installer and portable zip for **x64** and **ARM64**;
4. publishes the GitHub Release with `SHA256SUMS.txt` and release notes: a "which file do I
   need" table followed by the merged pull requests grouped by label.

Follow it in the repository's **Actions** tab.

## After releasing

- [ ] Open the release on GitHub and check the notes and the five files
      (two installers, two portable zips, `SHA256SUMS.txt`).
- [ ] Install the new version over the previous one to confirm the upgrade works.
- [ ] Edit the release text on GitHub if a highlight or known issue should be added at the top.

## When something goes wrong

**The workflow failed** (for example the installer did not build): fix the problem on `main`.

- If nothing was published yet, delete the tag and tag the fixed commit again:

  ```powershell
  git tag -d v1.5.0
  git push origin :refs/tags/v1.5.0
  ```

- If a release was published, release the next patch version (`v1.5.1`) instead.

**A published release has a bug:** release a patch version (`v1.5.1`). Never move, reuse or
re-create a published tag; people and tools treat a version as fixed.

**The release needs rebuilding without code changes** (for example a runner problem): run the
**Release** workflow manually from the Actions tab and enter the existing tag. It replaces the
files and notes of that release.

## Useful commands

```powershell
git tag --list "v*" --sort=-v:refname   # all versions, newest first
git describe --tags --abbrev=0          # latest version
git log v1.4.0..v1.5.0 --oneline        # commits between two versions
```
