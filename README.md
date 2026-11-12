# ecomachinaallocfix 1.0.0

Client-side Vintage Story compat mod for Eco Machina (`vsecomachina`, 0.6.x).

## What it fixes
Eco Machina's render-only tapered wood patches reallocate whole mesh pool buffers on every insertion, and the custom-data align helpers size a placeholder to the whole pool instead of the inserted mesh. This produces a constant stream of dead large Int32[] buffers on the large object heap, which only a gen2 GC reclaims.

## How it patches
Harmony prefixes (no transpilers) on 5 Eco Machina mesh-append helpers:
`EnsureTargetAppendCapacity` (pool buffers now grow geometrically) and `TryAlignCustomInts/Floats/Shorts/Bytes` (placeholder sized to the inserted mesh). Behavior is otherwise unchanged.

Expected log line on load:

    [EcoMachinaAllocFix] patched 5/5 Eco Machina mesh-append helpers.

## Install
Build and zip: `dotnet build -c Release` (set `VintagestoryPath` if the game is not in `%APPDATA%\Vintagestory`), then zip the build output (`EcoMachinaAllocFix.dll` + `modinfo.json`) into `ecomachinaallocfix_1.0.0.zip` and place it in the game's Mods folder. No binaries are shipped in this repo.

## Measured result
Int32[] LOH allocation rate with Eco Machina active: about 117 MB/s unpatched (28.2 GB in 241 s) vs about 6.3 MB/s patched (about -95%; Eco Machina off reference about 1.6 MB/s, one-time TextureAtlas allocation excluded).

## Credit
This mod patches the third-party Eco Machina mod at runtime and does not redistribute any of its code or assets.
