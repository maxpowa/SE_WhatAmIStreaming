# What Am I Streaming?

Replaces the vague "Streaming..." loading wheel with a live count of exactly what's loading in — voxels, small grids, static grids, and large grids.

## What it does

Instead of just:

> Streaming...

you get a live, per-category breakdown of the entities currently streaming into the world, e.g.:

> Streaming (2x grid, 1x voxel, 2x small grid, 1x large grid)

- **grid** — a grid that is still in flight (its entity hasn't been constructed yet, so its size is unknown)
- **voxel** — terrain voxel data
- **small grid** / **large grid** — cube grids by size class
- **static grid** — a static grid/station

Only non-zero categories are shown, and the text updates in real time as the stream progresses.

## Bugs & feedback

Report issues on the [GitHub issue tracker](https://github.com/maxpowa/SE_WhatAmIStreaming/issues).
