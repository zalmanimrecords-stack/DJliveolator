using System.Runtime.InteropServices;

namespace Liveolator.Media.Analysis;

/// <summary>
/// A pinned, redistributable portable-CPython build (python-build-standalone, from Astral's GitHub
/// releases) for one OS/arch: where to download it and the published SHA-256 to verify it against
/// before extraction (doc 32 §2.1). Verifying the checksum is mandatory — an unverified download is
/// never extracted.
/// </summary>
/// <param name="Url">Download URL of the portable-Python archive.</param>
/// <param name="Sha256">Published SHA-256 of the archive (hex). Verified before extraction.</param>
/// <param name="ArchiveFileName">Local file name to download the archive to (drives the extractor).</param>
internal sealed record PythonRuntimeSpec(string Url, string Sha256, string ArchiveFileName)
{
    // Pinned python-build-standalone 3.11 release. The SHA-256 values MUST match the published
    // checksums for these exact assets — update both URL and hash together when bumping the pin.
    // (Astral republishes a SHA256SUMS file per release; copy the matching line here.)
    private const string Tag = "20240814";
    private const string Version = "3.11.9";
    private const string BaseUrl =
        "https://github.com/astral-sh/python-build-standalone/releases/download/" + Tag + "/";

    /// <summary>The spec for the current OS/arch, or <c>null</c> when this platform is not supported.</summary>
    public static PythonRuntimeSpec? ForCurrentPlatform()
    {
        Architecture arch = RuntimeInformation.ProcessArchitecture;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && arch == Architecture.X64)
            return Asset(
                "cpython-" + Version + "+" + Tag + "-x86_64-pc-windows-msvc-install_only.tar.gz",
                "4c71d25731214b8a960d1d87510f24179d819249c5b434aaf7135818421b6215");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && arch == Architecture.Arm64)
            return Asset(
                "cpython-" + Version + "+" + Tag + "-aarch64-apple-darwin-install_only.tar.gz",
                "8760e908f25fdc8a01f4d1b101854ac047b4eacb723fb2593a168fb989c86eef");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && arch == Architecture.X64)
            return Asset(
                "cpython-" + Version + "+" + Tag + "-x86_64-apple-darwin-install_only.tar.gz",
                "76073305812c093ce840df9c4c17068aa69da8d951e7376ef48f43376986a13e");

        return null;
    }

    private static PythonRuntimeSpec Asset(string fileName, string sha256)
        => new(BaseUrl + fileName, sha256, fileName);
}
