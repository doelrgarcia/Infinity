# Infinity Windows installer

The per-user x64 MSI installs the verified application into
`%LOCALAPPDATA%\Programs\Infinity\app`. It adds three Start menu entries:

- **Infinity** checks dependencies first and offers missing tools and models before opening the workspace.
- **Infinity - set up AI tools** opens dependency and model setup.
- **Infinity - verify installation** checks installed application hashes and parses settings without sending requests or running inference.

The MSI contains public model profiles only. Host configuration, credentials,
conversations, models, and provider accounts are excluded. Existing
`appsettings.json` is preserved during installation and repair. Removal retains
settings and application data. The installer does not alter the manual
`releases` installation or its `Start.cmd` launcher.

This public beta candidate is unsigned. Windows 11 x64 is the supported beta target. Installing the
workspace does not establish provider connectivity or install local models.

## Build and verification

First obtain a passing immutable application report with `scripts/verify_release.py`.
Set `TEMP`, `TMP`, and `PYTHONDONTWRITEBYTECODE=1` for the task to an owned folder
under `C:\tmp`. Build with `scripts/build_msi.py` using these arguments:

```text
--report <passing-report.json>
--work <new-owned-build-directory>
--output <deliverable-directory>\Infinity-Setup-1.26.1113-x64.msi
--version 1.26.1113
--ui-extension <WixToolset.UI.wixext.dll>
```

WiX 7 and .NET 10 are required. The UI extension must match
`third-party/wix-ui-source-pin.json`. Its corresponding unmodified UI source and
license are included in both this repository and the installed payload.

Run the packaging guard tests in `scripts/test_build_msi.py`, the read-only
cabinet/database checks in `scripts/inspect_msi.py`, and the verifier guard suite
in `VerifierTests`. These checks do not install the MSI. Installation, repair,
removal, retained settings, and application startup require separate VM results.

The `.build.json` records application revision, payload count, definition hashes,
and MSI hash. The `.sha256` file accompanies the deliverable. Preserve these
alongside the MSI and record VM results separately; a built or inspected package
is not proof of a working guest installation.

Installer behavior uses WiX [per-user package scope](https://docs.firegiant.com/wix/schema/wxs/packagescopetype/),
[major upgrades](https://docs.firegiant.com/wix/schema/wxs/majorupgrade/), and the
[WiX UI library](https://docs.firegiant.com/wix/tools/wixext/wixui/).
