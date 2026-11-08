namespace XPlatUI.Demo;

/// <summary>
/// The SVG documents the sample shows. They are strings rather than files because the demo's render
/// path has to work from an arbitrary working directory, and because a viewer of the sample can read
/// the artwork next to the page that draws it.
/// </summary>
/// <remarks>
/// Every one of them carries a <c>viewBox</c>, which is what makes the control measure it correctly:
/// an SVG with no viewBox and no width or height has no size to give <see cref="XPlatUI.XPlatImage.AutoSize"/>.
/// </remarks>
internal static class DemoIcons
{
    /// <summary>A star with a hole, drawn in place of a bitmap.</summary>
    internal const string Star = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <path d="M12 2 L14.9 8.6 L22 9.3 L16.6 14.1 L18.2 21.1 L12 17.4 L5.8 21.1 L7.4 14.1 L2 9.3 L9.1 8.6 Z"
                fill="#e0a400" stroke="#8a6400" stroke-width="0.8" stroke-linejoin="round"/>
        </svg>
        """;

    /// <summary>A gear, to show that a detailed path is not a special case.</summary>
    internal const string Gear = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <circle cx="12" cy="12" r="9" fill="none" stroke="#3a7bd5" stroke-width="1.6"/>
          <circle cx="12" cy="12" r="3.4" fill="none" stroke="#3a7bd5" stroke-width="1.6"/>
          <g stroke="#3a7bd5" stroke-width="1.6" stroke-linecap="round">
            <path d="M12 1.6 L12 5.2"/><path d="M12 18.8 L12 22.4"/>
            <path d="M1.6 12 L5.2 12"/><path d="M18.8 12 L22.4 12"/>
            <path d="M4.6 4.6 L7.2 7.2"/><path d="M16.8 16.8 L19.4 19.4"/>
            <path d="M19.4 4.6 L16.8 7.2"/><path d="M7.2 16.8 L4.6 19.4"/>
          </g>
        </svg>
        """;

    /// <summary>A heart, whose path is curved enough that a raster copy at a small size shows.</summary>
    internal const string Heart = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <path d="M12 21 C 4 15, 2 9.6, 4.4 6.6 C 6.8 3.6, 10.6 4.6, 12 7.4 C 13.4 4.6, 17.2 3.6, 19.6 6.6 C 22 9.6, 20 15, 12 21 Z"
                fill="#d6455a"/>
        </svg>
        """;

    /// <summary>A tick, thin enough that antialiasing at 16 pixels is the interesting part.</summary>
    internal const string Tick = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <path d="M3 12.5 L9 19 L21 4.5" fill="none" stroke="#2e9e5b" stroke-width="2.6"
                stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        """;

    /// <summary>A gradient disc: the vector path a bitmap twin of this sample is compared against.</summary>
    internal const string Disc = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <defs>
            <radialGradient id="disc" cx="0.35" cy="0.3" r="0.75">
              <stop offset="0" stop-color="#ffe9a8"/>
              <stop offset="0.55" stop-color="#e8a13a"/>
              <stop offset="1" stop-color="#8a4a12"/>
            </radialGradient>
          </defs>
          <circle cx="12" cy="12" r="10" fill="url(#disc)"/>
        </svg>
        """;

    /// <summary>An icon that draws with <c>currentColor</c>, which is what a theme can change.</summary>
    internal const string CurrentColorBadge = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" stroke-width="2"/>
          <path d="M7.5 12.5 L10.8 15.8 L16.8 8.4" fill="none" stroke="currentColor" stroke-width="2.2"
                stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        """;
}
