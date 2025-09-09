using System;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;

namespace Robust.Shared.Analyzers;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
[BaseTypeRequired(typeof(IEntitySystem))]
public sealed class GenerateEventSubscriptionsAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CallAfterSubscriptions : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LocalEventSubscription : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class NetworkEventSubscription : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class EventSubscription : Attribute;
