using System;
using UnityEngine;

/// <summary>Undo for a single float parameter change on VolumeObject.</summary>
public class ParameterCommand : ICommand
{
    private readonly Action<float> _setter;
    private readonly float _oldValue, _newValue;

    public ParameterCommand(VolumeObject obj, string propertyName, float oldValue, float newValue)
    {
        _setter = CreateSetter(obj, propertyName);
        _oldValue = oldValue;
        _newValue = newValue;
    }

    private static Action<float> CreateSetter(object target, string name)
    {
        var field = target.GetType().GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(float))
            return v => { }; // no-op fallback
        return val => field.SetValue(target, val);
    }

    public void Execute() => _setter?.Invoke(_newValue);
    public void Revoke()  => _setter?.Invoke(_oldValue);

    public Bounds AffectedBounds => default; // Full rebuild on param change
}
