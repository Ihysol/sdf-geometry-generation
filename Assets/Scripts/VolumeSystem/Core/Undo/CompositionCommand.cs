using UnityEngine;

/// <summary>Undo for adding a VolumeObject to the scene.</summary>
public class AddObjectCommand : ICommand
{
    private readonly VolumeProcessor _processor;
    private readonly int _insertIndex;
    private readonly GameObject _gameObject;
    private readonly Bounds _affectedBounds;

    public AddObjectCommand(
        VolumeProcessor processor,
        int insertIndex,
        GameObject gameObject,
        Bounds affectedBounds = default)
    {
        _processor = processor;
        _insertIndex = insertIndex;
        _gameObject = gameObject;
        _affectedBounds = affectedBounds;
    }

    public void Execute()
    {
        // Object already exists — nothing to do (created before Push)
    }

    public void Revoke()
    {
        if (_gameObject == null || !_processor) return;
        VolumeObjectRegistry composer = _processor.GetComponent<VolumeObjectRegistry>();
        if (composer != null)
        {
            VolumeObject vo = _gameObject.GetComponent<VolumeObject>();
            if (vo != null) composer.objects.Remove(vo);
        }
        Object.DestroyImmediate(_gameObject);
    }

    public Bounds AffectedBounds => _affectedBounds;
}

/// <summary>Undo for removing a VolumeObject from the scene.</summary>
public class RemoveObjectCommand : ICommand
{
    private readonly VolumeProcessor _processor;
    private readonly string _name;
    private readonly VolumeShapeType _shape;
    private readonly VolumeOperationRole _role;
    private readonly Vector3 _localPosition;

    public RemoveObjectCommand(VolumeProcessor processor, string name, VolumeShapeType shape,
                               VolumeOperationRole role, Vector3 localPosition)
    {
        _processor = processor;
        _name = name;
        _shape = shape;
        _role = role;
        _localPosition = localPosition;
    }

    public void Execute()
    {
        // Object already destroyed — nothing to do
    }

    public void Revoke()
    {
        if (!_processor) return;
        GameObject child = new GameObject(_name);
        child.transform.SetParent(_processor.GetObjectsRoot(), false);
        child.transform.localPosition = _localPosition;

        VolumeObject vo = child.AddComponent<VolumeObject>();
        vo.shapeType = _shape;
        vo.role = _role;

        VolumeObjectRegistry composer = _processor.GetComponent<VolumeObjectRegistry>();
        if (composer != null && !composer.objects.Contains(vo))
            composer.objects.Add(vo);
    }

    public Bounds AffectedBounds => default;
}
