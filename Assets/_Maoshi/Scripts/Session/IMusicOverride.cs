namespace Maoshi.Session
{
    /// <summary>
    /// Мини-игра со своей музыкой (например, ритм-игра): AudioManager включает дорожку MusicTrack вместо обычной,
    /// "none" — фоновая музыка затихает.
    /// </summary>
    public interface IMusicOverride
    {
        string MusicTrack { get; }
    }
}
