using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Robust.Shared.EntitySystemSubscriptionsGenerator;

public static class WithDiagnostics
{
    public static void ReportDiagnostics<T>(this WithDiagnostics<T> wd, SourceProductionContext ctx)
    {
        foreach (var diagnostic in wd.Diagnostics)
        {
            ctx.ReportDiagnostic(diagnostic);
        }
    }

    public static bool TryGetValue<T>(this WithDiagnostics<T> wd, [NotNullWhen(true)] out T? value) where T : notnull
    {
        switch (wd)
        {
            case JustValue<T> justValue:
                value = justValue.Value;
                return true;
            case ValueWithDiagnostics<T> valueWithDiagnostics:
                value = valueWithDiagnostics.Value;
                return true;
            default:
                value = default;
                return false;
        }
    }

    public static WithDiagnostics<TRet> Map<T, TRet>(this WithDiagnostics<T> wd, Func<T, TRet> transform) =>
        wd.FlatMap(v => new JustValue<TRet>(transform(v)));

    public static WithDiagnostics<(TA, TB)> Zip<TA, TB>(this WithDiagnostics<TA> a, WithDiagnostics<TB> b) =>
        a.FlatMap(va => b.Map(vb => (va, vb)));

    public static WithDiagnostics<T> Or<T>(this WithDiagnostics<T> wd, Func<WithDiagnostics<T>> alternative) =>
        wd switch
        {
            JustDiagnostics<T> oldErrors => alternative() switch
            {
                JustDiagnostics<T> newErrors => oldErrors.AppendDiagnostics(newErrors.Diagnostics),
                JustValue<T> justValue => justValue,
                ValueWithDiagnostics<T> valueWithDiagnostics => valueWithDiagnostics,
                _ => throw new ArgumentOutOfRangeException()
            },
            JustValue<T> => wd,
            ValueWithDiagnostics<T> => wd,
            _ => throw new ArgumentOutOfRangeException(nameof(wd))
        };

    public static WithDiagnostics<TRet> Lift<T, TRet>(
        this IEnumerable<WithDiagnostics<T>> source,
        Func<IEnumerable<T>, TRet> transform
    ) where T : notnull
    {
        var poisoned = false;
        var values = new List<T>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var wd in source)
        {
            if (poisoned || !wd.TryGetValue(out var value))
            {
                poisoned = true;
            }
            else
            {
                values.Add(value);
            }

            diagnostics.AddRange(wd.Diagnostics);
        }

        if (poisoned)
            return new JustDiagnostics<TRet>(diagnostics.ToImmutable());

        if (diagnostics.Count == 0)
            return new JustValue<TRet>(transform(values));

        return new ValueWithDiagnostics<TRet>(transform(values), diagnostics.ToImmutable());
        ;
    }
}

public abstract record WithDiagnostics<T>
{
    public abstract WithDiagnostics<TRet> FlatMap<TRet>(Func<T, WithDiagnostics<TRet>> transform);
    public abstract WithDiagnostics<T> AppendDiagnostics(IEnumerable<Diagnostic> diagnostics);

    public abstract ImmutableArray<Diagnostic> Diagnostics { get; }
}

public sealed record JustValue<T>(T Value) : WithDiagnostics<T>
{
    public override ImmutableArray<Diagnostic> Diagnostics => ImmutableArray<Diagnostic>.Empty;

    public override WithDiagnostics<TRet> FlatMap<TRet>(Func<T, WithDiagnostics<TRet>> transform) =>
        transform(Value);

    public override WithDiagnostics<T> AppendDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        new ValueWithDiagnostics<T>(Value, [..diagnostics]);
}

public sealed record JustDiagnostics<T>(ImmutableArray<Diagnostic> _diagnostics) : WithDiagnostics<T>
{
    public JustDiagnostics(Diagnostic diagnostic) : this([diagnostic])
    {
    }

    public override ImmutableArray<Diagnostic> Diagnostics => _diagnostics;

    public override WithDiagnostics<TRet> FlatMap<TRet>(Func<T, WithDiagnostics<TRet>> transform) =>
        new JustDiagnostics<TRet>(Diagnostics);

    public override WithDiagnostics<T> AppendDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        new JustDiagnostics<T>([.._diagnostics.Concat(diagnostics)]);
}

public sealed record ValueWithDiagnostics<T>(T Value, ImmutableArray<Diagnostic> _diagnostics) : WithDiagnostics<T>
{
    public override ImmutableArray<Diagnostic> Diagnostics => _diagnostics;

    public override WithDiagnostics<TRet> FlatMap<TRet>(Func<T, WithDiagnostics<TRet>> transform) =>
        transform(Value).AppendDiagnostics(_diagnostics);

    public override WithDiagnostics<T> AppendDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        new ValueWithDiagnostics<T>(Value, [.._diagnostics.Concat(diagnostics)]);
}
