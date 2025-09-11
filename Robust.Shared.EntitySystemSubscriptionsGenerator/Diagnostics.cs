using Microsoft.CodeAnalysis;

namespace Robust.Shared.EntitySystemSubscriptionsGenerator;

public static class Diagnostics
{
    private const DiagnosticSeverity Sev = DiagnosticSeverity.Warning;

    public static readonly DiagnosticDescriptor BadParameterType = new(
        "cent0",
        "Incorrect Parameter Type",
        "Parameter {0} must be of type {1}",
        "Usage",
        Sev,
        true
    );

    public static readonly DiagnosticDescriptor BadNullableParameter = new(
        "cent1",
        "Incorrect Parameter Type",
        "Parameter {0} must not be nullable",
        "Usage",
        Sev,
        true
    );

    public static readonly DiagnosticDescriptor BadMethodSignature = new(
        "cent2",
        "Invalid Method Signature",
        "Method signature is incompatible with required delegate types for \"{0}\" subscription",
        "Usage",
        Sev,
        true
    );

    public static readonly DiagnosticDescriptor BadCallAfterMethodSignature = new(
        "cent2_1",
        "Invalid Method Signature",
        "Methods annotated with {0} must have no parameters",
        "Usage",
        Sev,
        true
    );

    public static readonly DiagnosticDescriptor MultipleAnnotations = new(
        "cent3",
        "Multiple Subscription Annotations",
        "Only one subscription annotation can be applied to a single method",
        "Usage",
        Sev,
        true
    );

    public static readonly DiagnosticDescriptor NotIEntitySystem = new(
        "cent4",
        "Invalid Annotation Target",
        "Types annotated with {0} must implement {1}",
        "Usage",
        Sev,
        true
    );

    public static readonly DiagnosticDescriptor NotPartial = new(
        "cent5",
        "Class must be Declared as Partial",
        "Types annotated with {0} must be partial",
        "Usage",
        Sev,
        true
    );

    public static readonly DiagnosticDescriptor AnnotatedMethodInNotAnnotatedType = new(
        "cent6",
        "Annotated Method is not in correctly Annotated Type",
        "Method with generator annotation must be in a {0} annotated with {1}",
        "Usage",
        Sev,
        true
    );
}
