using System.IO.Compression;

namespace XPlatUI.Svg;

/// <summary>
/// The per-user cache a compressed native library is unpacked into once, and the write discipline
/// that makes a concurrent reader never see half a file.
/// </summary>
/// <remarks>
/// <para>
/// This is the same discipline <c>SkiaNativeLoader</c> in the core library uses, factored out so it
/// can be driven without publishing: the archive is decompressed into a temporary file in the
/// destination directory and then moved into place, which is atomic on every platform this ships to.
/// A process that reads the destination either finds no file or the whole file, never a library
/// somebody else is still writing.
/// </para>
/// <para>
/// The key names the directory after the two things that decide whether a cached copy is still the
/// right one: the version of the assembly the archive was embedded in, and the compressed length of
/// the archive. A new build changes at least one of them, so an upgrade never reuses the previous
/// build's library.
/// </para>
/// </remarks>
internal static class NativeArchiveCache
{
    /// <summary>
    /// The directory name for one embedded archive: assembly version and compressed length.
    /// </summary>
    /// <param name="version">The version of the assembly the archive was embedded in.</param>
    /// <param name="archiveLength">The compressed length of the archive, in bytes.</param>
    internal static string Key(Version? version, long archiveLength) =>
        $"{version?.ToString() ?? "0"}-{archiveLength:x}";

    /// <summary>
    /// Decompresses <paramref name="archive"/> to <paramref name="path"/>, via a temporary file.
    /// </summary>
    /// <param name="archive">A gzip stream positioned at its start.</param>
    /// <param name="path">The final path the decompressed bytes appear at.</param>
    /// <exception cref="IOException">The archive is not readable, or the write failed.</exception>
    /// <exception cref="InvalidDataException">The archive is not valid gzip data.</exception>
    internal static void Write(Stream archive, string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        // Process id and a random suffix: two processes never share a temporary name, and neither do
        // two threads in one process.
        var temporary = Path.Combine(
            directory,
            $"{Path.GetFileName(path)}.{Environment.ProcessId:x}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = File.Create(temporary))
            using (var gzip = new GZipStream(archive, CompressionMode.Decompress))
            {
                gzip.CopyTo(file);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
                // A leftover temporary file is untidy, not a failure: it is never the file anything
                // loads, and the next extraction writes its own.
            }
        }
    }
}
