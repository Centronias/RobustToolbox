using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Robust.Roslyn.Shared;
using Robust.Roslyn.Shared.Helpers;

namespace Robust.Shared.EntitySystemSubscriptionsGenerator;

[Generator(LanguageNames.CSharp)]
public class SubscriptionGenerator : IIncrementalGenerator
{
    private const string ClassAttributeName = "Robust.Shared.Analyzers.GenerateEventSubscriptionsAttribute";
    private const string LocalSubscriptionMemberAttributeName = "Robust.Shared.Analyzers.LocalEventSubscription";
    private const string IEntitySystemTypeName = "Robust.Shared.GameObjects.IEntitySystem";
    private const string EntityUidTypeName = "Robust.Shared.GameObjects.EntityUid";
    private const string EntityTypeName = "Robust.Shared.GameObjects.Entity`1";
    private const string IComponentTypeName = "Robust.Shared.GameObjects.IComponent";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }

        var knownTypes = context.CompilationProvider.Select((compilation, _) =>
            new KnownTypesInfo(
                GetType(compilation, EntityUidTypeName),
                GetType(compilation, EntityTypeName),
                GetType(compilation, IComponentTypeName),
                GetType(compilation, IEntitySystemTypeName)
            )
        );
        var annotatedDeclCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            ClassAttributeName,
            (ctx, _) => ctx is TypeDeclarationSyntax,
            (ctx, _) => ((INamedTypeSymbol)ctx.TargetSymbol, (TypeDeclarationSyntax)ctx.TargetNode)
        );

        context.RegisterImplementationSourceOutput(
            annotatedDeclCandidates.Combine(knownTypes).Select(ParseAnnotatedDecls),
            AddSource
        );
    }

    private static AnnotatedTypeDeclInfo ParseAnnotatedDecls(
        ((INamedTypeSymbol, TypeDeclarationSyntax), KnownTypesInfo) values,
        CancellationToken cancellationToken
    )
    {
        var ((symbol, syntax), types) = values;
        var partialTypeInfo = PartialTypeInfo.FromSymbol(symbol, syntax);

        if (!symbol.AllInterfaces.Contains(types.IEntitySystemSymbol, SymbolEqualityComparer.IncludeNullability))
        {
            // Is annotated, but doesn't implement the interface.
            return new InvalidAnnotatedNonEntitySystemDecl(partialTypeInfo);
        }

        if (!partialTypeInfo.IsValid)
        {
            // Is annotated, but not partial.
            return new InvalidAnnotatedNonPartialEntitySytemDecl(partialTypeInfo);
        }

        var methods = ImmutableArray.CreateBuilder<MethodInfo>();
        foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (method is null ||
                !AttributeHelper.HasAttribute(
                    method,
                    LocalSubscriptionMemberAttributeName,
                    out var attribute
                ))
                continue;
            methods.Add(MethodInfo.Parse(method, attribute, types));
        }

        return new ValidAnnotatedEntitySystemInfo(
            partialTypeInfo,
            EquatableArray<MethodInfo>.FromImmutableArray(methods.ToImmutable())
        );
    }

    private static void AddSource(
        SourceProductionContext ctx,
        AnnotatedTypeDeclInfo typeDecl
    )
    {
        ValidAnnotatedEntitySystemInfo info;
        switch (typeDecl)
        {
            case InvalidAnnotatedNonEntitySystemDecl nonEntSys:
                ctx.ReportDiagnostic(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "todoCent01",
                            "Type is annotated but isn't an entity system",
                            "Remove the annotation, probably",
                            "Usage",
                            DiagnosticSeverity.Error,
                            true
                        ),
                        nonEntSys.PartialTypeInfo.SyntaxLocation,
                        nonEntSys.PartialTypeInfo.DisplayName
                    )
                );
                return;
            case InvalidAnnotatedNonPartialEntitySytemDecl nonPartial:
                nonPartial.PartialTypeInfo.CheckPartialDiagnostic(
                    ctx,
                    new DiagnosticDescriptor(
                        "todoCent02",
                        "Type is annotated but isn't partial",
                        "Make it partial, dummy",
                        "Usage",
                        DiagnosticSeverity.Error,
                        true
                    )
                );
                return;
            case ValidAnnotatedEntitySystemInfo validAnnotatedEntitySystemInfo:
                info = validAnnotatedEntitySystemInfo;
                break;
            default:
                throw new("Unreachable");
        }

        foreach (var method in info.Methods.OfType<InvalidMethodInfo>())
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            ctx.ReportDiagnostic(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "todoCent03",
                        method.Title,
                        method.Message,
                        "Usage",
                        DiagnosticSeverity.Error,
                        true
                    ),
                    method.Method.Locations[0],
                    method.Method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                )
            );
        }

        // TODO Collect imports and emit them.
        var builder = new StringBuilder(@"
// <auto-generated />

using Robust.Shared.GameObjects;

");
        info.PartialTypeInfo.WriteHeader(builder);

        builder.AppendLine(@"
{
    [MustCallBase]
    public override void Initialize()
    {
        base.Initialize();
");

        foreach (var method in info.Methods.OfType<ValidMethodInfo>())
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine($@"
        SubscribeLocalEvent<{method.ComponentType.Name}, {method.EventType.Name}>({method.Method.Name});
");
        }

        builder.AppendLine(@"
    }
}
");

        info.PartialTypeInfo.WriteFooter(builder);

        ctx.AddSource(info.PartialTypeInfo.GetGeneratedFileName(), builder.ToString());
    }

    private abstract record AnnotatedTypeDeclInfo;

    private abstract record InvalidAnnotatedTypeDeclInfo : AnnotatedTypeDeclInfo;

    private sealed record InvalidAnnotatedNonEntitySystemDecl(PartialTypeInfo PartialTypeInfo)
        : InvalidAnnotatedTypeDeclInfo;

    private sealed record InvalidAnnotatedNonPartialEntitySytemDecl(PartialTypeInfo PartialTypeInfo)
        : InvalidAnnotatedTypeDeclInfo;

    private sealed record ValidAnnotatedEntitySystemInfo(
        PartialTypeInfo PartialTypeInfo,
        EquatableArray<MethodInfo> Methods
    ) : AnnotatedTypeDeclInfo;

    private sealed record KnownTypesInfo(
        INamedTypeSymbol EntityUidSymbol,
        INamedTypeSymbol EntitySymbol,
        INamedTypeSymbol IComponentSymbol,
        INamedTypeSymbol IEntitySystemSymbol
    );

    private abstract record MethodInfo
    {
        public static MethodInfo Parse(IMethodSymbol method, AttributeData attribute, KnownTypesInfo types)
        {
            return method.Parameters.Length switch
            {
                2 => ParseAsEntityEventRefHandler(method, types),
                3 => ParseAsComponentEventHandler(method, types),
                _ => new InvalidMethodInfo(
                    method,
                    "Invalid number of parameters",
                    "Annotated method has the wrong number of parameters to be an event subscription."
                )
            };
        }

        private static MethodInfo ParseAsComponentEventHandler(IMethodSymbol method, KnownTypesInfo types)
        {
            // TODO Maybe do this intelligently based on the definition of the delegate

            // Check if the first parameter is `EntityUid`
            if (!SymbolEqualityComparer.IncludeNullability.Equals(
                    method.Parameters[0].Type,
                    types.EntityUidSymbol
                ))
                return new InvalidMethodInfo(method, "Invalid first parameter", "First parameter must be EntityUid");

            // Check if the second parameter is an `IComponent`
            if (method.Parameters[1].Type is not INamedTypeSymbol componentParameterType ||
                SymbolEqualityComparer.IncludeNullability.Equals(componentParameterType, types.IComponentSymbol)
               )
            {
                return new InvalidMethodInfo(
                    method,
                    "Invalid second parameter",
                    "Second parameter must be an IComponent"
                );
            }

            // Get the third parameter. We don't check anything because the delegate type for handlers is not constrained.
            // TODO I'm too dumb to figure out how to check if it's nullable. It should not be nullable.
            if (method.Parameters[2].Type is not INamedTypeSymbol eventParameterType)
            {
                return new InvalidMethodInfo(
                    method,
                    "Invalid third parameter",
                    "Third parameter must be a type which can be used as an event"
                );
            }

            return new ValidMethodInfo(method, componentParameterType, eventParameterType);
        }

        private static MethodInfo ParseAsEntityEventRefHandler(IMethodSymbol method, KnownTypesInfo types)
        {
            // TODO Maybe do this intelligently based on the definition of the delegate

            // Check if the first parameter is `Entity<$entityTypeArg : IComponent>`
            if (method.Parameters[0].Type is not INamedTypeSymbol entityParameterType ||
                !SymbolEqualityComparer.IncludeNullability.Equals(
                    entityParameterType.OriginalDefinition,
                    types.EntitySymbol
                ) ||
                entityParameterType.TypeArguments is not [INamedTypeSymbol entityTypeArg] ||
                SymbolEqualityComparer.IncludeNullability.Equals(entityTypeArg, types.IComponentSymbol)
               )
            {
                return new InvalidMethodInfo(
                    method,
                    "Invalid first parameter",
                    "First parameter must be Entity with valid type argument"
                );
            }

            // Get the second parameter. We don't check anything because the delegate type for handlers is not constrained.
            // TODO I'm too dumb to figure out how to check if it's nullable. It should not be nullable.
            if (method.Parameters[1].Type is not INamedTypeSymbol eventParameterType)
            {
                return new InvalidMethodInfo(
                    method,
                    "Invalid second parameter",
                    "Second parameter must be a type which can be used as an event"
                );
            }

            return new ValidMethodInfo(method, entityTypeArg, eventParameterType);
        }
    }

    private sealed record InvalidMethodInfo(
        IMethodSymbol Method,
        string Title,
        string Message
    ) : MethodInfo;

    private sealed record ValidMethodInfo(
        IMethodSymbol Method,
        INamedTypeSymbol ComponentType,
        INamedTypeSymbol EventType
    ) : MethodInfo;

    private static INamedTypeSymbol GetType(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName(name) ?? throw new Exception($"Failed to get type \"{name}\"");

    private static IncrementalValuesProvider<TOut> Filter<TIn, TOut>(IncrementalValuesProvider<TIn> provider)
        where TOut : TIn where TIn : notnull
    {
        return provider
            .Where(it => it is TOut)
            .Select((it, c) =>
            {
                c.ThrowIfCancellationRequested();
                return (TOut)it;
            });
    }
}
