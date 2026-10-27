# Unity Dependency Injection

Attribute-driven dependency injection for Unity. Mark a field or method with `[Inject]` (or `[Track]` to keep receiving updates) and an **ILPostProcessor weaves the binding for you** — you never write `Binder.Bind(this)` for a consumer, on `MonoBehaviour`s and plain classes alike. Providers are marked `[Install]` and registered where you own their lifetime (see below).

Self-contained and plug & play: it has no dependency on any other package, and its Mono.Cecil requirement is declared in `package.json`.

## Installation

Install from the zagora.dev UPM registry — add the scoped registry to `Packages/manifest.json`, then the package; its dependencies install automatically:

```json
"scopedRegistries": [
    { "name": "zagora.dev", "url": "https://upm.zagora.dev", "scopes": [ "com.zagoradev" ] }
],
"dependencies": {
  "com.zagoradev.unity.dependencyinjection": "1.0.3"
}
```

Requires Unity `6000.0+`. Assemblies that use these types reference the `DependencyInjection` assembly in their asmdef; code in the predefined assemblies (`Assembly-CSharp`) sees it automatically.

## How the code generation works

Binding is woven at **compile time** by an `ILPostProcessor` (`Unity.*.CodeGen`, using Mono.Cecil). A type is woven when it declares an `[Inject]` / `[Track]` member and is **neither** `[ManualBind]` **nor** `[Install]`-marked. The weaver rewrites the compiled IL so that `Binder.Bind(this)` and `Binder.Unbind(this)` run automatically:

- **`MonoBehaviour`s** — bind at the end of `Awake`, unbind at the end of `OnDestroy`.
- **Plain classes** — bind at the end of the constructor, unbind at the end of `Dispose`.

The methods are created (calling `base`) when absent. Unbind is placed **last** so the type's own teardown can still use its injected fields — `Unbind` clears them. In a class hierarchy only the top-most injectable type is woven; its `Bind(this)` resolves the whole instance.

**`[Install]` providers are never woven.** A provider is a service you register explicitly, where you control its lifetime — typically an installer `MonoBehaviour` that binds its set of services. The weaver leaves `[Install]` types (and `[ManualBind]` types) alone.

A plain class that uses **`[Track]`** must be `IDisposable` — the unbind is woven into `Dispose`. Otherwise the binder would keep it alive forever (GC can never reclaim a tracked object), so it would leak; the weaver reports this as a build error.

The weaver is **additive and idempotent**: a type that already binds (itself or via a base) is skipped, so it coexists with hand-written binding and never double-binds. Assemblies are only processed if they reference this package.

## Usage

All attributes and `Binder` live in the `DependencyInjection` namespace.

### Consumer — fully automatic

No `Binder` calls at all; the weaver adds them. The same for a `MonoBehaviour` or a plain class:

```csharp
using DependencyInjection;

// MonoBehaviour — bound in Awake, unbound in OnDestroy.
public sealed class Hud : MonoBehaviour
{
    [Inject] private IInventoryService _inventory;
}

// Plain class — bound in the constructor (nothing to unbind for a pure consumer).
public sealed class InventoryPresenter
{
    [Inject] private readonly IInventoryService _inventory;
}
```

### Provider — marked `[Install]`, registered where you own it

`[Install]` marks a type (or an implemented interface) as a provider. The weaver does **not** bind it; you register it where you manage its lifetime — typically an installer that binds a set of services:

```csharp
[Install(typeof(IInventoryService))]
public sealed class InventoryService : IInventoryService { }

// e.g. inside an installer MonoBehaviour:
Binder.Bind(inventoryService);    // registers it as IInventoryService
Binder.Unbind(inventoryService);  // on teardown
```

A convenient pattern is an **installer** base that owns the providers' lifetime — bind them on `Awake`, unbind and dispose them on `OnDestroy`. The package doesn't ship one (that would tie it to your service and base types), but here it is in full. `ValidatedMonoBehaviour` and `IService` are your own project's types (e.g. from Unity Commons); the only DI-specific calls are `Binder.Bind` / `Binder.Unbind`:

```csharp
public abstract class Installer : ValidatedMonoBehaviour
{
    // Subclasses apply [DefaultExecutionOrder(ExecutionOrder)] so providers bind before their consumers.
    protected const int ExecutionOrder = -5;

    private readonly List<IService> _services = new();

    protected virtual void Awake()
        => BindServices();

    protected virtual void OnDestroy()
        => UnbindServices();

    /// <summary>Derived classes override this and call AddService() for each service they register.</summary>
    protected abstract void AddServices();

    protected void AddService(IService service)
        => _services.Add(service);

    private void BindServices()
    {
        AddServices();

        foreach (var service in _services)
            Binder.Bind(service);
    }

    private void UnbindServices()
    {
        foreach (var service in _services)
        {
            Binder.Unbind(service);

            if (service is IDisposable disposable)
                disposable.Dispose();
        }

        _services.Clear();
    }
}
```

A concrete installer then just lists its providers:

```csharp
[DefaultExecutionOrder(ExecutionOrder)]
public sealed class GameInstaller : Installer
{
    protected override void AddServices()
    {
        AddService(new InventoryService());
        AddService(new SaveService());
    }
}
```

### `[ManualBind]` — opt out of weaving

When a type must drive its own binding lifecycle, mark it `[ManualBind]` and write the calls yourself:

```csharp
[ManualBind]
public sealed class SessionView : MonoBehaviour
{
    [Inject] private ISessionService _session;

    private void Awake() => Binder.Bind(this);
    private void OnDisable() => Binder.Unbind(this); // an unusual lifecycle, on purpose
}
```

## API

| Member | Purpose |
| --- | --- |
| `[Inject]` | Resolve a dependency into a field or single-parameter method once it is installed. Woven automatically. |
| `[Track]` | Like `[Inject]`, but keeps receiving the latest value whenever the binding changes. A plain `[Track]` class must be `IDisposable`. |
| `[Install]` | Mark a type (or implemented interface) as a provider. **Not woven** — register it with `Binder.Bind` / `Install` where you own its lifetime. |
| `[ManualBind]` | Opt a type out of weaving so it manages its own `Binder` calls. |
| `Binder.Bind` / `Unbind` | Wire / unwire an instance's injects and installs. Used directly only for `[Install]` providers and `[ManualBind]` types. |
| `Binder.Install` / `Uninstall` | Register / remove a provider instance manually. |
