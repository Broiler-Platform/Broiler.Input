# CI, packages, and releases

The five component repositories use the same workflow structure and release helpers.
`Directory.Packages.props` centrally manages dependency versions. Release versions
are separate: `eng/Broiler.Packaging.props` supplies defaults, with repository
overrides in `Directory.Build.props`. Packages within one repository share a version;
each repository advances its own preview sequence.

## Build, test, and pack

Use the .NET 10 SDK, Node.js 24 for release scripts, PowerShell 7 (or Windows PowerShell) for packaging,
and Bash (Git Bash on Windows) for the test runner.

```sh
dotnet build Broiler.Input.slnx -c Release
bash ./eng/run-tests.sh Release
node --test eng/resolve-preview-version.test.mjs
powershell -File eng/pack.ps1
```

Use `Debug` or `Release`; platform-suffixed solution configurations are obsolete.
The test script runs the suites appropriate to the host. Input and Graphics build
the host's platform suite explicitly because those projects are excluded from the
normal solution build. DOM uses `dotnet test`; the other components use console runners.

`eng/pack.ps1` enumerates **every packable project**, including excluded providers,
and verifies all 23 packages, versions, internal dependencies, README, icon,
assemblies, API documentation, and symbols. Tests, demos, and diagnostic tools do
not ship. Use Windows to pack the complete set. The output directory must contain
no previous packages; choose `-Output <empty-directory>` for another run. Optional
`-Version 0.1.0-preview.N` stamps the assembly and package versions together.

## Package feeds

All packages (including upstream `Broiler.*` dependencies such as `Broiler.Native.*` and `Broiler.Media.*`) are restored exclusively from **NuGet.org** (`https://api.nuget.org/v3/index.json`). No external package registry or credentials are required for restore.

Cross-repository dependencies use the versions in `Directory.Packages.props`; sibling source checkouts do not replace them.
`NuGet.config` at the repository root explicitly clears inherited sources, disabled-source settings, and source mappings to enforce NuGet.org as the sole package source:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <disabledPackageSources>
    <clear />
  </disabledPackageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Native dependencies (`Broiler.Native.Windows`, `Broiler.Native.Linux`) must be published to NuGet.org before Media; Media before Input; and those dependencies before Graphics. Selecting NuGet.org as the publish destination requires that every upstream `Broiler.*` version pinned in `Directory.Packages.props` already exists on NuGet.org, or the consumer-restore check fails before anything is pushed.

## CI and Publish

CI builds and tests `Release` on Ubuntu and Windows. Windows packs and attaches the complete package set as `nuget-packages`. Publish calls this same CI workflow with the resolved version, packs, and downloads its validated artifacts; it does not rebuild them.

### Consumer Restore Verification

Before packages are published to NuGet.org, `eng/verify-feed.ps1` verifies that a consumer project can successfully restore all 23 packages along with their transitive dependencies in an isolated sandbox with a fresh cache:

```sh
powershell -File eng/verify-feed.ps1 -Packages artifacts
```

The restore uses an isolated package cache, the local release artifacts, and NuGet.org for public dependencies. It catches missing or unlisted upstream dependencies before any package is pushed.

### Version Resolution and Publishing

Run the **Publish** workflow manually via GitHub Actions (`workflow_dispatch`) or by pushing a release tag (`v0.1.0-preview.N`).

- **Dry Run (`dry-run=true`, default):** Selects the preview version, runs CI, packs, and verifies a fresh consumer restore without pushing to NuGet.org.
- **Automatic Preview Selection:** Leave `version-suffix` empty to automatically compute the next unused `preview.N` on the release line by checking all shipping package IDs against NuGet.org.
- **Explicit Version Suffix:** Provide `preview.N` to target a specific preview number. It must be unused on NuGet.org and at least the computed next preview floor.
- **Pushing via Tag:** A tag such as `v0.1.0-preview.2` publishes that exact version to NuGet.org after passing validation and consumer restore checks. Only `X.Y.Z-preview.N` versions matching the configured release line are accepted.

Publishing to NuGet.org requires the repository secret `NUGET_TOKEN` (or `NUGET_API_KEY`). Symbol packages (`.snupkg`) are generated and pushed alongside `.nupkg` packages to NuGet.org.
