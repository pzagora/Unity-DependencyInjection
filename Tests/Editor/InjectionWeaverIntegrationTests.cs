using System;
using NUnit.Framework;

namespace DependencyInjection.Tests
{
    public sealed class InjectionWeaverIntegrationTests
    {
        private Service _service;

        [SetUp]
        public void SetUp()
        {
            _service = new Service();
            Binder.Install(_service, typeof(IService));
        }

        [TearDown]
        public void TearDown()
        {
            Binder.Uninstall(_service, typeof(IService));
            _service = null;
        }

        [Test]
        public void Constructor_AutomaticallyBindsPlainConsumer()
        {
            var consumer = new AutomaticConsumer();

            Assert.AreSame(_service, consumer.Service);
        }

        [Test]
        public void Dispose_AutomaticallyUnbindsTrackedConsumer()
        {
            var consumer = new AutomaticTrackedConsumer();
            Assert.AreSame(_service, consumer.Service);

            consumer.Dispose();

            Assert.IsTrue(consumer.Disposed);
            Assert.IsNull(consumer.Service);
        }

        [Test]
        public void ManualBind_LeavesLifecycleUnderCallerControl()
        {
            var consumer = new ManualConsumer();
            Assert.IsNull(consumer.Service);

            Binder.Bind(consumer);

            Assert.AreSame(_service, consumer.Service);
            Binder.Unbind(consumer);
        }

        private interface IService
        {
        }

        private sealed class Service : IService
        {
        }

        private sealed class AutomaticConsumer
        {
            [Inject] public IService Service;
        }

        private sealed class AutomaticTrackedConsumer : IDisposable
        {
            [Track] public IService Service;
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        [ManualBind]
        private sealed class ManualConsumer
        {
            [Inject] public IService Service;
        }
    }
}
