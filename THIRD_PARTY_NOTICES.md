# Third-party materials

The Apache-2.0 license covers this project's original code. It does not relicense
game artwork, trademarks, the map artwork, or the game save encryption key.

## Game artwork

`src/SaveEditor.App/Assets/Game/` contains character portraits, weapon pictures,
mission and checklist thumbnails, contact pictures, and collectible markers from
Grand Theft Auto V. Grand Theft Auto and GTA are trademarks of their respective
owners; Rockstar Games and Take-Two Interactive are credited in the app. This
project is not affiliated with them.

## Runtime and development dependencies

The release EXE bundles the Microsoft .NET runtime (MIT license; see
https://github.com/dotnet/runtime/blob/main/LICENSE.TXT). Development and test
dependencies are listed with their resolved versions in the committed
`packages.lock.json` files. They are not shipped in the app's release ZIP.

## Save keys

Player save files used for research are not distributed in this repository or its
release ZIP. A maintainer release may embed a save encryption key using the
`GTAV_SAVE_KEY` Actions secret. The key is not part of the Apache-2.0 license, and
embedding it in an executable makes it extractable; that repository secret is a
build input, not a promise of secrecy inside the published app. Builds made
without it use the app's key-import flow.
