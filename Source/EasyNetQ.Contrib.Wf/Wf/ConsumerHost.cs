using System;
using System.Activities;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using EasyNetQ.Consumer;
using EasyNetQ.FluentConfiguration;
using EasyNetQ.Producer;
using EasyNetQ.Topology;

namespace EasyNetQ.Contrib.Wf
{
    public abstract class ConsumerHost : IDisposable
    {
        //internal const string OnInitiateMethod = "OnInitiate";
        internal const string DispatcherConsumeAdvancedMethod = "ConsumeAdvanced";

        protected readonly IBus Bus;
        protected readonly IEasyNetQLogger Log;
        protected readonly IConventions Conventions;
        protected readonly IPublishExchangeDeclareStrategy PublishExchangeDeclareStrategy;
        protected readonly ConnectionConfiguration ConnectionConfiguration;
        
        protected ConsumerHost(IBus bus)
        {
            if (bus == null) throw new ArgumentNullException("bus");
                        
            Bus = bus;
            Log = bus.Advanced.Container.Resolve<IEasyNetQLogger>();
            Conventions = bus.Advanced.Container.Resolve<IConventions>(); 
            PublishExchangeDeclareStrategy = bus.Advanced.Container.Resolve<IPublishExchangeDeclareStrategy>();
            ConnectionConfiguration = bus.Advanced.Container.Resolve<ConnectionConfiguration>();
        }        
                        
        public void RegisterConsumerHandler(IHandlerRegistration handlers, IQueue queue, Type messageType, Action<SubscriptionConfiguration> configAction)
        {            
            var subscriptionConfig = new SubscriptionConfiguration(ConnectionConfiguration.PrefetchCount);

            if (configAction != null)
                configAction(subscriptionConfig);

            // NOTE: this needs to be refactored out at some point because the Dispatcher should be created for the subscription            
            var exchange = PublishExchangeDeclareStrategy.DeclareExchange(Bus.Advanced, messageType, ExchangeType.Topic);
            foreach (var topic in subscriptionConfig.Topics.DefaultIfEmpty("#"))
            {
                Bus.Advanced.Bind(exchange, queue, topic);
            }
            
            var actionMethodType = typeof(Action<,>).MakeGenericType(typeof(IMessage<>).MakeGenericType(messageType), typeof(MessageReceivedInfo));

            object messageDispatcher = Activator.CreateInstance(typeof (DefaultConsumerHostMessageDispatcher<>).MakeGenericType(messageType), this);

            var onMessageMethodInfo = messageDispatcher.GetType()
                .GetMethod(DispatcherConsumeAdvancedMethod, BindingFlags.Instance | BindingFlags.Public);
            var onMessageDelegate = Delegate.CreateDelegate(actionMethodType, messageDispatcher, onMessageMethodInfo);

            // Add<T>(Action<IMessage<T>, MessageReceivedInfo> handler)
            var addHandlerMethodInfo = typeof(IHandlerRegistration).GetMethods()
                    .First(x => x.Name == "Add" && x.IsGenericMethod && x.GetParameters().Count() > 0 &&
                                x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() ==
                                typeof(Action<,>))
                    .MakeGenericMethod(messageType);
            addHandlerMethodInfo.Invoke(handlers, new object[] { onMessageDelegate });
        }
            
        public virtual void OnConsumeAdvanced(object message, MessageReceivedInfo info) { }

        public virtual void Start() { }

        #region IDisposable pattern
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // dispose resources here
                }
                _disposed = true;
            }

            // dispose unmanaged resources here            
        }
        #endregion
    }    
}