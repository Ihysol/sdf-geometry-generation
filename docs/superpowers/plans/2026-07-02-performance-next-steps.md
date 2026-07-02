# Performance Next Steps

## Baseline

Latest benchmark source: `Logs/volume-benchmark-latest.log`, updated 2026-07-02 08:02.

The clustered chunk-local path is active and not falling back:

- `avgLocalChunks=13.2`
- `avgClusters=1.0`
- `avgClusterBuilds=1.0`
- `avgFallbacks=0.0`
- `volumeBuild` is effectively zero because the renderer is doing chunk-local builds.

Current cost distribution:

- `chunkLocalBuild` is the dominant cost, with median values around 28-36 ms.
- `chunkMeshBuild` is secondary, usually around 6-10 ms median but with visible spikes.
- `chunkApplyMesh` is negligible, around 0.2-0.3 ms.

## Recommended Order

1. Bound clustered local builds.

   Keep clustered chunk-local rendering, but prevent a connected dirty region from always becoming one large build. Add a cluster size or build-bounds threshold that splits oversized connected clusters into smaller groups.

   Goal: reduce `chunkLocalBuild` median and p95 without increasing `avgClusterBuilds` enough to lose the clustering win.

2. Add cluster diagnostics before tuning.

   Log enough information to explain each cluster build:

   - number of chunks in each cluster
   - cluster build bounds size
   - per-cluster local build time
   - per-cluster mesh build time
   - fallback count

   If node/cell counts are available from the flat builder, include them too.

3. Tune the split threshold from benchmark data.

   Run the same Dirty Move Benchmark with at least these candidates:

   - current behavior: unlimited connected cluster
   - max cluster size around 4 chunks
   - max cluster size around 8 chunks
   - bounds-size threshold based on chunk size

   Compare `rendererChunk`, `chunkLocalBuild`, `chunkMeshBuild`, p95, and max.

4. Address mesh build spikes after local build is stable.

   If `chunkLocalBuild` improves and `chunkMeshBuild` becomes the next visible cost, investigate mesh generation caching or per-frame mesh-build budgeting. Do not optimize `ApplyMesh` first; the current logs show it is not material.

## Validation

Use the same Dirty Move Benchmark scenario so results are comparable:

- `DirtyMoveSweep`
- `VolumeObject_02_Sphere_Add`
- `logicalRuns=10`
- `samples=20`
- visual and non-visual runs
- offset `(0.00,0.00,-1.00)`
- `refinementSteps=3`

Success criteria:

- `avgFallbacks` remains `0.0`.
- `rendererChunk` median and p95 improve versus the 2026-07-02 baseline.
- `chunkLocalBuild` p95 improves without unacceptable `chunkMeshBuild` regression.
- Visual and non-visual runs both remain stable.
