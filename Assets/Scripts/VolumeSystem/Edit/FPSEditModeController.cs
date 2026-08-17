using System.Collections.Generic;
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
        if (_processor != null && _processor.Pipeline != null)
            _layout = _processor.Pipeline.Buffer.Layout;

        // Cache input devices
        _kb = Keyboard.current;
        _mouse = Mouse.current;

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
                // Yaw goes on parent (main transform), pitch on camera child
                _yaw = transform.eulerAngles.y;
                _pitch = cameraRef.transform.localEulerAngles.x;
            }
        }
    }

     // ---------- Movement: W/S = toward/away from where you look, A/D = strafe, Space/Z = up/down ----------

    private void HandleMovement()
    {
        float forward = (_kb.wKey.isPressed ? 1f : 0f) - (_kb.sKey.isPressed ? 1f : 0f);
        float strafe = (_kb.dKey.isPressed ? 1f : 0f) - (_kb.aKey.isPressed ? 1f : 0f);
        float vertical = (_kb.spaceKey.isPressed ? 1f : 0f) - (_kb.zKey.isPressed ? 1f : 0f);

        // Use camera's actual world-space forward (including pitch for looking up/down)
        Vector3 camForward = cameraRef.transform.forward;
        Vector3 camRight = cameraRef.transform.right;

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

    // ---------- Dot cursor positioning and projection ----------

    private void UpdateDotCursor()
    {
        if (_processor == null || _processor.Pipeline == null) return;

        _layout = _processor.Pipeline.Buffer.Layout;
        Ray ray = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));

        // Clamp to min/max distance via physics raycast
        float dist = cursorMaxDistance;
        if (Physics.Raycast(ray, out RaycastHit hit, cursorMaxDistance))
            dist = Mathf.Max(hit.distance, cursorMinDistance);

        Vector3 hitPoint = ray.GetPoint(dist);

        if (_cellSnapMode)
        {
            Vector3Int idx = _layout.WorldToIndex(hitPoint);
            if (_layout.IsInside(idx))
            {
                hitPoint = _layout.IndexToWorld(idx);
                _hoveredCell = idx;
            }
            else
            {
                _hoveredCell = null;
            }

            _dotCursorRenderer.material.color = Color.yellow;
        }
        else
        {
            _hoveredCell = null;
            _dotCursorRenderer.material.color = Color.green;
        }

        // Position dot cursor at hit point
        _dotCursorObj.position = hitPoint;
        _dotCursorObj.LookAt(cameraRef.transform.position);
    }

    // ---------- Editing ----------

    private void HandleEditing()
    {
        if (_processor == null || _processor.Pipeline == null) return;

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

        // Right-click drag for group move (throttled to 10Hz)
        if (_mouse.rightButton.isPressed && _selectedCells.Count > 0 && Time.time - _lastGroupMoveTime > 0.1f)
        {
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

        Vector3 closestCorner = Vector3.zero;
        float minDist = Mathf.Infinity;

        Ray camRay = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));

        for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 corner = cellCenter + new Vector3(
                        (x - 0.5f) * _layout.CellSize,
                        (y - 0.5f) * _layout.CellSize,
                        (z - 0.5f) * _layout.CellSize
                    );
                    float dist = Vector3.Distance(corner, camRay.GetPoint(1f));
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestCorner = corner;
                    }
                }

        _selectedVertices.Clear();
        _selectedVertices.Add(closestCorner);
    }

    private void SelectFaceAt(Vector3Int cellIdx)
    {
        _selectedVertices.Clear();
        foreach (Vector3 corner in GetCellCorners(cellIdx))
            _selectedVertices.Add(corner);
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
        if (_processor == null || _processor.Pipeline == null) return;

        Ray ray = cameraRef.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));
        float dist = cursorMaxDistance;
        if (Physics.Raycast(ray, out RaycastHit hit, cursorMaxDistance))
            dist = Mathf.Max(hit.distance, cursorMinDistance);
        Vector3 hitPoint = ray.GetPoint(dist);

        float depth = Mathf.Abs(scrollDelta) * brushRadius * 0.05f;
        Bounds brushBounds = new Bounds(hitPoint, Vector3.one * brushRadius);

        var op = new CarveOperation(brushBounds, new EditAnchor { type = EditAnchorType.World }, depth);
        _processor.EditLayer.Add(op);
        _processor.MarkDirtyBounds(brushBounds);
    }

    private void ApplyCellChange(Vector3Int idx, bool fill)
    {
        if (_processor == null || _processor.Pipeline == null) return;

        var cells = new List<Vector3Int> { idx };
        var op = new CellOperation(cells, new EditAnchor { type = EditAnchorType.World }, fill);
        _processor.EditLayer.Add(op);

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

        GUI.Label(new Rect(20, 15, 300, 20), "FPS Edit Mode | " + (_cellSnapMode ? "Cell Snap" : "Brush"), style);
        GUI.Label(new Rect(20, 35, 300, 20), "Cells: " + _selectedCells.Count + " | Vertices: " + _selectedVertices.Count, style);
        GUI.Label(new Rect(20, 55, 300, 20), string.Format("Brush: {0:F1} | Scroll: carve/fill | Ctrl+Scroll: size", brushRadius), style);
    }

    void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (_dotCursorObj != null)
            Destroy(_dotCursorObj.gameObject);
    }
}
