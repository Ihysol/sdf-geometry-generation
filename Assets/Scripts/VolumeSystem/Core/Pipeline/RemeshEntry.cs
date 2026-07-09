public struct RemeshEntry
{
    public ChunkCoord Coord;
    public int Priority;
    public DirtyReason Reason;
    public int Version;

    public RemeshEntry(ChunkCoord coord, int priority, DirtyReason reason, int version)
    {
        Coord = coord;
        Priority = priority;
        Reason = reason;
        Version = version;
    }
}
