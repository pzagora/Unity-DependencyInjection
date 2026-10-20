using System;

namespace DependencyInjection
{
    /// <summary>
    /// Opts a type out of automatic <see cref="Binder"/> weaving. Apply it to a type that manages its own
    /// binding lifecycle (for example binding in a constructor or unbinding on <c>Dispose</c>) so the
    /// injection weaver leaves its <c>Awake</c>/<c>OnDestroy</c> untouched.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ManualBind : Attribute
    {
    }
}
