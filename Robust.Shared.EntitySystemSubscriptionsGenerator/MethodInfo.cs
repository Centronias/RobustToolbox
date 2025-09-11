using Microsoft.CodeAnalysis;
using Robust.Roslyn.Shared;

namespace Robust.Shared.EntitySystemSubscriptionsGenerator;

public abstract record MethodInfo
{
    public static WithDiagnostics<MethodInfo>? Parse(IMethodSymbol method, KnownTypesInfo types)
    {
        if (ParseAsCallAfterMethodOrNull(method, types) is { } callAfter)
            return callAfter;

        if (GetSubscriptionType(method, types) is not { } subscriptionType)
            return null;

        return subscriptionType.FlatMap(subscription => method.Parameters.Length switch
            {
                1 => ParseAsEventHandler(method, subscription),
                2 when subscription is SubscriptionType.All or SubscriptionType.Network =>
                    ParseAsSessionEventHandler(method, subscription, types),
                2 when subscription == SubscriptionType.Local =>
                    ParseAsSessionEventHandler(method, subscription, types)
                        .Or(() => ParseAsEntityEventRefHandler(method, types)),
                3 => ParseAsDirectedEventHandler(method, types),
                _ => new JustDiagnostics<MethodInfo>(
                    Diagnostic.Create(
                        Diagnostics.BadMethodSignature,
                        method.Locations[0],
                        messageArgs: subscription
                    )
                )
            }
        );
    }

    private static WithDiagnostics<MethodInfo>? ParseAsCallAfterMethodOrNull(IMethodSymbol method, KnownTypesInfo types)
    {
        if (!AttributeHelper.HasAttribute(method, KnownTypesInfo.CallAfterSubscriptionsAttributeName, out _))
            return null;

        if (method.Parameters.Length != 0)
        {
            return new JustDiagnostics<MethodInfo>(
                Diagnostic.Create(
                    Diagnostics.BadCallAfterMethodSignature,
                    method.Locations[0],
                    KnownTypesInfo.CallAfterSubscriptionsAttributeName
                )
            );
        }

        return new JustValue<MethodInfo>(new CallAfterMethod(method));
    }

    private static WithDiagnostics<MethodInfo> ParseAsEventHandler(IMethodSymbol method, SubscriptionType subType)
    {
        var eventParamType = GetNotNullParamType(method, 0);
        return eventParamType
            .Map(MethodInfo (ev) => new ParsedEventHandler(method, ev, subType));
    }

    private static WithDiagnostics<MethodInfo> ParseAsSessionEventHandler(
        IMethodSymbol method,
        SubscriptionType subType,
        KnownTypesInfo types
    )
    {
        var eventParamType = GetNotNullParamType(method, 0);
        var sessionParamType = GetParamType(method, 0, types.EntitySessionEventArgsSymbol);

        return eventParamType.Zip(sessionParamType)
            .Map(MethodInfo (it) =>
            {
                var (ev, _) = it;
                return new ParsedSessionEventHandler(method, ev, subType);
            });
    }

    private static WithDiagnostics<MethodInfo> ParseAsDirectedEventHandler(IMethodSymbol method, KnownTypesInfo types)
    {
        var entityUidParamType = GetParamType(method, 0, types.EntityUidSymbol);
        var componentParamType = GetParamType(method, 1, types.IComponentSymbol);
        var eventParamType = GetNotNullParamType(method, 2);

        return entityUidParamType.Zip(componentParamType)
            .Zip(eventParamType)
            .Map(MethodInfo (it) =>
            {
                var ((_, comp), ev) = it;
                return new ParsedDirectedEventHandler(method, comp, ev);
            });
    }

    private static WithDiagnostics<MethodInfo> ParseAsEntityEventRefHandler(IMethodSymbol method, KnownTypesInfo types)
    {
        var componentParamType = GetParamType(method, 0, types.EntitySymbol)
            .FlatMap<INamedTypeSymbol>(entityParamType =>
            {
                if (entityParamType.TypeArguments is not [INamedTypeSymbol typeArg])
                {
                    return new JustDiagnostics<INamedTypeSymbol>(Diagnostic.Create(
                        Diagnostics.BadParameterType,
                        method.Parameters[0].Locations[0],
                        0,
                        types.EntitySymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    ));
                }

                return new JustValue<INamedTypeSymbol>(typeArg);
            });
        var eventParamType = GetNotNullParamType(method, 1);

        return componentParamType
            .Zip(eventParamType)
            .Map(MethodInfo (it) =>
            {
                var (comp, ev) = it;
                return new ParsedDirectedEventHandler(method, comp, ev);
            });
    }

    private static WithDiagnostics<SubscriptionType>? GetSubscriptionType(IMethodSymbol method, KnownTypesInfo types)
    {
        var isAllSubscription =
            AttributeHelper.HasAttribute(method, KnownTypesInfo.AllSubscriptionMemberAttributeName, out _);
        var isLocalSubscription =
            AttributeHelper.HasAttribute(method, KnownTypesInfo.LocalSubscriptionMemberAttributeName, out _);
        var isNetworkSubscription =
            AttributeHelper.HasAttribute(method, KnownTypesInfo.NetworkSubscriptionMemberAttributeName, out _);

        return (isAllSubscription, isLocalSubscription, isNetworkSubscription) switch
        {
            (false, false, false) => null,
            (true, false, false) => new JustValue<SubscriptionType>(SubscriptionType.All),
            (false, true, false) => new JustValue<SubscriptionType>(SubscriptionType.Local),
            (false, false, true) => new JustValue<SubscriptionType>(SubscriptionType.Network),
            _ => new JustDiagnostics<SubscriptionType>(Diagnostic.Create(Diagnostics.MultipleAnnotations,
                method.Locations[0]))
        };
    }

    private static WithDiagnostics<INamedTypeSymbol> GetParamType(
        IMethodSymbol method,
        int position,
        INamedTypeSymbol expected
    )
    {
        if (method.Parameters[position].Type is not INamedTypeSymbol named ||
            !SymbolEqualityComparer.IncludeNullability.Equals(
                method.Parameters[position].Type.OriginalDefinition,
                expected
            ))
        {
            return new JustDiagnostics<INamedTypeSymbol>(
                Diagnostic.Create(
                    Diagnostics.BadParameterType,
                    method.Parameters[position].Locations[0],
                    position,
                    expected.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                )
            );
        }

        return new JustValue<INamedTypeSymbol>(named);
    }

    private static WithDiagnostics<INamedTypeSymbol> GetNotNullParamType(IMethodSymbol method, int position)
    {
        if (method.Parameters[position].Type is not INamedTypeSymbol named ||
            named.SpecialType == SpecialType.System_Nullable_T)
        {
            return new JustDiagnostics<INamedTypeSymbol>(
                Diagnostic.Create(
                    Diagnostics.BadNullableParameter,
                    method.Parameters[position].Locations[0],
                    position
                )
            );
        }

        return new JustValue<INamedTypeSymbol>(named);
    }
}

public sealed record ParsedEventHandler(
    IMethodSymbol Method,
    INamedTypeSymbol EventType,
    SubscriptionType SubscriptionType
) : MethodInfo;

public sealed record ParsedSessionEventHandler(
    IMethodSymbol Method,
    INamedTypeSymbol EventType,
    SubscriptionType SubscriptionType
) : MethodInfo;

public sealed record ParsedDirectedEventHandler(
    IMethodSymbol Method,
    INamedTypeSymbol ComponentType,
    INamedTypeSymbol EventType
) : MethodInfo;

public sealed record CallAfterMethod(IMethodSymbol Method) : MethodInfo;

public enum SubscriptionType
{
    All,
    Network,
    Local,
}
