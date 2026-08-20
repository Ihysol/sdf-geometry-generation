#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// ARD-5 smoke test: verifies the ARD Octree pipeline and Anchored Cell Size in the Editor.
/// Menu: Volume System/ARD Smoke Test
///
/// Phase 1 — Octree: fresh VolumeProcessor in Octree mode, add sphere, rebuild,
///               verify OctreeMeshOutput has a valid mesh.
/// Phase 2 — Anchored Cell Size: fresh VolumeProcessor in VoxelGrid mode (128³ @ extent 4,
///               cellSize 0.03125). Add distant sphere -> auto-expand must keep cellSize
///               constant while resolution scales (formula), then beyond maxResolutionCap
///               (cap applied).
/// </summary>
public static class ArDOctreeSmokeTest
{
    static int _pass;
    static int _fail;

    [MenuItem("Volume System/ARD Smoke Test")]
    public static void Run()
    {
        _pass = 0; _fail = 0;
        Debug.Log("========== ARD-5 SMOKE TEST START ==========");

        try
        {
            Phase1_Octree();
            Phase2_AnchoredCellSize();
        }
        catch (System.Exception e)
        {
            _fail++;
            Debug.LogError($"[SmokeTest] EXCEPTION: {e}");
        }

        Debug.Log($"========== ARD-5 SMOKE TEST END: {_pass} passed, {_fail} failed ==========");
        if (_fail > 0)
            Debug.LogError("SMOKE TEST FAILED — see [SmokeTest] entries above.");
        else
            Debug.Log("SMOKE TEST PASSED — all checks green.");
    }

    // ------------------------------------------------------------------
    // Phase 1: ARD Octree pipeline
    // ------------------------------------------------------------------
    static void Phase1_Octree()
    {
        Debug.Log("[SmokeTest] --- Phase 1: Octree ---");
        GameObject root = new GameObject("[SmokeTest] Octree");
        VolumeProcessor vp = root.AddComponent<VolumeProcessor>();
        try
        {
            vp.dataStructure = VolumeDataStructure.Octree;
            vp.octreeMaxDepth = 6;
            vp.octreeMinDepth = 3;

            vp.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
            Check("Sphere object added", CountObjects(vp) == 1);

            // AddObject already triggers a full rebuild via the command stack;
            // call RebuildModel again to be explicit (idempotent).
            vp.RebuildModel();

            Transform octT = root.transform.Find("VisualOutput/OctreeMeshOutput");
            Check("OctreeMeshOutput exists", octT != null);
            if (octT != null)
            {
                MeshFilter mf = octT.GetComponent<MeshFilter>();
                Check("MeshFilter with mesh", mf != null && mf.sharedMesh != null);
                if (mf != null && mf.sharedMesh != null)
                {
                    Mesh m = mf.sharedMesh;
                    Debug.Log($"[SmokeTest] Octree mesh: {m.vertexCount} verts, {m.triangles.Length / 3} tris, size={m.bounds.size}, center={m.bounds.center}");
                    Check("Mesh has >100 vertices", m.vertexCount > 100);
                    Check("Mesh has >100 triangles", m.triangles.Length / 3 > 100);

                    Vector3 s = m.bounds.size;
                    float aspect = Mathf.Max(s.x, Mathf.Max(s.y, s.z)) / Mathf.Max(0.0001f, Mathf.Min(s.x, Mathf.Min(s.y, s.z)));
                    Check($"Mesh roughly spherical (aspect {aspect:F2} < 1.3)", aspect < 1.3f);
                    Check($"Mesh center near origin ({m.bounds.center.magnitude:F2} < 2)", m.bounds.center.magnitude < 2f);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------
    // Phase 2: Anchored Cell Size (flat grid)
    // ------------------------------------------------------------------
    static void Phase2_AnchoredCellSize()
    {
        Debug.Log("[SmokeTest] --- Phase 2: Anchored Cell Size ---");
        GameObject root = new GameObject("[SmokeTest] Anchored");
        VolumeProcessor vp = root.AddComponent<VolumeProcessor>();
        try
        {
            vp.dataStructure = VolumeDataStructure.VoxelGrid;
            vp.resolution = new Vector3Int(128, 128, 128);
            vp.boundsExtent = 4f;
            vp.autoExpand = true;
            vp.expandPaddingFactor = 1.25f;
            vp.maxResolutionCap = 512;

            float cell0 = 4f / 128f; // 0.03125

            // Initial sphere — fits in grid, no resize
            vp.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);

            VolumeLayout layout0 = GetLayout(vp);
            Check("Flat pipeline initialized", layout0.Resolution != Vector3Int.zero);
            Check($"Initial resolution 128³ (got {layout0.Resolution})", layout0.Resolution == new Vector3Int(128, 128, 128));
            Check($"Initial cellSize {cell0:F5} (got {layout0.CellSize:F5})", Mathf.Approximately(layout0.CellSize, cell0));

            // Distant sphere at (5,0,0): total bounds size (7,2,2) center (2.5,0,0)
            // padded extent = 7*1.25 = 8.75 -> expected res = ceil(8.75/0.03125) = 280
            AddDistantSphere(vp, new Vector3(5f, 0f, 0f));
            vp.RebuildModel();

            VolumeLayout layout1 = GetLayout(vp);
            Check($"CellSize unchanged after expand (still {cell0:F5}, got {layout1.CellSize:F5})",
                Mathf.Approximately(layout1.CellSize, cell0));
            Check($"Resolution scaled to 280 (got {layout1.Resolution.x})", layout1.Resolution.x == 280);
            Check($"Grid now covers distant sphere (origin {layout1.Origin.x:F2}, size {layout1.Resolution.x * layout1.CellSize:F2})",
                layout1.Origin.x <= 4f - 0.01f && layout1.Origin.x + layout1.Resolution.x * layout1.CellSize >= 6f + 0.01f);

            // Even further sphere at (20,0,0): extent 27.5 -> raw res 880 > cap 512
            AddDistantSphere(vp, new Vector3(20f, 0f, 0f));
            vp.RebuildModel();

            VolumeLayout layout2 = GetLayout(vp);
            Check($"CellSize still unchanged (got {layout2.CellSize:F5})", Mathf.Approximately(layout2.CellSize, cell0));
            Check($"Resolution capped at 512 (got {layout2.Resolution.x})", layout2.Resolution.x == 512);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------
    static void AddDistantSphere(VolumeProcessor vp, Vector3 pos)
    {
        GameObject child = new GameObject($"DistantSphere_{pos.x}");
        child.transform.SetParent(vp.transform, false);
        child.transform.localPosition = pos;
        VolumeObject vo = child.AddComponent<VolumeObject>();
        vo.shapeType = VolumeShapeType.Sphere;
        vo.role = VolumeOperationRole.Add;
        VolumeObjectRegistry composer = vp.GetComponent<VolumeObjectRegistry>();
        composer.objects.Add(vo);
        composer.RebuildComposition();
    }

    static int CountObjects(VolumeProcessor vp)
    {
        VolumeObjectRegistry c = vp.GetComponent<VolumeObjectRegistry>();
        return c?.objects.Count ?? -1;
    }

    /// <summary>Read the current flat-grid layout via the public pipeline API
    /// (only _pipeline itself is private — one reflection hop).</summary>
    static VolumeLayout GetLayout(VolumeProcessor vp)
    {
        System.Reflection.FieldInfo fi = vp.GetType().GetField("_pipeline",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        VolumePipeline pipeline = (VolumePipeline)fi?.GetValue(vp);
        if (pipeline == null)
            throw new System.InvalidOperationException("_pipeline is null — flat pipeline not initialized");
        return pipeline.Buffer.Layout;
    }

    static void Check(string label, bool cond)
    {
        if (cond)
        {
            _pass++;
            Debug.Log($"[SmokeTest] PASS: {label}");
        }
        else
        {
            _fail++;
            Debug.LogError($"[SmokeTest] FAIL: {label}");
        }
    }
}
#endif
