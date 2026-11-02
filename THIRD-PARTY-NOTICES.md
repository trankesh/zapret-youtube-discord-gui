# Third-party components and acknowledgements

The MIT license in LICENSE covers Zapret Desktop's own GUI code. It does not
replace the licenses of the components below. Original copyright notices remain
with their authors. The standalone EXE embeds the GUI license and component
notices; the full ZIP also supplies readable copies.

| Component | Author / team | License / notices | Original project |
| --- | --- | --- | --- |
| Flowseal configuration bundle 1.10.4 | Flowseal, bol-van and contributors | MIT; `assets/bundle/LICENSE-Flowseal.txt` (`LICENSE-Flowseal.txt` in distribution) | https://github.com/Flowseal/zapret-discord-youtube |
| zapret / winws | bol-van and contributors | MIT; original notices are retained in the Flowseal license | https://github.com/bol-van/zapret and https://github.com/bol-van/zapret-win-bundle |
| PT Root UI VF | Paratype Inc / Paratype Ltd | SIL Open Font License 1.1; Reserved Font Name PT Root UI; `assets/fonts/OFL.txt` (`OFL.txt` in distribution) | https://www.paratype.com/fonts/pt/pt-root-ui |
| WinDivert.dll / WinDivert64.sys | basil00 and contributors | LGPLv3 or GPLv2 at the recipient's choice; `licenses/WinDivert-LICENSE.txt` includes full texts | https://github.com/basil00/WinDivert |
| cygwin1.dll | Red Hat and Cygwin contributors | LGPLv3 or later, with the Cygwin linking exception; `licenses/LGPL-3.0.txt`, `licenses/GPL-3.0.txt`, `licenses/Cygwin-linking-exception.txt` | https://cygwin.com/licensing.html |

## Sources for distributed native libraries

Matching WinDivert 2.2.2 and Cygwin 3.4.10-1 source archives, including build files
and upstream notices, are supplied as `Zapret-Desktop-<version>-third-party-sources.zip`
on the same [release page](https://github.com/trankesh/zapret-youtube-discord-gui/releases)
as the EXE/ZIP. Their URLs, exact hashes, and binary comparisons are documented in
[licenses/SOURCES.md](licenses/SOURCES.md). The public source repository also
contains a verification/download script, `prepare-third-party-sources.ps1`.

The GUI launches winws separately; it does not link to WinDivert or Cygwin.
Users may select an external compatible bundle, including one with native
components they have built themselves. The original Flowseal ZIP is embedded
without modification; its SHA-256 and binary inventory are in
`assets/bundle/NOTICE.md` (`UPSTREAM-NOTICE.md` in distribution).

## Design acknowledgement

The compact vertical layout, large connection circle, bottom navigation and
sliding selection panel are visually inspired by **AmneziaVPN**, by Amnezia Team
and contributors: https://github.com/amnezia-vpn/amnezia-client . That client is
licensed under GPL-3.0. Its source code, logo and graphic files are not included
in this GUI. The WPF implementation and drawn icons are part of this project's
own source code.

## External runtime and services

Windows, .NET Framework and the system curl are prerequisites, not redistributed
runtime components of this package. Microsoft and curl maintain their own terms.
Cloudflare, Google, Quad9, AdGuard and OpenDNS identify optional external DNS
services; no DNS-provider software or logo is bundled.

No affiliation with or endorsement by Flowseal, bol-van, Amnezia, Paratype,
WinDivert, Cygwin or the DNS providers is claimed.
