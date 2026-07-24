#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;

public enum VolumeShapeType
{
    Sphere,
    Box,
    Torus,
    Hyperboloid,
    CustomAsset
}

public enum VolumeOperationRole
{
    Add,
    Subtract,
    Intersect
}

public enum VolumeGridType
{
    None,
    Global,
    Sphere,
    Torus,
    Hyperboloid
}

[ExecuteAlways]
public class VolumeObject : MonoBehaviour
{
#if UNITY_EDITOR
    private bool _rebuildQueued;
    private double _lastTransformChangeTime;

    private Vector3 _lastLocalPosition;
    private Quaternion _lastLocalRotation;
    private Vector3 _lastLocalScale;
#endif

    [Header("Object")]
    public VolumeShapeType shapeType = VolumeShapeType.Sphere;
    public VolumeOperationRole role = VolumeOperationRole.Add;

    [Header("Custom")]
    public SDF customAsset;

    [Header("Sphere")]
    public float sphereRadius = 1f;

    [Header("Box")]
    public Vector3 boxHalfExtents = Vector3.one * 0.5f;

    [Header("Torus")]
    public float torusMajorRadius = 1f;
    public float torusMinorRadius = 0.25f;

    [Header("Hyperboloid")]
    public float hyperboloidA = 1f;
    public float hyperboloidB = 1f;
    public float hyperboloidC = 1f;

    [Header("Surface Grid / Cutter")]
    public VolumeGridType gridType = VolumeGridType.None;

    public float gridWidth = 0.02f;
    public float gridDepth = 0.04f;
    public bool autoClampGridToSampling = true;

    public Vector3 gridSpacing = new Vector3(0.4f, 0.4f, 0.4f);
    public Vector3 gridOffset = Vector3.zero;
    public bool globalGridInWorldSpace = false;

    public int longitudeCount = 16;
    public int latitudeCount = 8;

    public int torusMajorSegments = 24;
    public int torusMinorSegments = 12;

    public int hyperboloidRadialSegments = 24;
    public int hyperboloidHeightSegments = 12;
    public float hyperboloidHeightMin = -2f;
    public float hyperboloidHeightMax = 2f;
    public bool useXLines = true;
    public bool useYLines = true;
    public bool useZLines = true;

#if UNITY_EDITOR
    /// <summary>Stores the initial transform state for editor change detection.</summary>
    private void OnEnable()
    {
        CacheLocalTransform();
    }
#endif

    /// <summary>Updates editor metadata after inspector changes.</summary>
    private void OnValidate()
    {
#if UNITY_EDITOR
        UpdateGameObjectName();
        CacheLocalTransform();

        // Kein QueueComposerRebuild hier:
        // OnValidate feuert auch bei Parent-/Inspector-Änderungen.
        // Rebuild läuft über lokalen Transform-Check oder VolumeProcessorEditor.
#endif
    }

#if UNITY_EDITOR
    /// <summary>Watches local transform changes in edit mode and queues rebuilds.</summary>
    private void Update()
    {
        if (Application.isPlaying)
            return;

        if (!LocalTransformChanged())
            return;

        Bounds previousBounds = GetEstimatedLocalBoundsForTransform(
            _lastLocalPosition,
            _lastLocalRotation,
            _lastLocalScale
        );

        CacheLocalTransform();

        Bounds currentBounds = GetEstimatedLocalBounds();

        VolumeProcessor model = GetComponentInParent<VolumeProcessor>();
        if (model == null || !model.ShouldAutoRebuildOnTransformChange())
            return;

        model.NotifyInteractiveEdit();
        QueueComposerRebuild(previousBounds, currentBounds);
    }

    /// <summary>Caches the current local transform values.</summary>
    private void CacheLocalTransform()
    {
        _lastLocalPosition = transform.localPosition;
        _lastLocalRotation = transform.localRotation;
        _lastLocalScale = transform.localScale;
    }

    /// <summary>Checks whether the local transform changed since the last cache.</summary>
    private bool LocalTransformChanged()
    {
        return _lastLocalPosition != transform.localPosition ||
               _lastLocalRotation != transform.localRotation ||
               _lastLocalScale != transform.localScale;
    }

    /// <summary>Queues a delayed composition rebuild in the editor.</summary>
    private void QueueComposerRebuild(Bounds dirtyBounds)
    {
        if (_rebuildQueued)
        {
            _queuedDirtyBounds.Encapsulate(dirtyBounds);
            _queuedDirtyBoundsParts.Add(dirtyBounds);
        }
        else
        {
            _rebuildQueued = true;
            _queuedDirtyBounds = dirtyBounds;
            _queuedDirtyBoundsParts.Clear();
            _queuedDirtyBoundsParts.Add(dirtyBounds);
            EditorApplication.delayCall += DelayedComposerRebuild;
        }

        _lastTransformChangeTime = EditorApplication.timeSinceStartup;
    }

    private void QueueComposerRebuild(Bounds firstDirtyBounds, Bounds secondDirtyBounds)
    {
        QueueComposerRebuild(firstDirtyBounds);
        QueueComposerRebuild(secondDirtyBounds);
    }

    /// <summary>Runs the queued editor rebuild if this object still exists.</summary>
    private void DelayedComposerRebuild()
    {
        if (this == null)
            return;

        VolumeProcessor model = GetComponentInParent<VolumeProcessor>();

        if (model != null && !model.ShouldAutoRebuildOnTransformChange())
        {
            _rebuildQueued = false;
            return;
        }

        if (model != null)
        {
           bool previewEnabled =
                model.ShouldUseInteractionPreview() &&
                ((model.SupportsPreviewDepth() && model.usePreviewDepthWhileInteracting > 0) ||
                  (model.SupportsPreviewResolution() && model.usePreviewResolutionWhileInteracting != Vector3Int.zero));
            bool previewActive = previewEnabled && model.IsPreviewInteractionActive;
            bool isPointerOrHandleActive = IsEditorHandleActive();

            if (model.rebuildOnMoveRelease &&
                isPointerOrHandleActive &&
                !(previewEnabled && previewActive))
            {
                EditorApplication.delayCall += DelayedComposerRebuild;
                RequestEditorRebuildTick();
                return;
            }

            // Without preview: always wait for release-like pause.
            // With preview: allow live low-res updates during interaction.
            bool shouldWaitForRelease = !previewActive && (!previewEnabled || model.rebuildOnMoveRelease);

            double elapsed = EditorApplication.timeSinceStartup - _lastTransformChangeTime;

            if (shouldWaitForRelease && elapsed < model.moveReleaseDelaySeconds)
            {
                EditorApplication.delayCall += DelayedComposerRebuild;
                RequestEditorRebuildTick();
                return;
            }
        }

        _rebuildQueued = false;

        VolumeObjectRegistry composer = GetComponentInParent<VolumeObjectRegistry>();

        if (composer != null)
        {
            composer.MarkDirtyAndRebuild(_queuedDirtyBounds, _queuedDirtyBoundsParts);
            _queuedDirtyBoundsParts.Clear();
            model?.DrainPendingRenderChunksImmediately();
            RequestEditorRebuildTick();
        }
    }

    private static void RequestEditorRebuildTick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }

    private Bounds _queuedDirtyBounds;
    private readonly System.Collections.Generic.List<Bounds> _queuedDirtyBoundsParts = new();

    /// <summary>Renames the GameObject from its shape, role, and grid mode.</summary>
    private void UpdateGameObjectName()
    {
        string shapeName = shapeType.ToString();
        string roleName = role.ToString();

        string gridName = gridType != VolumeGridType.None
            ? $"_{gridType}Grid"
            : "";

        string newName = $"VolumeObject_{shapeName}_{roleName}{gridName}";

        if (gameObject.name != newName)
            gameObject.name = newName;
    }
#endif

    /// <summary>Samples this object's local SDF including optional grid cutters.</summary>
     public float EvaluateLocal(Vector3 p) => EvaluateLocal(p.x, p.y, p.z);

     /// <summary>Zero-allocation scalar overload — avoids Vector3 construction in hot loops.</summary>
     public float EvaluateLocal(float px, float py, float pz)
     {
         float d = EvaluateShapeScalar(px, py, pz);

         if (gridType != VolumeGridType.None)
         {
             float cutter = EvaluateGridCutterScalar(px, py, pz, d);
             d = Mathf.Max(d, -cutter);
         }

         return d;
     }

     /// <summary>Samples the base primitive or custom SDF in local space.</summary>
     private float EvaluateShape(Vector3 p) => EvaluateShapeScalar(p.x, p.y, p.z);

     private float EvaluateShapeScalar(float px, float py, float pz)
     {
         switch (shapeType)
         {
             case VolumeShapeType.Box:
                 return BoxScalar(px, py, pz, boxHalfExtents.x, boxHalfExtents.y, boxHalfExtents.z);

             case VolumeShapeType.Torus:
             {
                 float radial = Mathf.Sqrt(px * px + pz * pz) - torusMajorRadius;
                 return Mathf.Sqrt(radial * radial + py * py) - torusMinorRadius;
             }

             case VolumeShapeType.Hyperboloid:
             {
                 float a = Mathf.Max(0.0001f, hyperboloidA);
                 float b = Mathf.Max(0.0001f, hyperboloidB);
                 float c = Mathf.Max(0.0001f, hyperboloidC);
                 return (px * px) / (a * a) + (pz * pz) / (b * b) - (py * py) / (c * c) - 1f;
             }

             case VolumeShapeType.CustomAsset:
                 return customAsset != null ? customAsset.Evaluate(new Vector3(px, py, pz)) : 1f;

             case VolumeShapeType.Sphere:
             default:
                 return Mathf.Sqrt(px * px + py * py + pz * pz) - sphereRadius;
         }
     }

     private static float BoxScalar(float x, float y, float z, float hx, float hy, float hz)
     {
         float dx = Mathf.Abs(x) - hx;
         float dy = Mathf.Abs(y) - hy;
         float dz = Mathf.Abs(z) - hz;
         float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f), oz = Mathf.Max(dz, 0f);
         float outside = Mathf.Sqrt(ox * ox + oy * oy + oz * oz);
         float inside = Mathf.Min(Mathf.Max(dx, Mathf.Max(dy, dz)), 0f);
         return outside + inside;
     }

     /// <summary>Evaluates the active grid cutter inside the surface shell.</summary>
      private float EvaluateGridCutter(Vector3 p, float baseDistance) => EvaluateGridCutterScalar(p.x, p.y, p.z, baseDistance);

      private float EvaluateGridCutterScalar(float px, float py, float pz, float baseDistance)
      {
          GetEffectiveGridMetrics(out float width, out float depth);
          float shell = Mathf.Max(baseDistance, -baseDistance - depth);

          float gridD = gridType switch
          {
              VolumeGridType.Global => EvaluateGlobalGridScalar(px, py, pz, width),
              VolumeGridType.Sphere => EvaluateSphereGridScalar(px, py, pz, width),
              VolumeGridType.Torus => EvaluateTorusGridScalar(px, py, pz, width),
              VolumeGridType.Hyperboloid => EvaluateHyperboloidGridScalar(px, py, pz, width),
              _ => 1f
          };

          return Mathf.Max(gridD, shell);
      }

      /// <summary>Evaluates axis-aligned global grid grooves (scalar).</summary>
      private float EvaluateGlobalGridScalar(float px, float py, float pz, float width)
      {
          if (globalGridInWorldSpace)
          {
              // Transform point to world space using localToWorldMatrix columns
              Matrix4x4 m = transform.localToWorldMatrix;
              float wx = m.m00 * px + m.m01 * py + m.m02 * pz + m.m03;
              float wy = m.m10 * px + m.m11 * py + m.m12 * pz + m.m13;
              float wz = m.m20 * px + m.m21 * py + m.m22 * pz + m.m23;
              px = wx; py = wy; pz = wz;
          }

          float qx = px + gridOffset.x;
          float qy = py + gridOffset.y;
          float qz = pz + gridOffset.z;

          float d = float.PositiveInfinity;

          if (useXLines)
              d = Mathf.Min(d, Mathf.Abs(RepeatCentered(qx, gridSpacing.x)) - width);

          if (useYLines)
              d = Mathf.Min(d, Mathf.Abs(RepeatCentered(qy, gridSpacing.y)) - width);

          if (useZLines)
              d = Mathf.Min(d, Mathf.Abs(RepeatCentered(qz, gridSpacing.z)) - width);

          return d;
      }

      /// <summary>Evaluates longitude and latitude grooves on a sphere (scalar).</summary>
      private float EvaluateSphereGridScalar(float px, float py, float pz, float width)
      {
          float r = Mathf.Sqrt(px * px + py * py + pz * pz);

          if (r < 1e-6f)
              return 1f;

          float nx = px / r;
          float ny = py / r;
          float nz = pz / r;

          float theta = Mathf.Atan2(nz, nx) + gridOffset.x;
          float phi = Mathf.Acos(Mathf.Clamp(ny, -1f, 1f)) + gridOffset.y;

          int lon = Mathf.Max(1, longitudeCount);
          int lat = Mathf.Max(1, latitudeCount);

          float lonSpacing = Mathf.PI * 2f / lon;
          float latSpacing = Mathf.PI / lat;

          float lonDist = Mathf.Abs(RepeatCentered(theta, lonSpacing)) * r * Mathf.Sin(phi);
          float latDist = Mathf.Abs(RepeatCentered(phi, latSpacing)) * r;

          return Mathf.Min(lonDist, latDist) - width;
      }

      /// <summary>Evaluates major and minor grooves on a torus (scalar).</summary>
      private float EvaluateTorusGridScalar(float px, float py, float pz, float width)
      {
          float theta = Mathf.Atan2(pz, px) + gridOffset.x;

          float radial = Mathf.Sqrt(px * px + pz * pz);
          float phi = Mathf.Atan2(py, radial - torusMajorRadius) + gridOffset.y;

          int major = Mathf.Max(1, torusMajorSegments);
          int minor = Mathf.Max(1, torusMinorSegments);

          float majorSpacing = Mathf.PI * 2f / major;
          float minorSpacing = Mathf.PI * 2f / minor;

          float majorDist = Mathf.Abs(RepeatCentered(theta, majorSpacing)) * Mathf.Max(0.0001f, torusMajorRadius);
          float minorDist = Mathf.Abs(RepeatCentered(phi, minorSpacing)) * Mathf.Max(0.0001f, torusMinorRadius);

          return Mathf.Min(majorDist, minorDist) - width;
      }

      /// <summary>Evaluates radial and height grooves on a hyperboloid (scalar).</summary>
      private float EvaluateHyperboloidGridScalar(float px, float py, float pz, float width)
      {
          float safeA = Mathf.Max(0.0001f, hyperboloidA);
          float safeB = Mathf.Max(0.0001f, hyperboloidB);

          float theta = Mathf.Atan2(pz / safeB, px / safeA) + gridOffset.x;

          int radial = Mathf.Max(1, hyperboloidRadialSegments);
          int height = Mathf.Max(1, hyperboloidHeightSegments);

          float radialSpacing = Mathf.PI * 2f / radial;
          float heightSpacing = Mathf.Max(
              0.0001f,
              (hyperboloidHeightMax - hyperboloidHeightMin) / height
          );

          float rx = px / safeA;
          float rz = pz / safeB;
          float localRadius = Mathf.Sqrt(rx * rx + rz * rz);

          float angularScale = Mathf.Max(
              0.0001f,
              localRadius * Mathf.Min(safeA, safeB)
          );

          float radialDist = Mathf.Abs(RepeatCentered(theta, radialSpacing)) * angularScale;
          float heightDist = Mathf.Abs(
              RepeatCentered(py - hyperboloidHeightMin + gridOffset.y, heightSpacing)
          );

          return Mathf.Min(radialDist, heightDist) - width;
      }

    private void GetEffectiveGridMetrics(out float width, out float depth)
    {
        width = Mathf.Max(0.0001f, gridWidth);
        depth = Mathf.Max(0.0001f, gridDepth);

        if (!autoClampGridToSampling)
            return;

        float minCell = EstimateMinSamplingCellSize();

        if (minCell <= 0f)
            return;

        width = Mathf.Max(width, minCell * 0.55f);
        depth = Mathf.Max(depth, minCell * 0.75f);
    }

    private float EstimateMinSamplingCellSize()
    {
        VolumeProcessor model = GetComponentInParent<VolumeProcessor>();

        if (model == null)
            return 0f;

        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                {
                    Vector3Int size = model.voxelGridSampler.builder.gridSize;
                    Vector3 extent = model.voxelGridSampler.builder.gridExtent;

                    float cx = extent.x / Mathf.Max(1, size.x - 1);
                    float cy = extent.y / Mathf.Max(1, size.y - 1);
                    float cz = extent.z / Mathf.Max(1, size.z - 1);

                    return Mathf.Min(cx, Mathf.Min(cy, cz));
                }

            case VolumeDataStructure.Octree:
            case VolumeDataStructure.SparseVoxelOctree:
                {
                    OctreeVolumeBuilder builder = model.dataStructure == VolumeDataStructure.Octree
                        ? model.octreeSampler.builder
                        : model.sparseVoxelOctreeSampler.builder.backend;
                    int resolution = 1 << Mathf.Max(0, builder.maxDepth);
                    Vector3 cell = builder.size / Mathf.Max(1, resolution);

                    return Mathf.Min(cell.x, Mathf.Min(cell.y, cell.z));
                }

            default:
                return 0f;
        }
    }

    /// <summary>Repeats a coordinate around zero with the given spacing.</summary>
    private static float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        return v - spacing * Mathf.Floor(v / spacing + 0.5f);
    }

    /// <summary>Returns the component-wise absolute value.</summary>
    private static Vector3 Abs(Vector3 v)
    {
        return new Vector3(
            Mathf.Abs(v.x),
            Mathf.Abs(v.y),
            Mathf.Abs(v.z)
        );
    }

    /// <summary>Evaluates an axis-aligned box SDF.</summary>
    private static float Box(Vector3 p, Vector3 halfExtents)
    {
        Vector3 q = Abs(p) - halfExtents;

        return Vector3.Max(q, Vector3.zero).magnitude +
               Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
    }

    /// <summary>Draws a scene-view outline for this volume object.</summary>
    private void DrawVolumeGizmo(bool selected)
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;

        float alpha = selected ? 1f : 0.45f;

        Gizmos.color = role switch
        {
            VolumeOperationRole.Add => new Color(0f, 1f, 0f, alpha),
            VolumeOperationRole.Subtract => new Color(1f, 0f, 0f, alpha),
            VolumeOperationRole.Intersect => new Color(0f, 0.5f, 1f, alpha),
            _ => Color.white
        };

        switch (shapeType)
        {
            case VolumeShapeType.Sphere:
                Gizmos.DrawWireSphere(Vector3.zero, sphereRadius);
                break;

            case VolumeShapeType.Box:
                Gizmos.DrawWireCube(Vector3.zero, boxHalfExtents * 2f);
                break;

            case VolumeShapeType.Torus:
                DrawTorusGizmo(torusMajorRadius, torusMinorRadius);
                break;

            case VolumeShapeType.Hyperboloid:
                Gizmos.DrawWireCube(
                    Vector3.zero,
                    new Vector3(
                        hyperboloidA * 2f,
                        Mathf.Abs(hyperboloidHeightMax - hyperboloidHeightMin),
                        hyperboloidB * 2f
                    )
                );
                break;
        }

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

    /// <summary>Draws a simple torus outline for the scene-view gizmo.</summary>
    private void DrawTorusGizmo(float majorRadius, float minorRadius)
    {
        const int segments = 64;

        Vector3 prevOuter = Vector3.zero;
        Vector3 prevInner = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments * Mathf.PI * 2f;

            Vector3 outer = new Vector3(
                Mathf.Cos(t) * (majorRadius + minorRadius),
                0f,
                Mathf.Sin(t) * (majorRadius + minorRadius)
            );

            Vector3 inner = new Vector3(
                Mathf.Cos(t) * Mathf.Max(0f, majorRadius - minorRadius),
                0f,
                Mathf.Sin(t) * Mathf.Max(0f, majorRadius - minorRadius)
            );

            if (i > 0)
            {
                Gizmos.DrawLine(prevOuter, outer);
                Gizmos.DrawLine(prevInner, inner);
            }

            prevOuter = outer;
            prevInner = inner;
        }
    }

    /// <summary>Draws the object gizmo when child gizmos are enabled.</summary>
    private void OnDrawGizmos()
    {
        if (!ShouldDrawGizmos())
            return;

        DrawVolumeGizmo(false);
    }

    /// <summary>Draws the selected object gizmo when child gizmos are enabled.</summary>
    private void OnDrawGizmosSelected()
    {
        if (!ShouldDrawGizmos())
            return;

        DrawVolumeGizmo(true);
    }

    /// <summary>Checks the parent model setting that controls child gizmos.</summary>
    private bool ShouldDrawGizmos()
    {
        VolumeProcessor model = GetComponentInParent<VolumeProcessor>();

        if (model == null)
            return true;

        return model.drawChildGizmos;
    }

    private Bounds GetEstimatedLocalBounds()
    {
        return GetEstimatedLocalBoundsForTransform(
            transform.localPosition,
            transform.localRotation,
            transform.localScale
        );
    }

  /// <summary>Returns estimated world-space bounds of this volume object (zero-alloc OBB→AABB).</summary>
   public Bounds GetBounds()
   {
       Bounds local = GetEstimatedLocalBounds();

       // OBB→AABB via matrix math: |M_rows| · halfExtents. No corner array allocation.
       Matrix4x4 m = transform.localToWorldMatrix;
       Vector3 center = new Vector3(
           m.m00 * local.center.x + m.m01 * local.center.y + m.m02 * local.center.z + m.m03,
           m.m10 * local.center.x + m.m11 * local.center.y + m.m12 * local.center.z + m.m13,
           m.m20 * local.center.x + m.m21 * local.center.y + m.m22 * local.center.z + m.m23
       );

       Vector3 half = local.extents;
       float hx = Mathf.Abs(m.m00) * half.x + Mathf.Abs(m.m01) * half.y + Mathf.Abs(m.m02) * half.z;
       float hy = Mathf.Abs(m.m10) * half.x + Mathf.Abs(m.m11) * half.y + Mathf.Abs(m.m12) * half.z;
       float hz = Mathf.Abs(m.m20) * half.x + Mathf.Abs(m.m21) * half.y + Mathf.Abs(m.m22) * half.z;

       return new Bounds(center, new Vector3(hx * 2f, hy * 2f, hz * 2f));
   }

    public Bounds EstimateLocalMoveDirtyBounds(Vector3 fromLocalPosition, Vector3 toLocalPosition)
    {
        EstimateLocalMoveDirtyBoundsParts(fromLocalPosition, toLocalPosition, out Bounds dirtyBounds, out Bounds toBounds);
        dirtyBounds.Encapsulate(toBounds);

        return dirtyBounds;
    }

    public void EstimateLocalMoveDirtyBoundsParts(
        Vector3 fromLocalPosition,
        Vector3 toLocalPosition,
        out Bounds fromBounds,
        out Bounds toBounds)
    {
        fromBounds = GetEstimatedLocalBoundsForTransform(
            fromLocalPosition,
            transform.localRotation,
            transform.localScale
        );
        toBounds = GetEstimatedLocalBoundsForTransform(
            toLocalPosition,
            transform.localRotation,
            transform.localScale
        );
    }

#if UNITY_EDITOR
    public void SyncEditorTransformCache()
    {
        CacheLocalTransform();
    }
#endif

    private Bounds GetEstimatedLocalBoundsForTransform(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
     {
         Vector3 halfExtents = GetApproximateShapeHalfExtents();

         // Apply scale then rotation to get AABB of the rotated shape.
         // TransformBoundsToWorld() corner-transforms this to world space —
         // with identity parent rotation (common case), this is exact.
         // With non-identity parent rotation, it overestimates slightly (still safe).
         Vector3 scaled = new Vector3(
             Mathf.Abs(halfExtents.x * localScale.x),
             Mathf.Abs(halfExtents.y * localScale.y),
             Mathf.Abs(halfExtents.z * localScale.z)
         );

         Matrix4x4 r = Matrix4x4.Rotate(localRotation);
         Vector3 rotatedHalf = new Vector3(
             Mathf.Abs(r.m00) * scaled.x + Mathf.Abs(r.m01) * scaled.y + Mathf.Abs(r.m02) * scaled.z,
             Mathf.Abs(r.m10) * scaled.x + Mathf.Abs(r.m11) * scaled.y + Mathf.Abs(r.m12) * scaled.z,
             Mathf.Abs(r.m20) * scaled.x + Mathf.Abs(r.m21) * scaled.y + Mathf.Abs(r.m22) * scaled.z
         );

         return new Bounds(localPosition, rotatedHalf * 2f);
     }

    private Vector3 GetApproximateShapeHalfExtents()
    {
        switch (shapeType)
        {
            case VolumeShapeType.Box:
                return new Vector3(
                    Mathf.Abs(boxHalfExtents.x),
                    Mathf.Abs(boxHalfExtents.y),
                    Mathf.Abs(boxHalfExtents.z)
                );

            case VolumeShapeType.Torus:
                float torusR = Mathf.Abs(torusMajorRadius) + Mathf.Abs(torusMinorRadius);
                float torusY = Mathf.Abs(torusMinorRadius);
                return new Vector3(torusR, torusY, torusR);

            case VolumeShapeType.Hyperboloid:
            {
                // Hyperboloid: x²/a² + z²/b² - y²/c² = 1
                // At height y, surface radius is sqrt(1 + y²/c²) · (a, b).
                // Use max |y| to get tight-but-safe bounds.
                float maxH = Mathf.Max(Mathf.Abs(hyperboloidHeightMin), Mathf.Abs(hyperboloidHeightMax));
                float safeC = Mathf.Max(0.0001f, Mathf.Abs(hyperboloidC));
                float scale = Mathf.Sqrt(1f + (maxH * maxH) / (safeC * safeC));
                return new Vector3(
                    Mathf.Abs(hyperboloidA) * scale,
                    maxH,
                    Mathf.Abs(hyperboloidB) * scale
                );
            }

            case VolumeShapeType.CustomAsset:
                return Vector3.one * 2f;

            case VolumeShapeType.Sphere:
            default:
                float r = Mathf.Abs(sphereRadius);
                return new Vector3(r, r, r);
        }
    }

#if UNITY_EDITOR
    private static bool IsEditorHandleActive()
    {
        return GUIUtility.hotControl != 0;
    }
#endif
}
