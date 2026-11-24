# FL Studio CLI preview acceptance — WP-SYNC-06

Scope: CLI/engine and FL Studio only; the GUI is a separate product. A green
synthetic test does not certify opening a real project in FL Studio, two physical
workstations, Windows OpenSSH, or a particular NAS/SMB deployment.

## Automated packaged-product check

Requires Python 3.11+ for QA only; DAWVC users do not need Python. The Windows
CI job packages the self-contained executable, verifies its ZIP SHA-256, extracts
it into an isolated directory and invokes that executable directly. It does not
invoke Program.Main in a testhost or a build-output DLL for this check.

```powershell
python scripts/test_preview_acceptance.py --archive artifacts/dawvc-v0.1.0-preview.3-win-x64.zip --checksums artifacts/SHA256SUMS.txt --report artifacts/preview-acceptance-windows.json
```

Use the archive name actually produced by `scripts/package.ps1`. SHA-256 matches
protect integrity against the supplied checksum file; they are not a publisher
signature or proof that an independently supplied binary is trustworthy. The
extractor rejects traversal, links, case collisions, Windows trailing-dot/space
aliases/device names, encrypted/unsupported members and oversized archives before launching the executable.

For source-build checks on Linux or Windows:

```powershell
python scripts/test_preview_acceptance.py --dll src/DawVcs.Cli/bin/Release/net10.0/dawvc.dll --dotnet "C:\Program Files\dotnet\dotnet.exe" --report artifacts/preview-acceptance-source.json
```

Quote paths containing spaces, e.g. `--dotnet "C:\Program Files\dotnet\dotnet.exe"`.
On Linux select the actual dotnet path. Alternatively `--cli <native-executable>`
checks an already-extracted executable; this mode records its hash but does not
verify an archive checksum. Native modes set invalid DOTNET_ROOT/DOTNET_ROOT_X64
and disable multilevel lookup. This exercises self-contained operation but does
not prove execution on a clean Windows VM without an SDK installed.

The runner creates two isolated working copies on **one host**, generates a small
opaque/legacy synthetic FLP envelope plus a valid PCM WAV, and exercises:

- init, explicit sample add/commit, source-derived bare init and first Push;
- ordinary Clone with FLP/recording byte identity;
- independent recording commits, non-fast-forward rejection and Fetch;
- dirty Merge refusal preserving user bytes and branch;
- whole-snapshot incoming Merge and restoration of both original parent versions;
- ordinary Push/Pull through the second parent and integrity checks on both copies;
- a fresh remote Clone restoring the unchosen local parent, proving it survived transfer;
- Bare Clone preserving the merged tip without working files.

The FLP itself stays byte-identical throughout: changing the bundled WAV creates
the divergence. The synthetic FLP is **not** claimed to be an editable project
saved by FL Studio 2026. Both merge choices already have core acceptance coverage;
this distribution smoke check exercises the incoming choice.

All projects and remote storage are disposable and cleaned on exit. For an actual
mounted share/NAS destination, pass an existing, operator-owned parent:

```powershell
python scripts/test_preview_acceptance.py --cli C:\Tools\dawvc\dawvc.exe --remote-parent \\nas\qa-share --report artifacts/preview-acceptance-share.json
```

Only a new `dawvc-acceptance-remote-*` child is provisioned/removed; never pass an
existing repository as a test destination or add unrelated files inside the child.
This accesses the share from one client, not two machines. Ensure the parent has
sufficient space and reliable rename/locking semantics. No SSH policy, host keys,
credentials, DAW installation or global configuration is changed by this runner.
Interrupted processes/permissions failures can leave temporary data; inspect
ownership/inactivity before deleting it. Test parents must be trusted.

## Evidence contract

Exit 0 means every check passed; nonzero means failure, including argument errors.
Always check the exit code. A valid run overwrites the specified JSON report;
argument errors can leave an older report untouched, so verify timestamp/hash.

Schema version 1 records UTC start time, OS/architecture, fixture/workstation/
transport scope, entrypoint binary SHA-256, reported version, optional ZIP SHA-256
and per-check expected/actual exit codes and durations or equality outcomes.
For DLL mode the entrypoint hash does not cover all dependent assemblies.
`real_fl_studio_validated`, `real_multi_machine_validated`, and `ssh_validated`
are explicitly false for this runner. External-parent mode does not infer or
certify the underlying storage protocol/vendor. Raw project paths, native scan
output, credentials, file contents and child stdout/stderr are not exported;
version is the ordinary CLI version output. Filesystem failures retain only the
exception type. The report contains hashes of the tested distribution, not music.

CI uploads only `preview-acceptance-{windows,linux}.json`, even after a failing
workflow when a report exists, with 14-day retention. It does not upload projects,
keys, full command logs or generated storage. Linux DLL evidence and Windows
packaged-executable evidence are distinct. The release checklist must cite exact
artifact/version/hash and test scope rather than treating a badge as native DAW
certification.

## Native Windows / FL Studio acceptance still required

Perform these checks with authorized, noncommercial/redistributable test material
or keep private musical material locally. Do not upload FLP files, samples, plugin
licenses or SSH keys as CI evidence. Use copies of projects and fresh hosting
storage. Record a result for each applicable row; unexecuted rows stay **pending**.

| Gate | Setup | Required evidence |
|---|---|---|
| WIN-PACK | Clean supported Windows x64 VM, no .NET runtime/SDK | Exact ZIP hash/version; extracted exe `--version`/`--help` and automated runner success; OS build |
| FLP-LOCAL | Installed supported FL Studio 2026.x on Windows | Project saved by that exact FL Studio build; commit/restore byte identity; restored project opens, plays and has required samples/plugins |
| FLP-2PC | Two Windows PCs with different user/project/library paths | Producer A → host → B → host → A lifecycle; both FL Studio builds; project opens/plays on both after documented relinking |
| FLP-DIVERGE | Two PCs independently editing arrangements and/or explicitly bundled recordings | Rejected Push; Fetch; deliberate local/incoming selection; both parents independently restored/opened; subsequent Push/Pull works |
| SSH-WIN | Actual Windows OpenSSH client/host and explicit allowed alias | Same lifecycle; public-key auth; unknown/changed host-key refusal; denied push policy refusal; no password/keys exported |
| STORAGE-SMB | Actual intended UNC/NAS server and two clients | Vendor/OS/SMB version and relevant locking/rename configuration; lifecycle; concurrent publication rejected without tip loss; outage/retry inspection |
| RECOVERY | Disposable project copies | Dirty tracked/staged/untracked-collision protection; cancellation, killed-publication recovery and conflict refusal; physical power-cycle/storage qualification remains separate |

### Two-PC procedure

1. Record candidate ZIP hash/version, Windows/FL Studio builds, transport kind and
   fixture inventory/permissions. Keep hostnames, user paths and credentials in a
   private operator log; the shareable summary need only identify the deployment
   class. Install each collaborator's own licensed plugins/libraries.
2. Save a real FLP on producer A. Make a safe copy for acceptance. Initialize it
   with an explicit primary, explicitly add project recordings/samples intended
   for bundling and commit. Use `doctor`; assess findings rather than assuming
   exit 0 means all required external content exists.
3. On the host or mounted storage, use `init --bare --from <source> --dir <new-path>`
   or `clone <source> <new-path> --bare`. Preserve source identity. For SSH configure
   an allowed alias, trusted host key, public-key access and explicit AllowPush.
   The source may be a dawvc:// URL; the provisioning target is host-local.
4. Configure origin on A and Push. On B, use normal Clone into a different path.
   Compare FLP and bundled asset hashes, run Fsck and Doctor, configure documented
   FL Studio Extra Search Folders as needed, open/play the restored project.
5. Commit one independent edit on each machine. Push B first. A's Push must refuse
   non-fast-forward without moving the remote tip. Fetch on A; restore B's tip to
   a separate folder for review. Record both full tip IDs.
6. Commit or move unsaved/staged work before Merge. Choose one **complete** version:
   `dawvc merge origin/main --take incoming -m "Acceptance: choose B version"`
   (or local in a separate fresh scenario). Verify FLP/assets match the chosen
   snapshot; restore both parent IDs separately and open/play both originals.
7. Push A's merge, Pull on B, verify matching tip/content, Fsck/Doctor and native
   FL Studio playback. If someone advanced the remote meanwhile, inspect/fetch
   again; do not force-push. Never run local mutating commands concurrently.
8. Exercise refusals/recovery on disposable copies. Cancellation after publication
   may observe a completed operation; inspect log/status before retrying. Do not
   delete repository objects, live ref locks or recovery backups as a reset.

### Result template (manual, separate from automated JSON)

```text
Candidate version / ZIP SHA-256:
Gate ID:
Date (UTC) / tester:
Windows build(s) / FL Studio build(s):
Transport / storage class / relevant settings:
Fixture class and permission basis (no project contents):
Commands / expected outcome / observed outcome:
FLP and bundled asset identity verified: yes / no / not applicable
Native open/playback and relinking outcome:
Integrity / Doctor findings and disposition:
Recovery / refusal outcome:
Result: passed / failed / pending
Known limits / private evidence location:
```

No native/deployment gate is automatically marked passed by the acceptance runner.
WP-SYNC-07 adds process-kill-tested persistent workspace recovery; actual power-
cycle/storage qualification remains open. Semantic FLP merging and GUI work remain
separate scopes.

WP-SYNC-09 integrates the upstream/fresh-preview branches into the recovery/locking
candidate. Include a differently named local branch mapped by `remote track` in
native collaboration tests; confirm preview and actual Pull agree on a stable
host, and actual Pull updates the active branch through either merge parent.
Preview writes no local coordination/index files and is not an atomic view or
reservation during other writers. Track honors Busy/pending recovery; a recovered
Pull can retain its already-fetched tracking refs. Ubuntu CI additionally exercises
SIGINT/child cleanup for all four sync commands. This is not Windows console Ctrl+C
or native FL Studio/two-PC qualification. All unexecuted field gates stay pending.
