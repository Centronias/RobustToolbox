using Microsoft.CodeAnalysis;

namespace Robust.Shared.EntitySystemSubscriptionsGenerator;

public sealed record KnownTypesInfo(
    INamedTypeSymbol EntityUidSymbol,
    INamedTypeSymbol EntitySymbol,
    INamedTypeSymbol EntitySessionEventArgsSymbol,
    INamedTypeSymbol IComponentSymbol,
    INamedTypeSymbol IEntitySystemSymbol,
    INamedTypeSymbol GenerateEventSubscriptionsAnnotationSymbol
)
{
    private const string IEntitySystemTypeName = "Robust.Shared.GameObjects.IEntitySystem";
    private const string EntityUidTypeName = "Robust.Shared.GameObjects.EntityUid";
    private const string EntityTypeName = "Robust.Shared.GameObjects.Entity`1";
    private const string EntitySessionEventArgsTypeName = "Robust.Shared.GameObjects.EntitySessionEventArgs";
    private const string IComponentTypeName = "Robust.Shared.GameObjects.IComponent";

    public const string GenerateEventSubscriptionsAttributeName =
        "Robust.Shared.Analyzers.GenerateEventSubscriptionsAttribute";

    public const string LocalSubscriptionMemberAttributeName =
        "Robust.Shared.Analyzers.LocalEventSubscriptionAttribute";

    public const string NetworkSubscriptionMemberAttributeName =
        "Robust.Shared.Analyzers.NetworkEventSubscriptionAttribute";

    public const string AllSubscriptionMemberAttributeName = "Robust.Shared.Analyzers.AllEventsSubscriptionAttribute";

    public const string CallAfterSubscriptionsAttributeName =
        "Robust.Shared.Analyzers.CallAfterSubscriptionsAttribute";

    public static KnownTypesInfo Get(Compilation compilation)
    {
        return new KnownTypesInfo(
            GetType(compilation, EntityUidTypeName),
            GetType(compilation, EntityTypeName),
            GetType(compilation, EntitySessionEventArgsTypeName),
            GetType(compilation, IComponentTypeName),
            GetType(compilation, IEntitySystemTypeName),
            GetType(compilation, GenerateEventSubscriptionsAttributeName)
        );
    }

    private static INamedTypeSymbol GetType(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName(name) ?? throw new Exception($"Failed to get type \"{name}\"");
}
