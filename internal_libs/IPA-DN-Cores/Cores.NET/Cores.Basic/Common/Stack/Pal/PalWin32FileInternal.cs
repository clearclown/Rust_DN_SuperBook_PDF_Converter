// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Some parts of this program are from Microsoft CoreCLR - https://github.com/dotnet/coreclr
// 
// The MIT License (MIT)
// 
// Copyright (c) .NET Foundation and Contributors
// 
// All rights reserved.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace IPA.Cores.Basic;

public static partial class Win32PathInternal
{
    // All paths in Win32 ultimately end up becoming a path to a File object in the Windows object manager. Passed in paths get mapped through
    // DosDevice symbolic links in the object tree to actual File objects under \Devices. To illustrate, this is what happens with a typical
    // path "Foo" passed as a filename to any Win32 API:
    //
    //  1. "Foo" is recognized as a relative path and is appended to the current directory (say, "C:\" in our example)
    //  2. "C:\Foo" is prepended with the DosDevice namespace "\??\"
    //  3. CreateFile tries to create an object handle to the requested file "\??\C:\Foo"
    //  4. The Object Manager recognizes the DosDevices prefix and looks
    //      a. First in the current session DosDevices ("\Sessions\1\DosDevices\" for example, mapped network drives go here)
    //      b. If not found in the session, it looks in the Global DosDevices ("\GLOBAL??\")
    //  5. "C:" is found in DosDevices (in our case "\GLOBAL??\C:", which is a symbolic link to "\Device\HarddiskVolume6")
    //  6. The full path is now "\Device\HarddiskVolume6\Foo", "\Device\HarddiskVolume6" is a File object and parsing is handed off
    //      to the registered parsing method for Files
    //  7. The registered open method for File objects is invoked to create the file handle which is then returned
    //
    // There are multiple ways to directly specify a DosDevices path. The final format of "\??\" is one way. It can also be specified
    // as "\\.\" (the most commonly documented way) and "\\?\". If the question mark syntax is used the path will skip normalization
    // (essentially GetFullPathName()) and path length checks.

    // Windows Kernel-Mode Object Manager
    // https://msdn.microsoft.com/en-us/library/windows/hardware/ff565763.aspx
    // https://channel9.msdn.com/Shows/Going+Deep/Windows-NT-Object-Manager
    //
    // Introduction to MS-DOS Device Names
    // https://msdn.microsoft.com/en-us/library/windows/hardware/ff548088.aspx
    //
    // Local and Global MS-DOS Device Names
    // https://msdn.microsoft.com/en-us/library/windows/hardware/ff554302.aspx

    internal const char DirectorySeparatorChar = '\\';
    internal const char AltDirectorySeparatorChar = '/';
    internal const char VolumeSeparatorChar = ':';
    internal const char PathSeparator = ';';

    internal const string DirectorySeparatorCharAsString = "\\";

    internal const string ExtendedPathPrefix = @"\\?\";
    internal const string UncPathPrefix = @"\\";
    internal const string UncExtendedPrefixToInsert = @"?\UNC\";
    internal const string UncExtendedPathPrefix = @"\\?\UNC\";
    internal const string DevicePathPrefix = @"\\.\";
    internal const string ParentDirectoryPrefix = @"..\";

    internal const int MaxShortPath = 260;
    internal const int MaxShortDirectoryPath = 248;
    // \\?\, \\.\, \??\
    internal const int DevicePrefixLength = 4;
    // \\
    internal const int UncPrefixLength = 2;
    // \\?\UNC\, \\.\UNC\
    internal const int UncExtendedPrefixLength = 8;

    /// <summary>
    /// Returns true if the given character is a valid drive letter
    /// </summary>
    internal static bool IsValidDriveChar(char value)
    {
        return ((value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z'));
    }

    internal static bool EndsWithPeriodOrSpace(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        char c = path[path.Length - 1];
        return c == ' ' || c == '.';
    }

    /// <summary>
    /// Adds the extended path prefix (\\?\) if not already a device path, IF the path is not relative,
    /// AND the path is more than 259 characters. (> MAX_PATH + null). This will also insert the extended
    /// prefix if the path ends with a period or a space. Trailing periods and spaces are normally eaten
    /// away from paths during normalization, but if we see such a path at this point it should be
    /// normalized and has retained the final characters. (Typically from one of the *Info classes)
    /// </summary>
    [return: NotNullIfNotNull("path")]
    internal static string? EnsureExtendedPrefixIfNeeded(string? path)
    {
        if (path != null && (path.Length >= MaxShortPath || EndsWithPeriodOrSpace(path)))
        {
            return EnsureExtendedPrefix(path);
        }
        else
        {
            return path;
        }
    }

    /// <summary>
    /// DO NOT USE- Use EnsureExtendedPrefixIfNeeded. This will be removed shortly.
    /// Adds the extended path prefix (\\?\) if not already a device path, IF the path is not relative,
    /// AND the path is more than 259 characters. (> MAX_PATH + null)
    /// </summary>
    [return: NotNullIfNotNull("path")]
    internal static string? EnsureExtendedPrefixOverMaxPath(string? path)
    {
        if (path != null && path.Length >= MaxShortPath)
        {
            return EnsureExtendedPrefix(path);
        }
        else
        {
            return path;
        }
    }

    /// <summary>
    /// Adds the extended path prefix (\\?\) if not relative or already a device path.
    /// </summary>
    internal static string EnsureExtendedPrefix(string path)
    {
        // Putting the extended prefix on the path changes the processing of the path. It won't get normalized, which
        // means adding to relative paths will prevent them from getting the appropriate current directory inserted.

        // If it already has some variant of a device path (\??\, \\?\, \\.\, //./, etc.) we don't need to change it
        // as it is either correct or we will be changing the behavior. When/if Windows supports long paths implicitly
        // in the future we wouldn't want normalization to come back and break existing code.

        // In any case, all internal usages should be hitting normalize path (Path.GetFullPath) before they hit this
        // shimming method. (Or making a change that doesn't impact normalization, such as adding a filename to a
        // normalized base path.)
        if (IsPartiallyQualified(path.AsSpan()) || IsDevice(path.AsSpan()))
            return path;

        // Given \\server\share in longpath becomes \\?\UNC\server\share
        if (path.StartsWith(UncPathPrefix, StringComparison.OrdinalIgnoreCase))
            return path.Insert(2, UncExtendedPrefixToInsert);

        return ExtendedPathPrefix + path;
    }

    /// <summary>
    /// Returns true if the path uses any of the DOS device path syntaxes. ("\\.\", "\\?\", or "\??\")
    /// </summary>
    internal static bool IsDevice(ReadOnlySpan<char> path)
    {
        // If the path begins with any two separators is will be recognized and normalized and prepped with
        // "\??\" for internal usage correctly. "\??\" is recognized and handled, "/??/" is not.
        return IsExtended(path)
            ||
            (
                path.Length >= DevicePrefixLength
                && IsDirectorySeparator(path[0])
                && IsDirectorySeparator(path[1])
                && (path[2] == '.' || path[2] == '?')
                && IsDirectorySeparator(path[3])
            );
    }

    /// <summary>
    /// Returns true if the path is a device UNC (\\?\UNC\, \\.\UNC\)
    /// </summary>
    internal static bool IsDeviceUNC(ReadOnlySpan<char> path)
    {
        return path.Length >= UncExtendedPrefixLength
            && IsDevice(path)
            && IsDirectorySeparator(path[7])
            && path[4] == 'U'
            && path[5] == 'N'
            && path[6] == 'C';
    }

    /// <summary>
    /// Returns true if the path uses the canonical form of extended syntax ("\\?\" or "\??\"). If the
    /// path matches exactly (cannot use alternate directory separators) Windows will skip normalization
    /// and path length checks.
    /// </summary>
    internal static bool IsExtended(ReadOnlySpan<char> path)
    {
        // While paths like "//?/C:/" will work, they're treated the same as "\\.\" paths.
        // Skipping of normalization will *only* occur if back slashes ('\') are used.
        return path.Length >= DevicePrefixLength
            && path[0] == '\\'
            && (path[1] == '\\' || path[1] == '?')
            && path[2] == '?'
            && path[3] == '\\';
    }

    /// <summary>
    /// Check for known wildcard characters. '*' and '?' are the most common ones.
    /// </summary>
    internal static bool HasWildCardCharacters(ReadOnlySpan<char> path)
    {
        // Question mark is part of dos device syntax so we have to skip if we are
        int startIndex = IsDevice(path) ? ExtendedPathPrefix.Length : 0;

        // [MS - FSA] 2.1.4.4 Algorithm for Determining if a FileName Is in an Expression
        // https://msdn.microsoft.com/en-us/library/ff469270.aspx
        for (int i = startIndex; i < path.Length; i++)
        {
            char c = path[i];
            if (c <= '?') // fast path for common case - '?' is highest wildcard character
            {
                if (c == '\"' || c == '<' || c == '>' || c == '*' || c == '?')
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the length of the root of the path (drive, share, etc.).
    /// </summary>
    internal static int GetRootLength(ReadOnlySpan<char> path)
    {
        int pathLength = path.Length;
        int i = 0;

        bool deviceSyntax = IsDevice(path);
        bool deviceUnc = deviceSyntax && IsDeviceUNC(path);

        if ((!deviceSyntax || deviceUnc) && pathLength > 0 && IsDirectorySeparator(path[0]))
        {
            // UNC or simple rooted path (e.g. "\foo", NOT "\\?\C:\foo")
            if (deviceUnc || (pathLength > 1 && IsDirectorySeparator(path[1])))
            {
                // UNC (\\?\UNC\ or \\), scan past server\share

                // Start past the prefix ("\\" or "\\?\UNC\")
                i = deviceUnc ? UncExtendedPrefixLength : UncPrefixLength;

                // Skip two separators at most
                int n = 2;
                while (i < pathLength && (!IsDirectorySeparator(path[i]) || --n > 0))
                    i++;
            }
            else
            {
                // Current drive rooted (e.g. "\foo")
                i = 1;
            }
        }
        else if (deviceSyntax)
        {
            // Device path (e.g. "\\?\.", "\\.\")
            // Skip any characters following the prefix that aren't a separator
            i = DevicePrefixLength;
            while (i < pathLength && !IsDirectorySeparator(path[i]))
                i++;

            // If there is another separator take it, as long as we have had at least one
            // non-separator after the prefix (e.g. don't take "\\?\\", but take "\\?\a\")
            if (i < pathLength && i > DevicePrefixLength && IsDirectorySeparator(path[i]))
                i++;
        }
        else if (pathLength >= 2
            && path[1] == VolumeSeparatorChar
            && IsValidDriveChar(path[0]))
        {
            // Valid drive specified path ("C:", "D:", etc.)
            i = 2;

            // If the colon is followed by a directory separator, move past it (e.g "C:\")
            if (pathLength > 2 && IsDirectorySeparator(path[2]))
                i++;
        }

        return i;
    }

    /// <summary>
    /// Returns true if the path specified is relative to the current drive or working directory.
    /// Returns false if the path is fixed to a specific drive or UNC path.  This method does no
    /// validation of the path (URIs will be returned as relative as a result).
    /// </summary>
    /// <remarks>
    /// Handles paths that use the alternate directory separator.  It is a frequent mistake to
    /// assume that rooted paths (Path.IsPathRooted) are not relative.  This isn't the case.
    /// "C:a" is drive relative- meaning that it will be resolved against the current directory
    /// for C: (rooted, but relative). "C:\a" is rooted and not relative (the current directory
    /// will not be used to modify the path).
    /// </remarks>
    internal static bool IsPartiallyQualified(ReadOnlySpan<char> path)
    {
        if (path.Length < 2)
        {
            // It isn't fixed, it must be relative.  There is no way to specify a fixed
            // path with one character (or less).
            return true;
        }

        if (IsDirectorySeparator(path[0]))
        {
            // There is no valid way to specify a relative path with two initial slashes or
            // \? as ? isn't valid for drive relative paths and \??\ is equivalent to \\?\
            return !(path[1] == '?' || IsDirectorySeparator(path[1]));
        }

        // The only way to specify a fixed path that doesn't begin with two slashes
        // is the drive, colon, slash format- i.e. C:\
        return !((path.Length >= 3)
            && (path[1] == VolumeSeparatorChar)
            && IsDirectorySeparator(path[2])
            // To match old behavior we'll check the drive character for validity as the path is technically
            // not qualified if you don't have a valid drive. "=:\" is the "=" file's default data stream.
            && IsValidDriveChar(path[0]));
    }

    /// <summary>
    /// True if the given character is a directory separator.
    /// </summary>
    [MethodImpl(Inline)]
    internal static bool IsDirectorySeparator(char c)
    {
        return c == DirectorySeparatorChar || c == AltDirectorySeparatorChar;
    }

    /// <summary>
    /// Normalize separators in the given path. Converts forward slashes into back slashes and compresses slash runs, keeping initial 2 if present.
    /// Also trims initial whitespace in front of "rooted" paths (see PathStartSkip).
    /// 
    /// This effectively replicates the behavior of the legacy NormalizePath when it was called with fullCheck=false and expandShortpaths=false.
    /// The current NormalizePath gets directory separator normalization from Win32's GetFullPathName(), which will resolve relative paths and as
    /// such can't be used here (and is overkill for our uses).
    /// 
    /// Like the current NormalizePath this will not try and analyze periods/spaces within directory segments.
    /// </summary>
    /// <remarks>
    /// The only callers that used to use Path.Normalize(fullCheck=false) were Path.GetDirectoryName() and Path.GetPathRoot(). Both usages do
    /// not need trimming of trailing whitespace here.
    /// 
    /// GetPathRoot() could technically skip normalizing separators after the second segment- consider as a future optimization.
    /// 
    /// For legacy desktop behavior with ExpandShortPaths:
    ///  - It has no impact on GetPathRoot() so doesn't need consideration.
    ///  - It could impact GetDirectoryName(), but only if the path isn't relative (C:\ or \\Server\Share).
    /// 
    /// In the case of GetDirectoryName() the ExpandShortPaths behavior was undocumented and provided inconsistent results if the path was
    /// fixed/relative. For example: "C:\PROGRA~1\A.TXT" would return "C:\Program Files" while ".\PROGRA~1\A.TXT" would return ".\PROGRA~1". If you
    /// ultimately call GetFullPath() this doesn't matter, but if you don't or have any intermediate string handling could easily be tripped up by
    /// this undocumented behavior.
    /// 
    /// We won't match this old behavior because:
    /// 
    ///   1. It was undocumented
    ///   2. It was costly (extremely so if it actually contained '~')
    ///   3. Doesn't play nice with string logic
    ///   4. Isn't a cross-plat friendly concept/behavior
    /// </remarks>
    internal static string NormalizeDirectorySeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        char current;

        // Make a pass to see if we need to normalize so we can potentially skip allocating
        bool normalized = true;

        for (int i = 0; i < path.Length; i++)
        {
            current = path[i];
            if (IsDirectorySeparator(current)
                && (current != DirectorySeparatorChar
                    // Check for sequential separators past the first position (we need to keep initial two for UNC/extended)
                    || (i > 0 && i + 1 < path.Length && IsDirectorySeparator(path[i + 1]))))
            {
                normalized = false;
                break;
            }
        }

        if (normalized)
            return path;

        StringBuilder builder = new StringBuilder(path.Length);

        int start = 0;
        if (IsDirectorySeparator(path[start]))
        {
            start++;
            builder.Append(DirectorySeparatorChar);
        }

        for (int i = start; i < path.Length; i++)
        {
            current = path[i];

            // If we have a separator
            if (IsDirectorySeparator(current))
            {
                // If the next is a separator, skip adding this
                if (i + 1 < path.Length && IsDirectorySeparator(path[i + 1]))
                {
                    continue;
                }

                // Ensure it is the primary separator
                current = DirectorySeparatorChar;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns true if the path is effectively empty for the current OS.
    /// For unix, this is empty or null. For Windows, this is empty, null, or 
    /// just spaces ((char)32).
    /// </summary>
    internal static bool IsEffectivelyEmpty(ReadOnlySpan<char> path)
    {
        if (path.IsEmpty)
            return true;

        foreach (char c in path)
        {
            if (c != ' ')
                return false;
        }
        return true;
    }

    internal static ReadOnlySpan<char> TrimEndingDirectorySeparator(ReadOnlySpan<char> path) =>
        Win32PathInternal.EndsInDirectorySeparator(path) ?
        path.Slice(0, path.Length - 1) :
        path;

    internal static string TrimEndingDirectorySeparator(string path) =>
        EndsInDirectorySeparator(path.AsSpan()) && !IsRoot(path.AsSpan()) ?
            path.Substring(0, path.Length - 1) :
            path;

    internal static bool IsRoot(ReadOnlySpan<char> path)
        => path.Length == GetRootLength(path);

    internal static bool EndsInDirectorySeparator(ReadOnlySpan<char> path)
        => path.Length > 0 && IsDirectorySeparator(path[path.Length - 1]);


    public static bool IsValidPathChars(string path)
    {
        if (path == null || path == "") return false;

        if (path.IndexOfAny(InvalidPathChars) != -1)
            return false;

        return true;
    }

    public static bool IsValidFileNameChars(string fileName)
    {
        if (fileName == null || fileName == "") return false;

        if (fileName.IndexOfAny(InvalidFileNameChars) != -1)
            return false;

        return true;
    }

    public static char[] GetInvalidFileNameChars() => (char[])InvalidFileNameChars.Clone();
    public static char[] GetInvalidPathChars() => (char[])InvalidPathChars.Clone();

    static readonly char[] InvalidFileNameChars = new char[]
    {
            '"',
            '<',
            '>',
            '|',
            '\0',
            '\u0001',
            '\u0002',
            '\u0003',
            '\u0004',
            '\u0005',
            '\u0006',
            '\a',
            '\b',
            '\t',
            '\n',
            '\v',
            '\f',
            '\r',
            '\u000e',
            '\u000f',
            '\u0010',
            '\u0011',
            '\u0012',
            '\u0013',
            '\u0014',
            '\u0015',
            '\u0016',
            '\u0017',
            '\u0018',
            '\u0019',
            '\u001a',
            '\u001b',
            '\u001c',
            '\u001d',
            '\u001e',
            '\u001f',
            ':',
            '*',
            '?',
            '\\',
            '/'
    };

    static readonly char[] InvalidPathChars = new char[]
    {
            '"',
            '<',
            '>',
            '|',
            '\0',
            '\u0001',
            '\u0002',
            '\u0003',
            '\u0004',
            '\u0005',
            '\u0006',
            '\a',
            '\b',
            '\t',
            '\n',
            '\v',
            '\f',
            '\r',
            '\u000e',
            '\u000f',
            '\u0010',
            '\u0011',
            '\u0012',
            '\u0013',
            '\u0014',
            '\u0015',
            '\u0016',
            '\u0017',
            '\u0018',
            '\u0019',
            '\u001a',
            '\u001b',
            '\u001c',
            '\u001d',
            '\u001e',
            '\u001f',
            '*',
            '?',
    };
}

