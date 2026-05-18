using System.Collections.Generic;
using UnityEngine;

public interface IChunkLayoutVolume
{
    void BuildChunkBounds(ChunkingSettings settings, List<Bounds> output);
}
