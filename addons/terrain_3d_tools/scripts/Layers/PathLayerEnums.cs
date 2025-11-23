namespace Terrain3DTools.Layers
{
    public enum PathType
    {
        Road,
        Path, 
        River,
        Trench,
        Ridge,
        Custom
    }

    public enum PathTextureMode
    {
        SingleTexture,
        CenterEmbankment,
        TripleTexture // center, embankment, transition
    }
}