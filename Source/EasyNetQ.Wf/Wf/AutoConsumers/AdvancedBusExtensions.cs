using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EasyNetQ.Consumer;
using EasyNetQ.FluentConfiguration;
using EasyNetQ.Producer;
using EasyNetQ.Topology;

namespace EasyNetQ.Wf.AutoConsumers
{
    public static class AdvancedBusConsumerExtensions
    {
        public static void RegisterConsumers(this IServiceRegister serviceRegister)
        {
            // TODO: use EasyNetQ methods for AutoSubscription
            serviceRegister.Register<IConsumerMessageDispatcher, DefaultConsumerMessageDispatcher>();
        }

        #region Topic Based Attribute Routing

        internal static string GetTopicForMessage(object message)
        {
            string topic = null;
            TryGetTopicForMessageValue(message, out topic);
            return topic;
        }

        private static bool TryGetTopicForMessageValue(object message, out string value)
        {
            if(message == null) throw new ArgumentNullException("message");

            value = null;            
            var topicAttribute = message.GetType().GetCustomAttributes(typeof (OnTopicAttribute), false).Cast<OnTopicAttribute>().SingleOrDefault();
            if (topicAttribute != null && !String.IsNullOrWhiteSpace(topicAttribute.Name))
            {
                value = topicAttribute.Name;
                return true;
            }
            return false;
        }
        #endregion

        #region Declare Exchange and Queue helper methods

        internal static IExchange DeclareMessageExchange(this IBus bus, Type messageType)
        {
            var conventions = bus.Advanced.Container.Resolve<IConventions>();            
            var exchangeName = conventions.ExchangeNamingConvention(messageType);
            var exchange = bus.Advanced.ExchangeDeclare(exchangeName, ExchangeType.Topic);

            return exchange;
        }

        internal static IQueue DeclareMessageQueue(this IBus bus, Type messageType, string subscriptionId, Action<ISubscriptionConfiguration> configAction = null)
        {
            var conventions = bus.Advanced.Container.Resolve<IConventions>();
            var connectionConfig = bus.Advanced.Container.Resolve<ConnectionConfiguration>();
            var subscriptionConfig = new SubscriptionConfiguration(connectionConfig.PrefetchCount);
            if (configAction != null)
            {
                configAction(subscriptionConfig);
            }

            var queueName = conventions.QueueNamingConvention(messageType, subscriptionId);
            var queue = bus.Advanced.QueueDeclare(queueName, autoDelete: subscriptionConfig.AutoDelete, exclusive:subscriptionConfig.IsExclusive);

            return queue;
        }

        internal static void BindMessageExchangeToQueue(this IBus bus, IExchange exchange, IQueue queue, Action<ISubscriptionConfiguration> configAction = null)
        {
            var connectionConfig = bus.Advanced.Container.Resolve<ConnectionConfiguration>();
            var subscriptionConfig = new SubscriptionConfiguration(connectionConfig.PrefetchCount);
            if (configAction != null)
            {
                configAction(subscriptionConfig);
            }

            foreach (var topic in subscriptionConfig.Topics.DefaultIfEmpty("#"))
            {
                bus.Advanced.Bind(exchange, queue, topic);
            }
        }

        #endregion


        public static IDisposable Subscribe<T>(this IBus bus, string subscriptionId, Action<IMessage<T>,MessageReceivedInfo> onMessage)
            where T:class
        {
            return bus.Subscribe(subscriptionId, onMessage, x => { });
        }

        public static IDisposable Subscribe<T>(this IBus bus, string subscriptionId,
            Action<IMessage<T>, MessageReceivedInfo> onMessage, Action<ISubscriptionConfiguration> configure)
            where T : class
        {
            return bus.SubscribeAsync<T>(subscriptionId, (msg,info) =>
            {
                var tcs = new TaskCompletionSource<object>();
                try
                {
                    onMessage(msg, info);
                    tcs.SetResult(null);
                }
                catch (Exception exception)
                {
                    tcs.SetException(exception);
                }
                return tcs.Task;
            },
            configure);
        }

        public static IDisposable SubscribeAsync<T>(this IBus bus, string subscriptionId, Func<IMessage<T>, MessageReceivedInfo, Task> onMessage)
            where T : class
        {
            return bus.SubscribeAsync(subscriptionId, onMessage, x => { });
        }        

        public static IDisposable SubscribeAsync<T>(this IBus bus, string subscriptionId, Func<IMessage<T>, MessageReceivedInfo, Task> onMessage, Action<ISubscriptionConfiguration> configAction=null)
            where T : class
        {

            var connectionConfig = bus.Advanced.Container.Resolve<ConnectionConfiguration>();
            var subscriptionConfig = new SubscriptionConfiguration(connectionConfig.PrefetchCount);
            if (configAction != null)
            {
                configAction(subscriptionConfig);
            }

            var exchange = bus.DeclareMessageExchange(typeof(T));
            var queue = bus.DeclareMessageQueue(typeof (T), subscriptionId, configAction);            
            bus.BindMessageExchangeToQueue(exchange, queue, configAction);
                                                
            return bus.Advanced.Consume<T>(queue, onMessage, x => x.WithPriority(subscriptionConfig.Priority));
        }
                       
        public static void SubscribeConsumer<TConsumer>(this IBus bus, string subscriptionId) where TConsumer : class
        {
            var messageDispatcher = bus.Advanced.Container.Resolve<IConsumerMessageDispatcher>();
            
            // TODO: AutoSubscribeConsumer - subscribe using EasyNetQ.IConsume interface and EasyNetQ.IConsumeAsync

            AutoSubscribeConsumerAdvanced<TConsumer>(bus, subscriptionId, messageDispatcher);
            
            AutoSubscribeResponder<TConsumer>(bus, messageDispatcher, typeof (IRespond<,>), "Respond", "Respond",
                typeof (Func<,>),
                (requestType, responseType) => typeof (Func<,>).MakeGenericType(requestType, responseType));           
            AutoSubscribeResponder<TConsumer>(bus, messageDispatcher, typeof(IRespondAsync<,>), "RespondAsync", "RespondAsync",
                typeof(Func<,>),
                (requestType, responseType) => typeof(Func<,>).MakeGenericType(requestType, typeof(Task<>).MakeGenericType(responseType)));            
        }
        
        private static void AutoSubscribeConsumerAdvanced<TConsumer>(IBus bus, string subscriptionId, IConsumerMessageDispatcher dispatcher) where TConsumer : class
        {
            var consumerType = typeof(TConsumer);

            // IConsumeAdvanced 
            var consumeMessages =
                consumerType.GetInterfaces()
                    .Where(x => x.IsInterface && x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IConsumeAdvanced<>))
                    .ToArray();

            // IConsumeAdvancedAsync
            var consumeAsyncMessages =
                consumerType.GetInterfaces()
                    .Where(x => x.IsInterface && x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IConsumeAdvancedAsync<>))
                    .ToArray();

            if (consumeMessages.Any() || consumeAsyncMessages.Any())
            {
                var conventions = bus.Advanced.Container.Resolve<IConventions>();
                var publishExchangeDeclareStrategy = bus.Advanced.Container.Resolve<IPublishExchangeDeclareStrategy>();

                var queue = bus.DeclareMessageQueue(typeof (TConsumer), subscriptionId, null); 

                var connectionConfig = bus.Advanced.Container.Resolve<ConnectionConfiguration>();
                var subscriptionConfig = new SubscriptionConfiguration(connectionConfig.PrefetchCount);

                bus.Advanced.Consume(queue, handlers =>
                {
                    // IConsumeAdvanced
                    foreach (var consumeMessage in consumeMessages)
                    {

#if NET4
                        var genericTypeArgs = consumeMessage.GetGenericArguments().Where(t => !t.IsGenericParameter).ToArray();
                        var messageType = genericTypeArgs[0];
#else
                        var messageType = consumeMessage.GenericTypeArguments[0];
#endif
                        var exchange = bus.DeclareMessageExchange(messageType);
                        bus.BindMessageExchangeToQueue(exchange, queue, null);
                                                                        
                        var dispatchMethodInfo = dispatcher.GetType()
                            .GetMethod("ConsumeAdvanced", BindingFlags.Instance | BindingFlags.Public)
                            .MakeGenericMethod(messageType, typeof(TConsumer));
                        var actionMethodType = typeof(Action<,>).MakeGenericType(typeof(IMessage<>).MakeGenericType(messageType), typeof(MessageReceivedInfo));

                        var onMessageDelegate = Delegate.CreateDelegate(actionMethodType, dispatcher, dispatchMethodInfo);

                        // Add<T>(Action<IMessage<T>, MessageReceivedInfo> handler)
                        var addHandlerMethodInfo = typeof (IHandlerRegistration).GetMethods()
                            .First(x => x.Name == "Add" && x.IsGenericMethod && x.GetParameters().Count() > 0 &&
                                        x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() ==
                                        typeof (Action<,>))
                            .MakeGenericMethod(messageType);
                        addHandlerMethodInfo.Invoke(handlers, new object[] {onMessageDelegate});                        
                    }

                    // IConsumeAdvancedAsync
                    foreach (var consumeMessage in consumeAsyncMessages)
                    {
#if NET4
                        var genericTypeArgs = consumeMessage.GetGenericArguments().Where(t => !t.IsGenericParameter).ToArray();
                        var messageType = genericTypeArgs[0];
#else
                        var messageType = consumeMessage.GenericTypeArguments[0];
#endif

                        var exchange = bus.DeclareMessageExchange(messageType);
                        bus.BindMessageExchangeToQueue(exchange, queue, null);
                        
                        var dispatchMethodInfo = dispatcher.GetType()
                            .GetMethod("ConsumeAdvancedAsync", BindingFlags.Instance | BindingFlags.Public)
                            .MakeGenericMethod(messageType, typeof(TConsumer));

                        var actionMethodType = typeof (Func<,,>).MakeGenericType(typeof (IMessage<>).MakeGenericType(messageType),typeof (MessageReceivedInfo), typeof (Task));

                        var onMessageDelegate = Delegate.CreateDelegate(actionMethodType, dispatcher, dispatchMethodInfo);
                                                
                        // Add<T>(Func<IMessage<T>, MessageReceivedInfo, Task> handler)
                        var addHandlerMethodInfo = typeof (IHandlerRegistration).GetMethods()
                            .First(x => x.Name == "Add" && x.IsGenericMethod && x.GetParameters().Count() > 0 &&
                                        x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() ==
                                        typeof (Func<,,>))
                            .MakeGenericMethod(messageType);
                        addHandlerMethodInfo.Invoke(handlers, new object[] { onMessageDelegate });
                    }
                });                
            }
        }

        private static void AutoSubscribeResponder<TConsumer>(IBus bus, IConsumerMessageDispatcher dispatcher, Type responseMethodType, string dispatchMethod, string subscribeMethod, Type subscribeMethodType, Func<Type, Type, Type> responseActionType) where TConsumer : class
        {
            var responderType = typeof(TConsumer);
            var responseMessages =
                responderType.GetInterfaces()
                    .Where(x => x.IsInterface && x.IsGenericType && x.GetGenericTypeDefinition() == responseMethodType)
                    .ToArray();
            if (responseMessages.Any())
            {                
                foreach (var responseMessage in responseMessages)
                {
#if NET4
                    var genericTypeArgs = responseMessage.GetGenericArguments().Where(t => !t.IsGenericParameter).ToArray();
                    var requestType = genericTypeArgs[0];
                    var responseType = genericTypeArgs[1];
#else
                    var requestType = responseMessage.GenericTypeArguments[0];
                    var responseType = responseMessage.GenericTypeArguments[1];
#endif


                    var genericBusSubscribeMethod = bus.GetType().GetMethods()
                        .Where(m => m.Name == subscribeMethod)
                        .Select(m => new {Method = m, Params = m.GetParameters()})
                        .Single(m => m.Params.Length == 1
                                     && m.Params[0].ParameterType.GetGenericTypeDefinition() == subscribeMethodType
                        ).Method.MakeGenericMethod(requestType, responseType);
                                                                                                                    
                    var dispatchMethodInfo = dispatcher.GetType()
                        .GetMethod(dispatchMethod, BindingFlags.Instance | BindingFlags.Public)
                        .MakeGenericMethod(requestType, responseType, typeof(TConsumer));
                    var actionMethodType = responseActionType(requestType, responseType);

                    var dispatchDelegate = Delegate.CreateDelegate(actionMethodType, dispatcher, dispatchMethodInfo);
                    
                    genericBusSubscribeMethod.Invoke(bus, new object[] { dispatchDelegate });                    
                }
            }
        }                 
    }
}