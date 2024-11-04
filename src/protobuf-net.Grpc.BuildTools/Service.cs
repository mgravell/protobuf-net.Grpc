using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ProtoBuf.Grpc.BuildTools;

internal record class Service(LocationSnapshot Location, string Name, string Route, string Namespace, string? OuterType,
    ImmutableArray<Operation> Operations, Service.ServiceFlags Flags)
{
    public bool IsInvalid => (Flags & ServiceFlags.Invalid) != 0;

    [Flags]
    public enum ServiceFlags
    {
        None = 0,
        Invalid = 1 << 0,
        Generic = 1 << 1,
        Partial = 1 << 2,
    }

    public static Service? TryCreate(ISymbol? symbol)
    {
        if (symbol is not INamedTypeSymbol serviceType)
        {
            return null;
        }

        // look for [Service] or [ServiceContract]
        var route = GetServiceRoute(serviceType);
        if (route is null)
        {
            return null;
        }

        ServiceFlags flags = ServiceFlags.None;
        if (serviceType.IsGenericType)
        {
            flags |= ServiceFlags.Generic | ServiceFlags.Invalid;
        }

        var operations = Operation.Create(serviceType);
        foreach (var operation in operations )
        {
            if (operation.IsInvalid)
            {
                flags |= ServiceFlags.Invalid;
            }
            break;
        }

        return new Service(
            LocationSnapshot.Create(serviceType),
            serviceType.Name, route,
            serviceType.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ?? serviceType.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ?? "",
            serviceType.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            operations, flags);
    }

    private static string? GetServiceRoute(INamedTypeSymbol type)
    {
        if (type is not null)
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (IsServiceRoute(attribute, out var route))
                {
                    return route ?? type.Name;
                }
            }
        }
        return null;
    }

    private static bool IsServiceRoute(AttributeData attribute, out string? route)
    {
        route = null;
        if (attribute.AttributeClass is
            {
                Name: "ServiceAttribute",
                IsGenericType: false,
                ContainingType: null,
                ContainingNamespace:
                {
                    Name: "Configuration",
                    ContainingNamespace:
                    {
                        Name: "Grpc",
                        ContainingNamespace:
                        {
                            Name: "ProtoBuf",
                            ContainingNamespace.IsGlobalNamespace: true,
                        }
                    }
                }
            })
        {
            return true;
        }

        if (attribute.AttributeClass is
            {
                Name: "ServiceContractAttribute",
                IsGenericType: false,
                ContainingType: null,
                ContainingNamespace:
                {
                    Name: "ServiceModel",
                    ContainingNamespace:
                    {
                        Name: "System",
                        ContainingNamespace.IsGlobalNamespace: true,
                    }
                }
            })
        {
            return true;
        }

        return false;
    }
}
