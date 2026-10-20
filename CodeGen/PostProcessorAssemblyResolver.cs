using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Commons.DependencyInjection.CodeGen
{
    internal sealed class PostProcessorAssemblyResolver : IAssemblyResolver
    {
        private readonly string[] _references;
        private readonly Dictionary<string, AssemblyDefinition> _cache = new();

        public PostProcessorAssemblyResolver(ICompiledAssembly compiledAssembly)
        {
            _references = compiledAssembly.References;
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
            => Resolve(name, new ReaderParameters(ReadingMode.Deferred));

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(name.Name, out var cached))
                    return cached;

                var path = FindPath(name.Name);
                if (path == null)
                    return null;

                parameters.AssemblyResolver ??= this;
                parameters.ReadSymbols = false;

                var definition = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path)), parameters);
                _cache[name.Name] = definition;
                return definition;
            }
        }

        private string FindPath(string assemblyName)
        {
            var fileName = assemblyName + ".dll";

            foreach (var reference in _references)
            {
                if (string.Equals(Path.GetFileName(reference), fileName, StringComparison.OrdinalIgnoreCase))
                    return reference;
            }

            foreach (var reference in _references)
            {
                var directory = Path.GetDirectoryName(reference);
                if (directory == null)
                    continue;

                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public void Dispose()
        {
            lock (_cache)
            {
                foreach (var definition in _cache.Values)
                    definition.Dispose();

                _cache.Clear();
            }
        }
    }
}
