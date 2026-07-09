using System.Collections.Generic;
using UnityEngine;

/// <summary>Registry of available FeatureDefinitions for reuse across the volume system.</summary>
public class FeatureLibrary : ScriptableObject
{
    public List<FeatureDefinition> features = new();

    private Dictionary<string, FeatureDefinition> _lookupByName;

    /// <summary>Adds a feature definition to the library.</summary>
    public void AddFeature(FeatureDefinition definition)
    {
        if (definition == null) return;
        features.Add(definition);
        _lookupByName = null;
    }

    /// <summary>Removes a feature definition from the library.</summary>
    public void RemoveFeature(FeatureDefinition definition)
    {
        features.Remove(definition);
        _lookupByName = null;
    }

    /// <summary>Finds a feature by its display name.</summary>
    public FeatureDefinition GetFeature(string name)
    {
        if (_lookupByName == null) BuildLookup();
        return _lookupByName.TryGetValue(name, out var def) ? def : null;
    }

    /// <summary>Creates a new instance of the given feature at a position.</summary>
    public FeatureInstance CreateInstance(FeatureDefinition definition, Vector3 position)
    {
        if (definition == null) return null;
        return new FeatureInstance(definition, position);
    }

    /// <summary>Creates a new instance by display name.</summary>
    public FeatureInstance CreateInstance(string name, Vector3 position)
    {
        FeatureDefinition def = GetFeature(name);
        if (def == null) return null;
        return new FeatureInstance(def, position);
    }

    /// <summary>Returns all feature definitions matching the given shape type.</summary>
    public List<FeatureDefinition> GetFeaturesByShape(VolumeShapeType shapeType)
    {
        var result = new List<FeatureDefinition>();
        foreach (var def in features)
        {
            if (def.shapeType == shapeType && def.operationRole != VolumeOperationRole.Subtract)
                result.Add(def);
        }
        return result;
    }

    /// <summary>Returns all subtractive feature definitions matching the given shape type.</summary>
    public List<FeatureDefinition> GetSubtractiveFeaturesByShape(VolumeShapeType shapeType)
    {
        var result = new List<FeatureDefinition>();
        foreach (var def in features)
        {
            if (def.shapeType == shapeType && def.operationRole == VolumeOperationRole.Subtract)
                result.Add(def);
        }
        return result;
    }

    private void BuildLookup()
    {
        _lookupByName = new Dictionary<string, FeatureDefinition>();
        foreach (var def in features)
        {
            if (def != null && !_lookupByName.ContainsKey(def.displayName))
                _lookupByName[def.displayName] = def;
        }
    }
}
