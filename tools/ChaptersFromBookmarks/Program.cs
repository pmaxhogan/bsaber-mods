using System;

namespace ChaptersFromBookmarks;

/// <summary>Entry point; see <see cref="ChaptersCli"/> for arguments and exit codes.</summary>
internal static class Program
{
    private static int Main(string[] args) => ChaptersCli.Run(args, Console.Out, Console.Error);
}
