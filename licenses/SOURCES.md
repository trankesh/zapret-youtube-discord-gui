# Corresponding sources and provenance

The release's `Zapret-Desktop-<version>-third-party-sources.zip` supplies the source
archives below alongside the GUI EXE/ZIP, without charge. `prepare-third-party-sources.ps1`
fetches the original archives and verifies pinned SHA-256 values before packaging.
No downloaded source or binary is executed by this script.

| Component | Source archive | SHA-256 |
| --- | --- | --- |
| WinDivert 2.2.2 | https://api.github.com/repos/basil00/WinDivert/tarball/v2.2.2 (commit `1789526ecfb9ff5397c94f9f54c1a3dc2fb60440`) | `b26a67dd9f58a899db1aa5b47a467c0efb98cf0d6cf357ec46c1198c260ccd7f` |
| Cygwin 3.4.10-1 | https://ftp.cvut.cz/mirrors/cygwin.com/x86_64/release/cygwin/cygwin-3.4.10-1-src.tar.xz | `ac70af0d4e644732f74946f55f7bafe010bab9b9da39a9327c3f0367f1fa43a2` |

The Cygwin source package includes its `cygwin.cygport` recipe and the complete
`newlib-cygwin-3.4.10.tar.bz2` source tarball, including their component notices.
WinDivert's archive includes driver/DLL sources, project files, build instructions
and its upstream LICENSE. See those archives for their respective build processes.
The GUI launches winws separately and does not link to the native DLLs itself.
You may use a separately built, compatible external Flowseal bundle in the GUI;
there is no requirement to use the embedded native binaries.

## Matching the distributed binaries

Compared without executing the files during release preparation:

- Flowseal's `cygwin1.dll` exactly matches `usr/bin/cygwin1.dll` from the publisher's
  `cygwin-3.4.10-1.tar.xz` package hosted on the same Cygwin mirror.
- Flowseal's x64 `WinDivert.dll` and `WinDivert64.sys` exactly match those in
  the official [WinDivert-2.2.2-A.zip](https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip).
- The embedded Flowseal 1.10.4 archive is unmodified and hash-pinned; binary hashes
  are recorded in [the bundle notice](../assets/bundle/NOTICE.md).

This is a comparison to the publishers' binary packages and identification of
corresponding upstream sources, not a claim of a bit-for-bit reproducible local
build of the native components. The original publishers' signatures remain intact.

Original projects: [WinDivert / basil00](https://github.com/basil00/WinDivert),
[Cygwin](https://cygwin.com/licensing.html),
[zapret / bol-van](https://github.com/bol-van/zapret),
[Windows bundle / bol-van](https://github.com/bol-van/zapret-win-bundle),
[Flowseal](https://github.com/Flowseal/zapret-discord-youtube).
