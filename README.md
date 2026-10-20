# Unity Commons — Dependency Injection

Attribute-driven dependency injection for Unity. Mark fields or methods with `[Inject]` (or `[Track]` to keep receiving updates), register providers with `[Install]` / `Binder.Install`, and resolve an object's dependencies with `Binder.Bind(this)`.

Assembly: **`Commons.DependencyInjection`**. It is **standalone** — it has no dependency on Unity Commons or any other package, so the dependency arrow runs one way: Commons (and other consumers) reference this, never the reverse.

## Installation

Add to `Packages/manifest.json`:

```json
"com.zagoradev.unity.commons.dependencyinjection": "https://github.com/pzagora/Unity-Commons.DependencyInjection.git"
```

Requires Unity `6000.0+`. Assemblies that use these types must reference `Commons.DependencyInjection` in their asmdef; code in the predefined assemblies (`Assembly-CSharp`) sees it automatically.

## Usage

```csharp
using Commons;

public class InventoryInstaller
{
    [Install(typeof(IInventoryService))]
    public class InventoryService : IInventoryService { }
}

public class Hud : MonoBehaviour
{
    [Inject] private readonly IInventoryService _inventory;

    private void Awake() => Binder.Bind(this);
    private void OnDestroy() => Binder.Unbind(this);
}
```

| Member | Purpose |
| --- | --- |
| `[Inject]` | Resolve a dependency into a field or single-parameter method once it is installed. |
| `[Track]` | Like `[Inject]`, but keeps receiving the latest value whenever the binding changes. |
| `[Install]` | Register the attributed type (or an explicit type) as a provider. |
| `Binder.Bind` / `Unbind` | Wire / unwire an instance's injects and installs. |
| `Binder.Install` / `Uninstall` | Register / remove a provider instance manually. |
