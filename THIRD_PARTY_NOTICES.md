# Third-party materials

The Apache-2.0 license covers this project's original code. It does not relicense
game artwork, trademarks, external map geometry, or the game save encryption key.

## Game artwork

`src/SaveEditor.App/Assets/Game/` contains character portraits, weapon pictures,
mission and checklist thumbnails, contact pictures, and collectible markers.
The source identifies these as GTA V game artwork. Rockstar Games and Take-Two
Interactive are credited in the app. Distribution permission and required credit
text must be recorded by the maintainer before publishing the public release.

## Map

`src/SaveEditor.App/Themes/GtaMap.xaml` contains recolored vector geometry derived
from `Maps/Main.svg` in XB36Hazard's GTA V Save Editor, as recorded in that file's
source comment. Credit: XB36Hazard's GTA V Save Editor. The original map's license,
source link, and any required permission text must be supplied by the maintainer.

## Application branding

`assets/icons/` contains the app logo and Windows icon. The maintainer must record
their origin and any required credit or distribution terms before public release.

The maintainer has requested that these visuals be retained and will provide the
necessary permission and attribution details. This document records the known
sources; it does not grant permission on behalf of third-party rights holders.

## Runtime and development dependencies

The default download uses the separately installed Microsoft .NET Desktop
Runtime; it does not bundle that runtime. Development and test dependencies are
listed with their resolved versions in the committed `packages.lock.json` files.
They are not shipped in the app's release ZIP.

## Local research inputs and save keys

The reference editor binary, decompiled game scripts, and player save files used
for research are not distributed in this repository or its release ZIP. The
format research is described in `docs/FINDINGS.md`. A maintainer release may embed
a save encryption key using the `GTAV_SAVE_KEY` Actions secret. The key is not
part of the Apache-2.0 license, and embedding it in an executable makes it
extractable; that repository secret is a build input, not a promise of secrecy
inside the published app. Builds made without it use the app's key-import flow.
