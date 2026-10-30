namespace Viora.Application.Live;

// Pixel dimensions describe the clean screen. The face camera occupies a separate
// strip in the same video; its normalized placement is chosen independently by UI.
public sealed record LivePresentation(int Version, int Width, int Height, bool CameraOn, double CameraX, double CameraY)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Version == 1 && Width is >= 2 and <= 4096 && Height is >= 2 and <= 4096 &&
        Width % 2 == 0 && Height % 2 == 0 && double.IsFinite(CameraX) && double.IsFinite(CameraY) &&
        CameraX is >= 0 and <= 1 && CameraY is >= 0 and <= 1;
}
