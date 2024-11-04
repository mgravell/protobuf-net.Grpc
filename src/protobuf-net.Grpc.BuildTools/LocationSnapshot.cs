using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ProtoBuf.Grpc.BuildTools;

internal readonly record struct LocationSnapshot(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public override string ToString()
    {
        if (FilePath is null) return "(no location)";

        if (LineSpan.Start.Line == LineSpan.End.Line)
        {
            // single-line
            return $"{FilePath} L{LineSpan.Start.Line}#{LineSpan.Start.Character}-{LineSpan.End.Character}";
        }
        // multi-line
        return $"{FilePath} L{LineSpan.Start.Line}#{LineSpan.Start.Character}-L{LineSpan.End.Line}#{LineSpan.End.Character}";
    }

    public static implicit operator Location?(LocationSnapshot value) => value.AsLocation();

    public Location? AsLocation() => FilePath is null ? null : Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationSnapshot Create(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            var mapped = location.GetMappedLineSpan();
            if (mapped.IsValid && mapped.Path is not null)
            {
                return new LocationSnapshot(mapped.Path, location.SourceSpan, mapped.Span);
            }
            var path = location.SourceTree?.FilePath;
            if (path is not null)
            {
                return new LocationSnapshot(path, location.SourceSpan, mapped.Span);
            }
        }
        return default;
    }
}
