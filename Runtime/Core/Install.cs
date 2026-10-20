using System;

namespace Commons
{
    /// <summary>
    /// Marks a class to have itself installed as a dependency by <see cref="Binder"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public class Install : Attribute
    {
        /// <summary>
        /// Allows defining a type by which dependency should be installed. Defaults to class type
        /// </summary>
        public readonly Type type;

        public Install() : this(null)
        {
        }

        public Install(Type type)
        {
            this.type = type;
        }
    }
}