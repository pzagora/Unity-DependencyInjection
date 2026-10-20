using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;

namespace Commons
{
    /// <summary>
    /// Handles class fields marked by <see cref="Inject"/> or <see cref="Track"/> and classes by <see cref="Install"/> dependency binding
    /// </summary>
    public static class Binder
    {
        private const BindingFlags BINDINGS = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class Listener
        {
            public object target;
            public FieldInfo field;
            public MethodInfo method;

            public void Call(object dependency)
            {
                if (method != null && dependency != null)
                {
                    method.Invoke(target, new object[] { dependency });
                }

                if (field != null)
                {
                    field.SetValue(target, dependency);
                }
            }
        }

        private class ListenerList : List<Listener>
        {
            public void Call(object dependency) 
                => ForEach(listener => listener.Call(dependency));

            public void Remove(object target) 
                => RemoveAll(listener => listener.target == target);
        }

        private class DependencyList : List<object>
        {
        }

        private class InstallData
        {
            public Type type;
        }

        private class InjectData
        {
            public Type type;
            public FieldInfo field;
            public MethodInfo method;
            public bool tracking;
        }

        private class ReflectionData
        {
            public List<InstallData> installs;
            public List<InjectData> injects;
        }

        private static Dictionary<Type, DependencyList> _dependencyLists = new();
        private static Dictionary<Type, ListenerList> _injectorLists = new();
        private static Dictionary<Type, ListenerList> _trackerLists = new();
        private static Dictionary<Type, ReflectionData> _reflections = new();
        
        /// <summary>
        /// Binds <paramref name="target"/> by handling <see cref="Inject"/>s and <see cref="Install"/>s, defined by assigned attributes.
        /// </summary>
        public static void Bind(object target)
        {
            var type = target.GetType();
            var list = EnsureReflection(type);

            if (list.installs != null)
            {
                foreach (var install in list.installs)
                {
                    Install(target, install);
                }
            }

            if (list.injects != null)
            {
                foreach (var inject in list.injects)
                {
                    Inject(target, inject);
                }
            }
        }

        /// <summary>
        /// Unbinds <paramref name="target"/> by clearing Injects and Installs, defined by assigned attributes.
        /// </summary>
        public static void Unbind(object target)
        {
            var type = target.GetType();
            var list = EnsureReflection(type);

            if (list.installs != null)
            {
                foreach (var install in list.installs)
                {
                    Uninstall(target, install);
                }
            }

            if (list.injects != null)
            {
                foreach (var inject in list.injects)
                {
                    Uninject(target, inject);
                }
            }
        }

        /// <summary>
        /// Manually Installs <paramref name="target"/> as Dependency of <paramref name="type"/> .
        /// </summary>
        public static void Install(object target, Type type)
        {
            AddDependency(type, target);
            RefreshDependency(type);
        }

        /// <summary>
        /// Manually Installs <paramref name="target"/> as Dependency of Type <typeparamref name="T"/>.
        /// </summary>
        public static void Install<T>(T target) 
            => Install(target, typeof(T));

        /// <summary>
        /// Uninstalls manually assigned Dependency <paramref name="target"/> of <paramref name="type"/>.
        /// </summary>
        public static void Uninstall(object target, Type type)
        {
            if (RemoveDependency(type, target))
            {
                RefreshDependency(type);
            }
        }

        /// <summary>
        /// Uninstalls manually assigned <paramref name="target"/> Dependency of Type <typeparamref name="T"/>.
        /// </summary>
        public static void Uninstall<T>(T target) 
            => Uninstall(target, typeof(T));

        private static void Install(object target, InstallData install) 
            => Install(target, install.type);

        private static void Uninstall(object target, InstallData install) 
            => Uninstall(target, install.type);

        private static void Inject(object target, InjectData inject)
        {
            var listener = new Listener
            {
                target = target,
                field = inject.field,
                method = inject.method
            };

            var injected = RefreshListener(inject.type, listener);

            if (inject.tracking)
                AddUpdater(inject.type, listener);
            else if (!injected) 
                AddInjector(inject.type, listener);
        }

        private static void Uninject(object target, InjectData inject)
        {
            RemoveInjector(inject.type, target);
            RemoveTracker(inject.type, target);

            if (inject.field != null)
                inject.field.SetValue(target, null);
        }

        private static bool RefreshListener(Type type, Listener listener)
        {
            if (TryGetDependencies(type, out var dependencies) && dependencies.TryGetLast(out var dependency))
            {
                listener.Call(dependency);
                return true;
            }
            return false;
        }

        private static void RefreshDependency(Type type)
        {
            if (!TryGetDependencies(type, out var dependencies) || !dependencies.TryGetLast(out var dependency))
            {
                dependency = null;
            }

            if (TryGetUpdaters(type, out var updaters))
            {
                updaters.Call(dependency);
            }

            if (TryGetInjectors(type, out var injectors))
            {
                injectors.Call(dependency);
                injectors.Clear();

                RemoveInjectors(type);
            }
        }

        #region Dependencies
        private static void AddDependency(Type type, object dependency) 
            => EnsureDependencies(type).Add(dependency);

        private static bool RemoveDependency(Type type, object dependency)
        {
            if (!TryGetDependencies(type, out var dependencies))
                return false;
            
            var changed = dependencies.Remove(dependency);

            if (dependencies.IsEmpty())
            {
                RemoveDependencies(type);
            }

            return changed;
        }

        private static bool TryGetDependencies(Type type, out DependencyList dependencies) 
            => _dependencyLists.TryGetValue(type, out dependencies);

        private static DependencyList EnsureDependencies(Type type)
        {
            if (!_dependencyLists.TryGetValue(type, out var dependencies))
                _dependencyLists[type] = dependencies = new DependencyList();
            
            return dependencies;
        }

        private static void RemoveDependencies(Type type) 
            => _dependencyLists.Remove(type);

        #endregion

        #region Updaters
        private static void AddUpdater(Type type, Listener updater) 
            => EnsureUpdaters(type).Add(updater);

        private static void RemoveTracker(Type type, object updater)
        {
            if (!TryGetUpdaters(type, out var updaters))
                return;
            
            updaters.Remove(updater);
            if (updaters.IsEmpty())
            {
                RemoveTrackers(type);
            }
        }

        private static bool TryGetUpdaters(Type type, out ListenerList updaters) 
            => _trackerLists.TryGetValue(type, out updaters);

        private static ListenerList EnsureUpdaters(Type type)
        {
            if (!_trackerLists.TryGetValue(type, out var updaters))
                _trackerLists[type] = updaters = new ListenerList();
            
            return updaters;
        }

        private static void RemoveTrackers(Type type) 
            => _trackerLists.Remove(type);

        #endregion

        #region Injectors
        private static void AddInjector(Type type, Listener injector) 
            => EnsureInjectors(type).Add(injector);

        private static void RemoveInjector(Type type, object injector)
        {
            if (!TryGetInjectors(type, out var injectors))
                return;

            injectors.Remove(injector);
            if (injectors.IsEmpty())
            {
                RemoveInjectors(type);
            }
        }

        private static bool TryGetInjectors(Type type, out ListenerList injectors) 
            => _injectorLists.TryGetValue(type, out injectors);

        private static ListenerList EnsureInjectors(Type type)
        {
            if (!_injectorLists.TryGetValue(type, out var injectors))
                _injectorLists[type] = injectors = new ListenerList();
            
            return injectors;
        }

        private static void RemoveInjectors(Type type) 
            => _injectorLists.Remove(type);

        #endregion

        private static ReflectionData EnsureReflection(Type type)
        {
            if (!_reflections.TryGetValue(type, out var result))
                _reflections[type] = result = CreateReflection(type);
            
            return result;
        }

        private static ReflectionData CreateReflection(Type type)
        {
            var injects = CreateInjectDataCollection(type);
            var installs = CreateInstallDataCollection(type);

            // Special case, for external installs
            if (installs == null && injects == null)
            {   
                installs = new List<InstallData> { new() { type = type } };
            }

            return new ReflectionData
            {
                installs = installs,
                injects = injects
            };
        }

        private static List<InstallData> CreateInstallDataCollection(Type type)
        {
            var result = new List<InstallData>();

            CreateInstallDataCollection(type, result);

            return result.Any() ? result : null;
        }

        private static void CreateInstallDataCollection(Type type, List<InstallData> result)
        {
            foreach (var attribute in type.GetCustomAttributes<Install>())
            {
                var data = new InstallData
                {
                    type = attribute.type ?? type
                };

                result.Add(data);
            }

            foreach (var item in type.GetInterfaces())
            {
                CreateInstallDataCollection(item, result);
            }

            if (type.BaseType != null)
            {
                CreateInstallDataCollection(type.BaseType, result);
            }
        }

        private static List<InjectData> CreateInjectDataCollection(Type type)
        {
            var fields = type.GetAllFields(BINDINGS);
            var result = fields.Select(CreateInjectData).Where(inject => inject != null).ToList();
            var methods = type.GetAllMethods(BINDINGS);
            result.AddRange(methods.Select(CreateInjectData).Where(inject => inject != null));

            return result.Any() ? result : null;
        }

        private static InjectData CreateInjectData(FieldInfo field)
        {
            if (!field.TryGetCustomAttribute<Inject>(out var attribute)) 
                return null;
            
            var injectType = attribute.type ?? field.FieldType;

            return new InjectData
            {
                type = injectType,
                field = field,
                tracking = attribute is Track
            };
        }

        private static InjectData CreateInjectData(MethodInfo method)
        {
            if (!method.TryGetMethodParameterType(out var parameterType) ||
                !method.TryGetCustomAttribute<Inject>(out var attribute)) 
                return null;
            
            var injectType = attribute.type ?? parameterType;

            return new InjectData
            {
                type = injectType,
                method = method,
                tracking = attribute is Track
            };
        }

        private static void Clear()
        {
            _dependencyLists = new Dictionary<Type, DependencyList>();
            _injectorLists = new Dictionary<Type, ListenerList>();
            _trackerLists = new Dictionary<Type, ListenerList>();
            _reflections = new Dictionary<Type, ReflectionData>();
        }

#if UNITY_EDITOR
        [DidReloadScripts]
        private static void OnReloadScripts()
        {
            Clear();

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            return;

            void OnPlayModeStateChanged(PlayModeStateChange change)
            {
                if (change == PlayModeStateChange.ExitingPlayMode)
                {
                    Clear();
                }
            }
        }
#endif
    }
}