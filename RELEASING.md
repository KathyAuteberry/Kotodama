# Releasing Kotodama

**Local builds and tests are Debug-only.** Never build `-c Release` locally and never point Dalamud at a
Release output. Release ZIPs are built only by the tag workflow on a clean GitHub Actions runner. Use
Conventional Commit messages. Nothing is pushed, tagged or published without the owner's approval.

## Before the first public release

- **License (blocking).** EchoXIV has no license, so a public repository or release ZIP redistributes
  code its author hasn't licensed. Get ssalazar217's permission or a license first (`docs/AUDIT.md`,
  finding 11).
- Create the public `KathyAuteberry/Kotodama` repository and add it as the `origin` remote.
- Add an Actions secret **`PLOGONS_TOKEN`** to the Kotodama repository: a fine-grained token restricted to
  `KathyAuteberry/plogons` with Contents read/write. It's the same kind of token as FluffySim's; a separate
  token per repository keeps them independently revocable.
- plogons branch rules must allow the bot push to `master`.
- Push the plogons change `feat(tools): sync source metadata and changelog into the manifest` first. The Kotodama
  workflow calls `update_manifest.py release --plugin Kotodama` and `metadata --plugin Kotodama`.

## Pipeline

| Trigger | Workflow | What it does |
|---|---|---|
| Push or PR | `.github/workflows/build.yml` | Debug build, xUnit tests, Python tooling tests. |
| Tag `vX.Y.Z.0` | `.github/workflows/publish-kotodama.yml` | Checks that the tag equals `v$(KotodamaVersion).0` and is on published `master`. Runs all tests. Builds Release on the runner, packages `Kotodama-X.Y.Z.0.zip`, creates the GitHub release (notes from `CHANGELOG.md`), verifies it is public and stable, then updates `plogons/mostly-harmless.json` from the **downloaded** public ZIP and pushes. Push races are retried up to 3 times. |

A rerun (`gh workflow run publish-kotodama.yml --ref vX.Y.Z.0`) reuses the existing release asset and never
replaces it. A broken release is fixed with a higher version, never a moved tag or replaced ZIP.

## Steps (after validation and explicit owner approval)

1. Bump `Kotodama.Version.props` (`KotodamaVersion`, three parts; the tag adds `.0`) and add a
   `# Kotodama X.Y.Z.0 — …` entry to `CHANGELOG.md`.
2. Debug checks:

   ```bash
   dotnet build Kotodama.sln -c Debug
   ```

   ```bash
   dotnet test Kotodama.sln -c Debug
   ```

   ```bash
   python -m unittest discover -s tools/tests
   ```

3. Optional local packaging dry run on Debug output (never distribute this ZIP):

   ```bash
   python tools/package_release.py --input Kotodama/bin/x64/Debug --output /tmp/kotodama-package-check
   ```

4. Commit, set `master` to the reviewed change, push `master`, and wait for CI to pass on that exact commit.
5. Tag and push the tag:

   ```bash
   jj tag set vX.Y.Z.0 -r master
   ```

   ```bash
   jj git push --remote origin --tag vX.Y.Z.0
   ```

6. Check the Actions run, the GitHub release, and the Kotodama entry in
   https://raw.githubusercontent.com/KathyAuteberry/plogons/master/mostly-harmless.json.

## Failed publication

- Test or package failures publish nothing.
- If the release exists but the metadata step failed, fix the credentials or the metadata state and dispatch
  the same tag again.
- Manual metadata recovery follows `plogons/AGENTS.md`, using `--plugin Kotodama`.

## Metadata without a release

Author, punchline, description, tags, repository/icon URLs and the plogons `Changelog` come from
`master`, not from the release ZIP. `Kotodama/Kotodama.json` holds them; the changelog is a plain-text
summary of the `CHANGELOG.md` entry for the version plogons already publishes (section headings, then
each bullet's bold lead-in and first sentence; `## Internal` is left out). Pushing a change to either file
on `master` runs `.github/workflows/sync-plogons-metadata.yml`, which updates only those fields in plogons;
version, API level and download links stay as released. The release workflow runs the same `metadata`
step right after recording a new ZIP. Run it by hand with
`gh workflow run sync-plogons-metadata.yml --ref master`, or preview locally from a plogons checkout with
`python tools/update_manifest.py metadata --plugin Kotodama --source ../Kotodama --print-changelog`.
