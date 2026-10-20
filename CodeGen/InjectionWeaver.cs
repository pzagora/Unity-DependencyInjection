using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace DependencyInjection.CodeGen
{
    /// <summary>
    /// ILPostProcessor that weaves <see cref="DependencyInjection.Binder"/> calls into injectable <c>MonoBehaviour</c>s so that
    /// explicit <c>Binder.Bind(this)</c> / <c>Binder.Unbind(this)</c> are not required. A type is woven when it derives
    /// from <c>MonoBehaviour</c>, declares an <c>[Inject]</c>/<c>[Track]</c> member or is <c>[Install]</c>-marked, is not
    /// <c>[ManualBind]</c>, and neither it nor a base type already binds. Weaving is additive: a type whose chain already
    /// binds is skipped, so the weaver coexists with existing manual binding.
    /// </summary>
    public sealed class InjectionWeaver : ILPostProcessor
    {
        private const string TARGET_DEPENDENCY = "DependencyInjection";
        private const string INJECT_ATTRIBUTE = "DependencyInjection.Inject";
        private const string INSTALL_ATTRIBUTE = "DependencyInjection.Install";
        private const string MANUAL_BIND_ATTRIBUTE = "DependencyInjection.ManualBind";
        private const string MONO_BEHAVIOUR = "UnityEngine.MonoBehaviour";
        private const string BINDER_TYPE = "DependencyInjection.Binder";
        private const string AWAKE = "Awake";
        private const string ON_DESTROY = "OnDestroy";
        private const string BIND = "Bind";
        private const string UNBIND = "Unbind";

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name == TARGET_DEPENDENCY)
                return false;

            return compiledAssembly.References.Any(reference =>
                Path.GetFileNameWithoutExtension(reference) == TARGET_DEPENDENCY);
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();

            using var resolver = new PostProcessorAssemblyResolver(compiledAssembly);
            using var assembly = ReadAssembly(compiledAssembly, resolver);
            var module = assembly.MainModule;

            if (!TryResolveBinder(module, resolver, out var bind, out var unbind))
                return new ILPostProcessResult(null, diagnostics);

            var weaved = false;
            foreach (var type in module.GetAllTypes())
                weaved |= WeaveType(type, module, bind, unbind, diagnostics);

            return weaved
                ? new ILPostProcessResult(WriteAssembly(assembly), diagnostics)
                : new ILPostProcessResult(null, diagnostics);
        }

        private static AssemblyDefinition ReadAssembly(ICompiledAssembly compiledAssembly, IAssemblyResolver resolver)
        {
            var symbols = compiledAssembly.InMemoryAssembly.PdbData;
            var parameters = new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadingMode = ReadingMode.Immediate,
                InMemory = true
            };

            if (symbols != null && symbols.Length > 0)
            {
                parameters.ReadSymbols = true;
                parameters.SymbolStream = new MemoryStream(symbols);
                parameters.SymbolReaderProvider = new PortablePdbReaderProvider();
            }

            return AssemblyDefinition.ReadAssembly(new MemoryStream(compiledAssembly.InMemoryAssembly.PeData), parameters);
        }

        private static InMemoryAssembly WriteAssembly(AssemblyDefinition assembly)
        {
            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            var parameters = new WriterParameters
            {
                SymbolStream = pdb,
                WriteSymbols = true,
                SymbolWriterProvider = new PortablePdbWriterProvider()
            };

            assembly.Write(pe, parameters);
            return new InMemoryAssembly(pe.ToArray(), pdb.ToArray());
        }

        private static bool TryResolveBinder(ModuleDefinition module, IAssemblyResolver resolver, out MethodReference bind, out MethodReference unbind)
        {
            bind = null;
            unbind = null;

            var reference = module.AssemblyReferences.FirstOrDefault(a => a.Name == TARGET_DEPENDENCY);
            var binder = reference == null ? null : resolver.Resolve(reference)?.MainModule.GetType(BINDER_TYPE);
            if (binder == null)
                return false;

            var bindMethod = FindBinderMethod(binder, BIND);
            var unbindMethod = FindBinderMethod(binder, UNBIND);
            if (bindMethod == null || unbindMethod == null)
                return false;

            bind = module.ImportReference(bindMethod);
            unbind = module.ImportReference(unbindMethod);
            return true;
        }

        private static MethodDefinition FindBinderMethod(TypeDefinition binder, string name)
            => binder.Methods.FirstOrDefault(m =>
                m.Name == name && m.IsStatic && !m.HasGenericParameters && m.Parameters.Count == 1);

        private bool WeaveType(TypeDefinition type, ModuleDefinition module, MethodReference bind, MethodReference unbind, List<DiagnosticMessage> diagnostics)
        {
            if (!type.IsClass || IsStatic(type))
                return false;
            if (!DerivesFromMonoBehaviour(type))
                return false;
            if (HasAttribute(type, MANUAL_BIND_ATTRIBUTE))
                return false;
            if (!IsInjectable(type))
                return false;
            if (ChainBinds(type))
                return false;

            var boundAwake = EnsureLifecycle(type, module, AWAKE, bind, atEnd: true, diagnostics);
            var boundDestroy = EnsureLifecycle(type, module, ON_DESTROY, unbind, atEnd: false, diagnostics);
            return boundAwake || boundDestroy;
        }

        private static bool EnsureLifecycle(TypeDefinition type, ModuleDefinition module, string name, MethodReference call, bool atEnd, List<DiagnosticMessage> diagnostics)
        {
            var method = DeclaredLifecycle(type, name);
            if (method != null)
            {
                InsertCall(method, call, atEnd);
                return true;
            }

            var baseMethod = BaseLifecycle(type, name);
            if (baseMethod != null && !IsCallable(baseMethod))
            {
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData =
                        $"[InjectionWeaver] Cannot auto-bind '{type.FullName}': base '{baseMethod.DeclaringType.Name}.{name}()' " +
                        $"is inaccessible and would be shadowed. Add an explicit '{name}' that calls base, or mark the type [ManualBind]."
                });
                return false;
            }

            type.Methods.Add(SynthesizeLifecycle(module, name, call, atEnd, baseMethod));
            return true;
        }

        private static MethodDefinition SynthesizeLifecycle(ModuleDefinition module, string name, MethodReference call, bool atEnd, MethodDefinition baseMethod)
        {
            var method = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.HideBySig, module.TypeSystem.Void);
            var il = method.Body.GetILProcessor();
            var baseCall = baseMethod == null ? null : module.ImportReference(baseMethod);

            if (atEnd)
            {
                EmitBaseCall(il, baseCall);
                EmitCall(il, call);
            }
            else
            {
                EmitCall(il, call);
                EmitBaseCall(il, baseCall);
            }

            il.Emit(OpCodes.Ret);
            return method;
        }

        private static void InsertCall(MethodDefinition method, MethodReference call, bool atEnd)
        {
            var il = method.Body.GetILProcessor();

            if (atEnd)
            {
                foreach (var ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
                {
                    il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(ret, il.Create(OpCodes.Call, call));
                }
            }
            else
            {
                var first = method.Body.Instructions[0];
                il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, il.Create(OpCodes.Call, call));
            }
        }

        private static void EmitCall(ILProcessor il, MethodReference call)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, call);
        }

        private static void EmitBaseCall(ILProcessor il, MethodReference baseCall)
        {
            if (baseCall == null)
                return;

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, baseCall);
        }

        private static MethodDefinition DeclaredLifecycle(TypeDefinition type, string name)
            => type.Methods.FirstOrDefault(m => m.Name == name && !m.IsStatic && m.Parameters.Count == 0);

        private static MethodDefinition BaseLifecycle(TypeDefinition type, string name)
        {
            for (var baseType = type.BaseType?.Resolve(); baseType != null; baseType = baseType.BaseType?.Resolve())
            {
                var method = DeclaredLifecycle(baseType, name);
                if (method != null)
                    return method;
            }

            return null;
        }

        private static bool ChainBinds(TypeDefinition type)
        {
            for (var current = type; current != null; current = current.BaseType?.Resolve())
            {
                var awake = DeclaredLifecycle(current, AWAKE);
                if (awake != null && CallsBinder(awake))
                    return true;
            }

            return false;
        }

        private static bool CallsBinder(MethodDefinition method)
        {
            if (!method.HasBody)
                return false;

            return method.Body.Instructions.Any(instruction =>
                (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is MethodReference called &&
                called.DeclaringType.FullName == BINDER_TYPE &&
                (called.Name == BIND || called.Name == UNBIND));
        }

        private static bool IsInjectable(TypeDefinition type)
        {
            if (HasInstall(type))
                return true;

            return type.Fields.Any(HasInject) || type.Methods.Any(HasInject);
        }

        private static bool HasInstall(TypeDefinition type)
        {
            if (HasAttribute(type, INSTALL_ATTRIBUTE))
                return true;

            return type.Interfaces.Any(i =>
            {
                var resolved = i.InterfaceType.Resolve();
                return resolved != null && HasAttribute(resolved, INSTALL_ATTRIBUTE);
            });
        }

        private static bool HasInject(ICustomAttributeProvider member)
            => member.CustomAttributes.Any(attribute => IsOrDerivesFrom(attribute.AttributeType, INJECT_ATTRIBUTE));

        private static bool DerivesFromMonoBehaviour(TypeDefinition type)
            => IsOrDerivesFrom(type.BaseType, MONO_BEHAVIOUR);

        private static bool IsOrDerivesFrom(TypeReference type, string fullName)
        {
            for (var current = type; current != null; current = current.Resolve()?.BaseType)
            {
                if (current.FullName == fullName)
                    return true;
            }

            return false;
        }

        private static bool HasAttribute(ICustomAttributeProvider provider, string fullName)
            => provider.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == fullName);

        private static bool IsStatic(TypeDefinition type)
            => type.IsAbstract && type.IsSealed;

        private static bool IsCallable(MethodDefinition method)
            => !method.IsPrivate && !method.IsAssembly;
    }
}
