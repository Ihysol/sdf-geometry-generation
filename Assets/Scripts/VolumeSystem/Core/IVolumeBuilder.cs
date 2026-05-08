public interface IVolumeBuilder<TVolume>
{
    TVolume Build(IScalarFieldSource source);
}