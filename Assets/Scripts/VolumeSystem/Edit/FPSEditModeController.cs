using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Playmode FPS-style edit mode — toggled with E. WASD + mouse look, scroll to edit.</summary>
public class FPSEditModeController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 2f;

    [Header("Camera")]
    public Camera cameraRef;
    public float mouseSensitivity = 2f;
    public float minLookAngle = -80f;
    public float maxLookAngle = 80f;

    [Header("Dot Cursor")]
    public float cursorMinDistance = 0.1f;
    public float cursorMaxDistance = 5f;

    [Header("Brush")]
    public float brushRadius = 0.5f;
    public float brushSizeScrollSpeed = 0.1f;

    [Header("Cell Snap")]
    public Key cellSnapToggleKey = Key.LeftAlt;

    [Header("Vertex / Face Editing")]
    public Key vertexSelectKey = Key.Q;
    public Key faceSelectKey = Key.F;

    // ---- State ----
    private bool _active;
    private float _yaw;
    private float _pitch;
    private bool _cellSnapMode;
    private VolumeProcessor _processor;
    private VolumeLayout _layout;
    private Transform _dotCursorObj;
    private MeshRenderer _dotCursorRenderer;

    // Cell editing state
    private Vector3Int? _hoveredCell;
    private HashSet<Vector3Int> _selectedCells = new();
    private List<Vector3> _selectedVertices = new();
    private bool _draggingMultiSelect;
    private float _lastGroupMoveTime;

    // Cached InputSystem references
    private Keyboard _kb;
    private Mouse _mouse;

    void Start()
    {
        if (cameraRef == null)
            cameraRef = GetComponentInChildren<Camera>();

        _processor = GameObject.FindObjectOfType<VolumeProcessor>();
        if (_processor != null)
            _layout = _processor.EditLayout;

        // Cache input devices
        _kb = Keyboard.current;
        _mouse = Mouse.current;

        // Serialized key bindings can hold legacy/out-of-range values (e.g. 308
        // from the old KeyCode era) — Keyboard[key] throws on those and would
        // break HandleEditing() every frame. Clamp them to the default bindings.
        if (!IsValidKey(cellSnapToggleKey))
            cellSnapToggleKey = Key.LeftAlt;
        if (!IsValidKey(vertexSelectKey))
            vertexSelectKey = Key.Q;
        if (!IsValidKey(faceSelectKey))
            faceSelectKey = Key.F;

        // Create dot cursor as a small sphere
        var go = new GameObject("DotCursor");
        _dotCursorObj = go.transform;
        _dotCursorObj.SetParent(cameraRef.transform, false);
        _dotCursorObj.localPosition = Vector3.forward * 2f;
        _dotCursorObj.localScale = Vector3.one * 0.02f;

        var mf = go.AddComponent<MeshFilter>();
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mf.mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
        Destroy(sphere);

        _dotCursorRenderer = go.AddComponent<MeshRenderer>();
        _dotCursorRenderer.material = new Material(Shader.Find("Standard"));
        _dotCursorRenderer.material.color = Color.green;
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        HandleModeToggle();
        if (!_active) return;

        HandleMovement();
        HandleMouseLook();
        UpdateDotCursor();
        HandleEditing();
    }

    /// <summary>
    /// True if a key is indexable by Keyboard[key]. The device key table holds 123
    /// entries (InputSystem 1.x), so only values 1..123 are valid — anything else
    /// (e.g. 308, a leftover legacy KeyCode) makes Keyboard[key] throw.
    /// </summary>
    private static bool IsValidKey(Key key)
    {
        return (int)key >= 1 && (int)key <= 123;
    }

    /// <summary>Maps an angle to signed -180..180 (Unity eulers are 0..360).</summary>
    private static float NormalizeAngle(float a)
    {
        a = a % 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    // ---------- Mode toggle ----------

    private void HandleModeToggle()
    {
        if (_kb.eKey.wasPressedThisFrame)
        {
            _active = !_active;
            Debug.Log("FPS Edit Mode: " + (_active ? "ON" : "OFF"));

            Cursor.lockState = _active ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_active;

            if (_active)
            {
                // Re-anchor the FPS rig from the camera's WORLD pose: parent carries
                // yaw only, camera child carries pitch only (the model
                // HandleMouseLook() assumes). The scene camera may carry local yaw/roll
                // (e.g. local yaw -90 here), and zeroing those would snap the view on
                // entry. Reconstructing from the world rotation keeps the view
                // identical; eulers are 0..360, so normalize to signed -180..180.
                Vector3 worldEuler = cameraRef.transform.rotation.eulerAngles;
                _yaw = NormalizeAngle(worldEuler.y);
                _pitch = Mathf.Clamp(NormalizeAngle(worldEuler.x), minLookAngle, maxLookAngle);
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                cameraRef.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }
    }

     // ---------- Movement: W/S = toward/away from where you look, A/D = strafe, Space/Z = up/down ----------

    private void HandleMovement()
    {
        float forward = (_kb.wKey.isPressed ? 1f : 0f) - (_kb.sKey.isPressed ? 1f : 0f);
        float strafe = (_kb.dKey.isPressed ? 1f : 0f) - (_kb.aKey.isPressed ? 1f : 0f);
        float vertical = (_kb.spaceKey.isPressed ? 1f : 0f) - (_kb.zKey.isPressed ? 1f : 0f);

        // Full flight: both forward and strafe follow camera look direction (pitch + yaw)
        Quaternion camRot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 camForward = camRot * Vector3.forward;
        Vector3 camRight = camRot * Vector3.right;

        Vector3 direction = camForward * forward + camRight * strafe + Vector3.up * vertical;

        if (direction.magnitude > 0f) direction.Normalize();

        float speed = moveSpeed * (_kb.leftShiftKey.isPressed ? sprintMultiplier : 1f);
        transform.Translate(direction * speed * Time.deltaTime, Space.World);
    }

    // ---------- Mouse look: yaw on parent, pitch on camera child ----------

    private void HandleMouseLook()
    {
        Vector2 mouseDelta = _mouse.delta.ReadValue();

        _yaw += mouseDelta.x * mouseSensitivity;
        _pitch -= mouseDelta.y * mouseSensitivity;
        _pitch = Mathf.Clamp(_pitch, minLookAngle, maxLookAngle);

        // Yaw on the parent so movement (transform.Translate) follows camera direction
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        // Pitch only on the camera child
        cameraRef.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    // ---------- Dot cursor positioning — steps ray through grid ----------

    private void UpdateDotCursor()
    {
        if (_processor == null) return;

        _layout = _processor.EditLayout;
        if (_layout.CellSize <= 0f) return; // Flat pipeline not initialized yet

        Ray ray = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));

        // Step along the ray to find: (1) first solid cell, (2) nearest grid cell in range
        float step = _layout.CellSize * 0.5f;
        float dist = cursorMinDistance;
        Vector3Int? hitCell = null;       // First solid surface cell
        float hitDist = 0f;
        Vector3Int? nearestGridCell = null; // Closest cell in grid (any density)
        float nearestDist = cursorMaxDistance + 1f;

        while (dist <= cursorMaxDistance)
        {
            Vector3 point = ray.GetPoint(dist);
            Vector3Int idx = _layout.WorldToIndex(point);

            if (_layout.IsInside(idx))
            {
                // Track nearest grid cell (for block selection even in empty space)
                if (dist < nearestDist)
                {
                    nearestGridCell = idx;
                    nearestDist = dist;
                }

                // Mode-agnostic solid check: buffer density (flat) or SDF composition (octree)
                if (_processor.SampleDensity(point) < _layout.IsoLevel)
                {
                    hitCell = idx;
                    hitDist = dist;
                    break; // Found surface — stop stepping
                }

                // Step to next cell boundary for efficiency
                Vector3 frac = (_layout.WorldToCell(point) - (Vector3)idx);
                float maxFrac = Mathf.Max(frac.x, Mathf.Max(frac.y, frac.z));
                dist += maxFrac * _layout.CellSize;
            }
            else
            {
                dist += step;
            }

            // Safety: prevent infinite loop on tiny steps
            if (step < 0.001f) break;
        }

        if (_cellSnapMode)
        {
            // Cell snap mode: use nearest grid cell for selection (even empty space)
            if (nearestGridCell.HasValue)
            {
                _hoveredCell = nearestGridCell.Value;
                _dotCursorObj.position = _layout.IndexToWorld(nearestGridCell.Value);

                // Color: yellow if solid surface, cyan if empty but in grid
                if (hitCell.HasValue)
                    _dotCursorRenderer.material.color = Color.yellow;
                else
                    _dotCursorRenderer.material.color = Color.cyan;
            }
            else
            {
                _hoveredCell = null;
                _dotCursorObj.position = ray.GetPoint(cursorMaxDistance);
                _dotCursorRenderer.material.color = Color.red;
            }
        }
        else
        {
            // Brush mode: snap to solid surface or fall back to nearest grid cell
            _hoveredCell = null;
            Vector3 hitPoint;
            if (hitCell.HasValue)
                hitPoint = ray.GetPoint(hitDist);
            else if (nearestGridCell.HasValue)
                hitPoint = _layout.IndexToWorld(nearestGridCell.Value);
            else
                hitPoint = ray.GetPoint(cursorMaxDistance);

            _dotCursorObj.position = hitPoint;
            _dotCursorRenderer.material.color = hitCell.HasValue ? Color.green : Color.cyan;
        }

        _dotCursorObj.LookAt(cameraRef.transform.position);
    }

    // ---------- Editing ----------

    private void HandleEditing()
    {
        if (_processor == null) return;

        // Cell snap toggle
        if (_kb[cellSnapToggleKey].wasPressedThisFrame)
            _cellSnapMode = !_cellSnapMode;

        // Brush size: Ctrl + scroll
        float scrollDelta = _mouse.scroll.ReadValue().y;
        if (_kb.leftCtrlKey.isPressed && scrollDelta != 0f)
        {
            brushRadius += scrollDelta * brushSizeScrollSpeed;
            brushRadius = Mathf.Max(0.1f, brushRadius);
        }

        // Vertex selection (Q)
        if (_kb[vertexSelectKey].wasPressedThisFrame && _cellSnapMode && _hoveredCell.HasValue)
        {
            SelectVertexAt(_hoveredCell.Value);
        }

        // Face selection (F)
        if (_kb[faceSelectKey].wasPressedThisFrame && _cellSnapMode && _hoveredCell.HasValue)
        {
            SelectFaceAt(_hoveredCell.Value);
        }

        // Multi-select drag (middle-click)
        if (_mouse.middleButton.wasPressedThisFrame)
            _draggingMultiSelect = true;
        if (_mouse.middleButton.wasReleasedThisFrame)
            _draggingMultiSelect = false;

        // Left-click drag for multi-cell selection
        if (_mouse.leftButton.isPressed && _cellSnapMode)
        {
            ExpandCellSelection();
        }

        // Right-click drag for group move or vertex/face drag (throttled to 10Hz)
        if (_mouse.rightButton.isPressed && Time.time - _lastGroupMoveTime > 0.1f)
        {
            if (_selectedVertices.Count > 0)
                MoveSelectedVertices();
            else if (_selectedCells.Count > 0)
                MoveSelectedGroup();
            _lastGroupMoveTime = Time.time;
        }

        // Scroll to modify cells (only when not Ctrl+scroll)
        if (_cellSnapMode && scrollDelta != 0f && !_kb.leftCtrlKey.isPressed)
        {
            ModifyCells(scrollDelta);
        }

        if (!_cellSnapMode && scrollDelta != 0f && !_kb.leftCtrlKey.isPressed)
        {
            ModifyBrush(scrollDelta);
        }
    }

    private void SelectVertexAt(Vector3Int cellIdx)
      {
          Vector3 cellCenter = _layout.IndexToWorld(cellIdx);

          // Pick the corner closest to the camera ray (proper projection, not arbitrary distance)
          Ray camRay = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));
          Vector3 closestCorner = Vector3.zero;
          float minDistSq = Mathf.Infinity;

          for (int x = 0; x <= 1; x++)
              for (int y = 0; y <= 1; y++)
                  for (int z = 0; z <= 1; z++)
                  {
                      Vector3 corner = cellCenter + new Vector3(
                          (x - 0.5f) * _layout.CellSize,
                          (y - 0.5f) * _layout.CellSize,
                          (z - 0.5f) * _layout.CellSize
                      );
                      float distSq = DistanceToRaySq(corner, camRay);
                      if (distSq < minDistSq)
                      {
                          minDistSq = distSq;
                          closestCorner = corner;
                      }
                  }

          _selectedVertices.Clear();
          _selectedVertices.Add(closestCorner);
      }

      private void SelectFaceAt(Vector3Int cellIdx)
      {
          Vector3 cellCenter = _layout.IndexToWorld(cellIdx);
          Ray camRay = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));
          float half = _layout.CellSize * 0.5f;

          // Find the face whose normal points most toward the camera
          int? bestAxis = null;
          int bestSign = 0;
          float bestDot = Mathf.Infinity; // We want the MOST negative dot (facing camera)

          for (int axis = 0; axis < 3; axis++)
              for (int sign = -1; sign <= 1; sign += 2)
              {
                  Vector3 normal = Vector3.zero;
                  normal[axis] = sign;
                  float dot = Vector3.Dot(camRay.direction, normal);
                  // Face pointing toward camera has negative dot with ray direction
                  if (dot < bestDot)
                  {
                      bestDot = dot;
                      bestAxis = axis;
                      bestSign = sign;
                  }
              }

          _selectedVertices.Clear();
          if (bestAxis.HasValue)
          {
              int axis = bestAxis.Value;
              // Collect the 4 corners of this face
              for (int ix = -1; ix <= 1; ix += 2)
                  for (int iy = -1; iy <= 1; iy += 2)
                      for (int iz = -1; iz <= 1; iz += 2)
                      {
                          Vector3 offset = new Vector3(
                              axis == 0 ? bestSign * half : ix * half,
                              axis == 1 ? bestSign * half : iy * half,
                              axis == 2 ? bestSign * half : iz * half
                          );
                          _selectedVertices.Add(cellCenter + offset);
                      }
          }
      }

      /// <summary>Squared distance from point to infinite ray.</summary>
      private float DistanceToRaySq(Vector3 point, Ray ray)
      {
          Vector3 v = point - ray.origin;
          float t = Mathf.Clamp01(Vector3.Dot(v, ray.direction));
          Vector3 closest = ray.origin + ray.direction * t;
          return (point - closest).sqrMagnitude;
      }

      /// <summary>Drag selected vertices/faces with right-click.</summary>
      private void MoveSelectedVertices()
      {
          if (_selectedVertices.Count == 0) return;

          Vector2 delta = _mouse.delta.ReadValue();
          if (delta.magnitude < 1f) return;

          // Project mouse delta into world-space movement on a plane facing the camera
          Plane camPlane = new Plane(cameraRef.transform.forward, transform.position);
          Ray ray0 = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f) - delta);
          Ray ray1 = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));

          float dist0, dist1;
          if (camPlane.Raycast(ray0, out dist0) && camPlane.Raycast(ray1, out dist1))
          {
              Vector3 move = ray1.GetPoint(dist1) - ray0.GetPoint(dist0);

              foreach (var vertex in _selectedVertices)
              {
                  Vector3 newPos = vertex + move;
                  // Modify density around the new position to attract/repel the surface
                  Bounds editBounds = new Bounds(newPos, Vector3.one * _layout.CellSize * 2f);
                  float depth = move.magnitude * 0.5f;
                  _processor.EditVertexDrag(editBounds, -depth); // negative = fill (pull surface toward)
                  _processor.MarkDirtyBounds(editBounds);
              }
          }
      }

    private List<Vector3> GetCellCorners(Vector3Int idx)
    {
        Vector3 center = _layout.IndexToWorld(idx);
        float half = _layout.CellSize * 0.5f;
        var corners = new List<Vector3>();
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    corners.Add(center + new Vector3(x * half, y * half, z * half));
        return corners;
    }

    private void ExpandCellSelection()
    {
        if (!_hoveredCell.HasValue) return;
        _selectedCells.Add(_hoveredCell.Value);
    }

    private void MoveSelectedGroup()
    {
        Vector2 delta = _mouse.delta.ReadValue();

        if (delta.magnitude < 0.5f) return;

        // Shift cells by one unit in dominant axis
        Vector3Int shift = Vector3Int.zero;
        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            shift.x = delta.x > 0 ? 1 : -1;
        else
            shift.y = delta.y > 0 ? 1 : -1;

        foreach (var cell in _selectedCells)
        {
            Vector3Int newIdx = cell + shift;
            if (_layout.IsInside(newIdx))
            {
                ApplyCellChange(newIdx, true);
                ApplyCellChange(cell, false);
            }
        }
    }

    private void ModifyCells(float scrollDelta)
    {
        bool fill = scrollDelta < 0; // Scroll down = add, up = carve

        if (_selectedCells.Count > 0)
        {
            foreach (var cell in _selectedCells)
                ApplyCellChange(cell, fill);
        }
        else if (_hoveredCell.HasValue)
        {
            ApplyCellChange(_hoveredCell.Value, fill);
        }
    }

    private void ModifyBrush(float scrollDelta)
    {
        if (_processor == null) return;

        // Reuse the hit point from the dot cursor (already computed in UpdateDotCursor)
        Vector3 hitPoint = _dotCursorObj != null ? _dotCursorObj.position : cameraRef.transform.position;

        float depth = Mathf.Abs(scrollDelta) * brushRadius * 0.05f;
        Bounds brushBounds = new Bounds(hitPoint, Vector3.one * brushRadius);

        // Always carve (original flat semantics) — octree: Subtract sphere primitive
        _processor.EditBrush(brushBounds, depth, false);
        _processor.MarkDirtyBounds(brushBounds);
    }

    private void ApplyCellChange(Vector3Int idx, bool fill)
    {
        if (_processor == null) return;

        _processor.EditCell(idx, fill);

        Vector3 worldPos = _layout.IndexToWorld(idx);
        Bounds cellBounds = new Bounds(worldPos, Vector3.one * _layout.CellSize);
        _processor.MarkDirtyBounds(cellBounds);
    }

    // ---------- Debug drawing (Playmode) ----------

    void OnDrawGizmosSelected()
    {
        if (!_active || _processor == null) return;

        if (_cellSnapMode && _hoveredCell.HasValue)
        {
            Vector3 center = _layout.IndexToWorld(_hoveredCell.Value);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(center, Vector3.one * _layout.CellSize);
        }

        foreach (var cell in _selectedCells)
        {
            if (!_layout.IsInside(cell)) continue;
            Vector3 center = _layout.IndexToWorld(cell);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(center, Vector3.one * _layout.CellSize * 0.95f);
        }

        // Vertex/face selection gizmos (magenta)
        if (_selectedVertices.Count > 0)
        {
            float halfCell = _layout.CellSize * 0.25f;
            Gizmos.color = Color.magenta;
            foreach (var vertex in _selectedVertices)
                Gizmos.DrawWireSphere(vertex, halfCell);

            // Draw edges connecting selected vertices (shows face outline for 4-vertex faces)
            if (_selectedVertices.Count == 4)
            {
                // Sort by distance to camera ray for a clean quad loop
                Ray camRay = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));
                var sorted = _selectedVertices.OrderBy(v => DistanceToRaySq(v, camRay)).ToList();
                for (int i = 0; i < sorted.Count; i++)
                    Gizmos.DrawLine(sorted[i], sorted[(i + 1) % sorted.Count]);
            }
        }

        if (_dotCursorObj != null)
        {
            Gizmos.color = _cellSnapMode ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(_dotCursorObj.position, _layout.CellSize * 0.3f);
        }
    }

    // ---------- On-screen HUD ----------

    void OnGUI()
    {
        if (!_active) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.normal.textColor = Color.white;

        Rect box = new Rect(10, 10, 320, 80);
        GUI.Box(box, "", "box");

        GUI.Label(new Rect(20, 15, 300, 20), "FPS Edit Mode | " + (_cellSnapMode ? "Cell Snap" : "Brush") + " | " + (_selectedVertices.Count > 0 ? (_selectedVertices.Count == 4 ? "Face" : "Vertex") : ""), style);
        GUI.Label(new Rect(20, 35, 300, 20), "Cells: " + _selectedCells.Count + " | Vertices: " + _selectedVertices.Count, style);
        GUI.Label(new Rect(20, 55, 300, 20), string.Format("Brush: {0:F1} | Q=vertex F=face | RMB drag to move", brushRadius), style);
    }

    void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (_dotCursorObj != null)
            Destroy(_dotCursorObj.gameObject);
    }
}
