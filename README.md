# Unity Dependency Injection

Attribute-driven dependency injection for Unity. Mark a field or method with `[Inject]` (or `[Track]` to keep receiving updates), mark a provider with `[Install]`, and an **ILPostProcessor weaves the binding for you** — on `MonoBehaviour`s you never write `Binder.Bind(this)` by hand.

Self-contained and plug & play: it has no dependency on any other package, and its Mono.Cecil requirement is declared in `package.json`.

## Installation

Add to `Packages/manifest.json`:

```json
"com.zagoradev.unity.dependencyinjection": "https://github.com/pzagora/Unity-DependencyInjection.git"
```

Requires Unity `6000.0+`. Assemblies that use these types reference this package's assembly in their asmdef; code in the predefined assemblies (`Assembly-CSharp`) sees it automatically.

## How the code generation works

Binding is woven at **compile time** by an `ILPostProcessor` (`Unity.*.CodeGen`, using Mono.Cecil). For every type that opts in, the weaver rewrites the compiled IL so that:

- `Binder.Bind(this)` runs at the **end of `Awake`**, and
- `Binder.Unbind(this)` runs at the **start of `OnDestroy`**,

creating those methods (and calling `base`) when they don't exist. A type is woven when **all** of the following hold:

- it derives from `MonoBehaviour` (that's where `Awake`/`OnDestroy` exist),
- it declares an `[Inject]`/`[Track]` member, or is `[Install]`-marked (on the class or an implemented interface), and
- it is **not** `[ManualBind]`.

The weaver is **additive and idempotent**: if a type — or any of its base types — already binds, it is skipped. That means it coexists with hand-written binding and never double-binds. Assemblies are only processed if they reference this package, so unrelated code pays no cost.

Because the hook points are `Awake`/`OnDestroy`, **only `MonoBehaviour`s are woven**. Plain classes bind themselves (see below); they are never touched by the weaver.

## Usage

### MonoBehaviour — fully automatic

No `Binder` calls at all; the weaver adds them:

```csharp
[Install(typeof(IInventoryService))]
public sealed class InventoryService : MonoBehaviour, IInventoryService { }

public sealed class Hud : MonoBehaviour
{
    [Inject] private IInventoryService _inventory;

    // No Awake / OnDestroy, no Binder.Bind — the weaver injects them.
}
```

`[Install(typeof(IInventoryService))]` makes `InventoryService` register itself as `IInventoryService` when it is woven, so any `[Inject] IInventoryService` resolves to it.

### Plain class — bind in the constructor

Non-`MonoBehaviour` types have no `Awake`, so the weaver leaves them alone (no `[ManualBind]` needed). Bind in the constructor and unbind when the object's own lifetime ends:

```csharp
public sealed class InventoryPresenter : IDisposable
{
    [Inject] private readonly IInventoryService _inventory;

    public InventoryPresenter() => Binder.Bind(this);

    public void Dispose() => Binder.Unbind(this);
}
```

### MonoBehaviour with a custom unbind — opt out with `[ManualBind]`

When a `MonoBehaviour` must control its own binding lifecycle — for example unbinding on `Dispose` rather than on `OnDestroy` — mark it `[ManualBind]` so the weaver never touches its `Awake`/`OnDestroy`, and write the calls yourself:

```csharp
[ManualBind]
public sealed class SessionView : MonoBehaviour, IDisposable
{
    [Inject] private ISessionService _session;

    private void Awake() => Binder.Bind(this);

    // Unbinds deterministically on Dispose, not when the GameObject is destroyed.
    public void Dispose() => Binder.Unbind(this);
}
```

## API

| Member | Purpose |
| --- | --- |
| `[Inject]` | Resolve a dependency into a field or single-parameter method once it is installed. |
| `[Track]` | Like `[Inject]`, but keeps receiving the latest value whenever the binding changes. |
| `[Install]` | Register the attributed type (or an explicit type) as a provider — on the class or an implemented interface. |
| `[ManualBind]` | Opt a `MonoBehaviour` out of weaving so it manages its own `Binder` calls. |
| `Binder.Bind` / `Unbind` | Wire / unwire an instance's injects and installs (used directly only by plain classes and `[ManualBind]` types). |
| `Binder.Install` / `Uninstall` | Register / remove a provider instance manually. |
