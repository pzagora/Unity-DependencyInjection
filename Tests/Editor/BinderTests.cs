using NUnit.Framework;

namespace DependencyInjection.Tests
{
    public sealed class BinderTests
    {
        private Service _first;
        private Service _second;

        [TearDown]
        public void TearDown()
        {
            Uninstall(_second);
            Uninstall(_first);
        }

        [Test]
        public void Inject_ResolvesDependencyInstalledAfterConsumerBinds()
        {
            var consumer = new OneShotConsumer();
            Binder.Bind(consumer);

            _first = Install("first");

            Assert.AreSame(_first, consumer.Service);
            Binder.Unbind(consumer);
            Assert.IsNull(consumer.Service);
        }

        [Test]
        public void Inject_IsOneShotWhenNewerDependencyIsInstalled()
        {
            _first = Install("first");
            var consumer = new OneShotConsumer();
            Binder.Bind(consumer);

            _second = Install("second");

            Assert.AreSame(_first, consumer.Service);
            Binder.Unbind(consumer);
        }

        [Test]
        public void Track_FollowsNewestDependencyAndFallsBackWhenItIsRemoved()
        {
            _first = Install("first");
            var consumer = new TrackingConsumer();
            Binder.Bind(consumer);
            Assert.AreSame(_first, consumer.Service);

            _second = Install("second");
            Assert.AreSame(_second, consumer.Service);

            Binder.Uninstall(_second, typeof(IService));
            _second = null;
            Assert.AreSame(_first, consumer.Service);

            Binder.Unbind(consumer);
            Assert.IsNull(consumer.Service);
        }

        [Test]
        public void InjectMethod_ReceivesInstalledDependency()
        {
            _first = Install("first");
            var consumer = new MethodConsumer();

            Binder.Bind(consumer);

            Assert.AreSame(_first, consumer.Service);
            Assert.AreEqual(1, consumer.Calls);
            Binder.Unbind(consumer);
        }

        private Service Install(string name)
        {
            var service = new Service(name);
            Binder.Install(service, typeof(IService));
            return service;
        }

        private static void Uninstall(Service service)
        {
            if (service != null)
                Binder.Uninstall(service, typeof(IService));
        }

        private interface IService
        {
            string Name { get; }
        }

        private sealed class Service : IService
        {
            public Service(string name) => Name = name;
            public string Name { get; }
        }

        [ManualBind]
        private sealed class OneShotConsumer
        {
            [Inject] public IService Service;
        }

        [ManualBind]
        private sealed class TrackingConsumer
        {
            [Track] public IService Service;
        }

        [ManualBind]
        private sealed class MethodConsumer
        {
            public IService Service { get; private set; }
            public int Calls { get; private set; }

            [Inject]
            private void SetService(IService service)
            {
                Service = service;
                Calls++;
            }
        }
    }
}
