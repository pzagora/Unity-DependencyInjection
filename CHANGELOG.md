# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this package adheres to Semantic Versioning.

## [1.0.5]

### Changed
- Published packages are signed with the zagora.dev Unity organization (Unity Package Manager CLI in the publish workflow), so Unity 6.3+ no longer warns that the package has no signature.

## [1.0.4]

### Changed
- First version published to the zagora.dev UPM registry (1.0.3 was tagged but never published). The publish workflow takes its target from the `UPM_PUBLISH_REGISTRY` repo variable, so CI publishes through the Worker's workers.dev address, which zagora.dev's Bot Fight Mode doesn't challenge.

## [1.0.3]

### Added
- `documentationUrl`, `changelogUrl` and `licensesUrl` in `package.json`, so Package Manager links to the README, CHANGELOG and LICENSE on GitHub.

## [1.0.2]

### Changed
- Documented an installer base-class pattern for registering `[Install]` providers in the README.

## [1.0.1]

### Added
- Author metadata in `package.json`.

## [1.0.0]

### Added
- Initial release, extracted from Unity Commons. Attribute-driven dependency injection: `[Inject]` / `[Track]` to consume dependencies, `[Install]` to mark a provider, and `Binder` to register and resolve.
- Compile-time binding via an `ILPostProcessor` (`Unity.DependencyInjection.CodeGen`, using Mono.Cecil): `[Inject]` / `[Track]` types are woven automatically — `MonoBehaviour`s bind in `Awake` and unbind in `OnDestroy`, plain classes bind in their constructor and unbind in `Dispose` — so manual `Binder.Bind(this)` is not required. Unbind is placed last so a type's own teardown can still use its injected fields. A plain `[Track]` type must be `IDisposable` (enforced as a build error).
- `[Install]` providers and `[ManualBind]` types are left to manual binding; the weaver skips them.
- Self-contained and plug & play: Mono.Cecil is declared as a package dependency.
