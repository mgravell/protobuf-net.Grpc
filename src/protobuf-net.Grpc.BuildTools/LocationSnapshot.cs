using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ProtoBuf.Grpc.BuildTools;

internal readonly record struct LocationSnapshot(string FilePath, TextSpan TextSpan)
{
    public override string ToString()
        => FilePath is null ? "(no location)" : $"{FilePath} C{TextSpan.Start}-{TextSpan.End}";

    public Location? GetLocation(IEnumerable<SyntaxTree> trees)
    {
        if (FilePath is not null && trees is not null)
        {
            foreach (var tree in trees)
            {
                if (tree.FilePath == FilePath)
                {
                    return Location.Create(tree, TextSpan);
                }
            }
        }

        return null;
    }

    public static LocationSnapshot Create(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            // there are other things we can do with linespan/mappedlinespan,
            // but we'll keep things simple for now
            var path = location.SourceTree?.FilePath;
            if (path is not null)
            {
                return new LocationSnapshot(path, location.SourceSpan);
            }
        }
        return default;
    }
}
