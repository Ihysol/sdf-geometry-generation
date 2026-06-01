# Volume Model Rebuild Mode Design

## Goal

Replace the overlapping rebuild toggles with one `VolumeModel` dropdown that makes
editor rebuild behavior explicit. Entering Play Mode must not trigger an automatic
rebuild; the mesh already generated in the editor remains available.

## Rebuild Modes

Add a serialized `VolumeRebuildMode` enum with these values:

- `PreviewAndOnChange`: default. Preserve the current editor behavior. Interactive
  transform edits may render reduced-detail previews, then rebuild at final detail
  after interaction ends. Inspector changes rebuild automatically.
- `OnChange`: rebuild automatically after changes, but do not render intermediate
  previews while an object is being moved. Transform edits rebuild after the
  release-like pause at final detail. Inspector changes rebuild automatically.
- `EveryFrame`: call `RebuildModel()` from `VolumeModel.Update()` every frame. This
  is the live remeshing mode.
- `Manual`: do not rebuild for inspector edits, transform changes, or frames.
  Explicit actions such as the Rebuild Model button may still rebuild.

The enum replaces `autoRebuildOnChange` and `rebuildEveryFrame`. Existing serialized
assets that do not yet contain the enum use `PreviewAndOnChange` because it is the
first enum value and therefore the serialized default.

## Implementation Shape

`VolumeModel` owns the enum and exposes small intent methods:

- whether inspector changes rebuild automatically
- whether transform changes rebuild automatically
- whether interaction previews are enabled
- whether every-frame rebuilding is enabled

Editor scripts and `VolumeObject` use these methods rather than interpreting enum
values independently. Existing preview quality settings remain available, but only
affect rendering in `PreviewAndOnChange`.

`rebuildOnMoveRelease` and its delay remain as final-rebuild timing settings for
transform edits. They do not become rebuild modes.

## Data Flow

For inspector changes, custom editors rebuild only when the selected mode allows
automatic change rebuilds.

For transform changes, `VolumeObject` ignores edits in `Manual` and `EveryFrame`.
In `PreviewAndOnChange`, it uses the existing preview and finalization path. In
`OnChange`, it queues one final-detail rebuild after the existing release-like
delay.

For runtime updates, only `EveryFrame` calls `RebuildModel()` from `Update()`.
There is no `Start()` or `OnEnable()` rebuild hook.

## Testing

Add focused tests for the mode intent methods:

- default mode enables preview and automatic change rebuilds
- `OnChange` enables automatic change rebuilds but disables preview
- `EveryFrame` enables only frame rebuilds
- `Manual` disables implicit rebuilds

Compile the Unity project after the change. Manually verify the four inspector modes
in the editor because transform interaction timing depends on Unity editor callbacks.
