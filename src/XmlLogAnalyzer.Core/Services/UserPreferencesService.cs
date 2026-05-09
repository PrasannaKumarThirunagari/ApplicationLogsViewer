using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

/// <summary>
/// Stores favourites and recent paths in a small JSON file beside the executable.
/// Thread-safe via a single lock — adequate for desktop / single-server usage.
/// </summary>
public sealed class UserPreferencesService : IUserPreferencesService
{
    private static readonly object _lock = new();
    private readonly string _file;
    private readonly AppSettings _settings;
    private readonly ILogger<UserPreferencesService> _logger;
    private Prefs _state;

    public UserPreferencesService(IOptions<AppSettings> options, ILogger<UserPreferencesService> logger)
    {
        _settings = options.Value;
        _logger = logger;
        var dir = Path.Combine(AppContext.BaseDirectory, "App_Data");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "preferences.json");
        _state = Load();
    }

    public IReadOnlyList<string> GetFavorites() { lock (_lock) return _state.Favorites.ToList(); }
    public IReadOnlyList<string> GetRecent()    { lock (_lock) return _state.Recent.ToList(); }

    public void AddFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_lock)
        {
            _state.Favorites.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _state.Favorites.Insert(0, path);
            if (_state.Favorites.Count > _settings.FavoritesMax)
                _state.Favorites.RemoveRange(_settings.FavoritesMax, _state.Favorites.Count - _settings.FavoritesMax);
            Save();
        }
    }

    public void RemoveFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_lock)
        {
            _state.Favorites.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Save();
        }
    }

    public void TouchRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_lock)
        {
            _state.Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _state.Recent.Insert(0, path);
            if (_state.Recent.Count > _settings.RecentMax)
                _state.Recent.RemoveRange(_settings.RecentMax, _state.Recent.Count - _settings.RecentMax);
            Save();
        }
    }

    public void ClearRecent()
    {
        lock (_lock) { _state.Recent.Clear(); Save(); }
    }

    private Prefs Load()
    {
        try
        {
            if (!File.Exists(_file)) return new Prefs();
            var json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<Prefs>(json) ?? new Prefs();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load preferences; starting fresh.");
            return new Prefs();
        }
    }

    private void Save()
    {
        try { File.WriteAllText(_file, JsonSerializer.Serialize(_state)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to save preferences."); }
    }

    private sealed class Prefs
    {
        public List<string> Favorites { get; set; } = new();
        public List<string> Recent { get; set; } = new();
    }
}
