using System;

namespace Commons
{
    /// <summary>
    /// Marks a field or a method to have a dependency kept updated into, by <see cref="Binder"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public class Track : Inject
    {
        public Track()
        {
        }

        public Track(Type type) : base(type)
        {
        }
    }
}