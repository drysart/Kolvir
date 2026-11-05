using System.Globalization;

namespace XPlatUI.Tests;

/// <summary>
/// Pins the current culture for the lifetime of a test and restores it afterwards.
/// </summary>
/// <remarks>
/// A date field parses and formats with the current culture on purpose - the decimal separator and
/// the day/month order are the user's, not the library's - so a test that types a date has to say
/// which culture it is typing in. Without this, "3/4/2025" is March 4 on a US machine and 3 April on
/// a British one, and the same test would pass in one runner and fail in another.
/// </remarks>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous;
    private readonly CultureInfo _previousUi;

    internal CultureScope(string name)
    {
        _previous = CultureInfo.CurrentCulture;
        _previousUi = CultureInfo.CurrentUICulture;

        var culture = new CultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>US English, the culture the typed-date tests are written in.</summary>
    internal static CultureScope English(string name = "en-US") => new(name);

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previous;
        CultureInfo.CurrentUICulture = _previousUi;
    }
}
