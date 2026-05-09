namespace XmlLogAnalyzer.Core.Interfaces;

public interface IUserPreferencesService
{
    IReadOnlyList<string> GetFavorites();
    void AddFavorite(string path);
    void RemoveFavorite(string path);

    IReadOnlyList<string> GetRecent();
    void TouchRecent(string path);
    void ClearRecent();
}
