namespace XmlLogAnalyzer.Core.Interfaces;

public interface IPathValidator
{
    /// <summary>
    /// Resolves the supplied path, normalises it, and ensures it lives below an allowed root
    /// and is not in a forbidden system folder. Throws <see cref="UnauthorizedAccessException"/>
    /// otherwise.
    /// </summary>
    string ValidateFolder(string path);
    string ValidateFile(string path);

    bool IsExtensionAllowed(string path);

    // ----- Text-log variants: validated against the TextLogRoots / TextLogExtensions
    // configured under XmlLogAnalyzer.TextLogRoots in appsettings.
    string ValidateTextLogFolder(string path);
    string ValidateTextLogFile(string path);
    bool IsTextLogExtensionAllowed(string path);
}
