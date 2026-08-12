using UnityEngine;

/// <summary>Undo for VolumeObject local transform changes (position, rotation, scale).</summary>
public class TransformCommand : ICommand
{
    private readonly Transform _transform;
    private readonly Vector3 _oldPos, _newPos;
    private readonly Quaternion _oldRot, _newRot;
    private readonly Vector3 _oldScl, _newScl;
    private readonly Bounds _affectedBounds;

    public TransformCommand(Transform t, Vector3 oldPos, Quaternion oldRot, Vector3 oldScl,
                            Vector3 newPos, Quaternion newRot, Vector3 newScl)
        : this(t, oldPos, oldRot, oldScl, newPos, newRot, newScl, default)
    { }

    public TransformCommand(Transform t, Vector3 oldPos, Quaternion oldRot, Vector3 oldScl,
                            Vector3 newPos, Quaternion newRot, Vector3 newScl, Bounds affectedBounds)
    {
        _transform = t;
        (_oldPos, _newPos) = (oldPos, newPos);
        (_oldRot, _newRot) = (oldRot, newRot);
        (_oldScl, _newScl) = (oldScl, newScl);
        _affectedBounds = affectedBounds;
    }

    public void Execute()
    {
        if (_transform == null) return;
        _transform.localPosition = _newPos;
        _transform.localRotation = _newRot;
        _transform.localScale = _newScl;
    }

    public void Revoke()
    {
        if (_transform == null) return;
        _transform.localPosition = _oldPos;
        _transform.localRotation = _oldRot;
        _transform.localScale = _oldScl;
    }

    public Bounds AffectedBounds => _affectedBounds;
}
