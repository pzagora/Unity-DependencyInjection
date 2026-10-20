using System;
using UnityEngine.Scripting;

namespace Commons
{
    /// <summary>
    /// Marks a field or a method to have a dependency injected into, once, by <see cref="Binder"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public class Inject : PreserveAttribute
    {
        /// <summary>
        /// Allows defining a type by which dependency should be injected. Defaults to field type
        /// </summary>
        public readonly Type type;

        public Inject()
        {
        }

        public Inject(Type type)
        {
            this.type = type;
        }
    }
}