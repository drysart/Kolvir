using System.Runtime.CompilerServices;

// The cache behind XPlatColorPicker's spectrum is a performance contract rather than an appearance,
// so the suite has to be able to see how often the square's image was generated. Everything else in
// the library is still exercised through its public surface.
[assembly: InternalsVisibleTo("XPlatUI.Tests")]
