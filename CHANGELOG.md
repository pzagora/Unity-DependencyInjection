# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this package adheres to Semantic Versioning.

## [1.0.0]

### Added
- Initial release, extracted from Unity Commons. Attribute-driven dependency injection: `[Inject]` / `[Track]` to consume dependencies, `[Install]` to mark a provider, and `Binder` to register and resolve.
- Compile-time binding via an `ILPostProcessor` (`Unity.DependencyInjection.CodeGen`, using Mono.Cecil): `[Inject]` / `[Track]` types are woven automatically — `MonoBehaviour`s bind in `Awake` and unbind in `OnDestroy`, plain classes bind in their constructor and unbind in `Dispose` — so manual `Binder.Bind(this)` is not required. Unbind is placed last so a type's own teardown can still use its injected fields. A plain `[Track]` type must be `IDisposable` (enforced as a build error).
- `[Install]` providers and `[ManualBind]` types are left to manual binding; the weaver skips them.
- Self-contained and plug & play: Mono.Cecil is declared as a package dependency.
