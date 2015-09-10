using System;
using System.Activities;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.DurableInstancing;
using System.Text;
using System.Threading.Tasks;
using EasyNetQ.Consumer;
using EasyNetQ.FluentConfiguration;
using EasyNetQ.Wf.Activities;
using EasyNetQ.Wf.AutoConsumers;
using EasyNetQ.NonGeneric;
using EasyNetQ.Producer;
using EasyNetQ.Topology;

namespace EasyNetQ.Wf
{
    public static class WorkflowBusExtensions
    {
        #region WorkflowApplicationHost Registry

        private readonly static List<IWorkflowApplicationHost> _workflowApplicationHostRegistry = new List<IWorkflowApplicationHost>();

        public static IEnumerable<IWorkflowApplicationHost> GetWorkflowApplicationHosts(this IBus bus)
        {
            return _workflowApplicationHostRegistry.ToArray();
        }

        internal static void RemoveWorkflowApplicationHost(IWorkflowApplicationHost host)
        {
            _workflowApplicationHostRegistry.Remove(host);
        }

        #endregion

        public static void UseWorkflowOrchestration(this IServiceRegister serviceRegister)
        {
            // register default components            
            serviceRegister.RegisterConsumers();    // TODO: RegisterConsumers may not be needed here

            serviceRegister.Register<IWorkflowApplicationHostInstanceStore, DefaultWorkflowApplicationHostInstanceStore>();            
            serviceRegister.Register<IWorkflowApplicationHostPerformanceMonitor, DefaultWorkflowApplicationHostPerformanceMonitor>();
            serviceRegister.Register<IWorkflowApplicationHost, DefaultWorkflowApplicationHost>();
        }
                
        #region Publish With Workflow Correlation methods

        public static void PublishEx<TMessage>(this IBus bus, TMessage message, string topic = null) where TMessage : class
        {
            PublishEx(bus, typeof(TMessage), message, topic);
        }

        public static Task PublishExAsync<TMessage>(this IBus bus, TMessage message, string topic = null) where TMessage : class
        {
            return PublishExAsync(bus, typeof(TMessage), message, topic);            
        }

        public static void PublishEx(this IBus bus, Type messageType, object message, string topic = null)
        {
            // Find the correlation property
            topic = topic ?? (CorrelatesOnAttribute.GetMessageCorrelatesOnTopic(message) ?? AdvancedBusConsumerExtensions.GetTopicForMessage(message));

            // Sync publish
            if (!String.IsNullOrWhiteSpace(topic))
                bus.Publish(message.GetType(), message, topic);
            else
                bus.Publish(message.GetType(), message);
        }

        public static Task PublishExAsync(this IBus bus, Type messageType, object message, string topic = null)
        {
            // Find the correlation property
            topic = topic ?? (CorrelatesOnAttribute.GetMessageCorrelatesOnTopic(message) ?? AdvancedBusConsumerExtensions.GetTopicForMessage(message));

            // Async publish
            if (!String.IsNullOrWhiteSpace(topic))
                return bus.PublishAsync(message.GetType(), message, topic);            
            return bus.PublishAsync(message.GetType(), message);
        }
                
        #endregion
        
        #region SubscribeWorkflow methods
        
        private static void SubscribeForOrchestration(this IBus bus, Activity workflowActivity, string subscriptionId, Func<IWorkflowApplicationHost> createWorkflowHost, Action<ISubscriptionConfiguration> subscriptionConfig = null, bool subscribeAsync=true, bool autoStart=true)
        {
            if (workflowActivity == null) throw new ArgumentNullException("workflowActivity");                                                                         
            if (String.IsNullOrWhiteSpace(subscriptionId)) throw new ArgumentNullException("subscriptionId");
                        
            if (subscriptionConfig == null)
            {
                var connectionConfiguration = bus.Advanced.Container.Resolve<ConnectionConfiguration>();
                subscriptionConfig = (s) =>
                {
                    s.WithTopic(workflowActivity.GetType().Name);
                    s.WithPrefetchCount(connectionConfiguration.PrefetchCount);
                };
            }

            // NOTE: Convention for the RootWorkflowActivity InArgument is InArgument<TMessage>
            var initialArgumentInfo = GetInArgumentsFromActivity(workflowActivity).SingleOrDefault();
            if (initialArgumentInfo == null)
            {
                // convention is that a Workflow must have a single InArgument<TMessage> that will be used to initiate the workflow
                throw new InvalidOperationException(String.Format("Workflow type {0} cannot be subscribed without an InArgument with a valid message type", workflowActivity.GetType().FullName));
            }                               
                                    
            // create a new WorkflowApplicationHost as a consumer
            var workflowApplicationHost = createWorkflowHost();
            workflowApplicationHost.Initialize(workflowActivity);

            // add the WorkflowApplicationHost to the registry
            _workflowApplicationHostRegistry.Add(workflowApplicationHost);
                        
            // Advanced Bus Method                        
            var queue = bus.DeclareMessageQueue(workflowActivity.GetType(), subscriptionId);
            bus.Advanced.Consume(queue, handlers =>
            {
                var logger = bus.Advanced.Container.Resolve<IEasyNetQLogger>();
                logger.InfoWrite("Workflow {0} subscribing to messages using queue {1}:", workflowActivity.GetType().Name, queue.Name);
                logger.InfoWrite("{0} on topic {1}", initialArgumentInfo.InArgumentType.FullName, workflowActivity.GetType().Name);

                // declare and bind the message exchange to the workflow queue
                var exchange = bus.DeclareMessageExchange(initialArgumentInfo.InArgumentType, subscriptionConfig);
                bus.BindMessageExchangeToQueue(exchange, queue, subscriptionConfig);

                // add an initiated by handler   
                if (subscribeAsync)
                    handlers.Add(initialArgumentInfo.InArgumentType, (msg,info)=> workflowApplicationHost.OnDispatchMessageAsync(msg.GetBody()));                     
                else                    
                    handlers.Add(initialArgumentInfo.InArgumentType, (msg, info) => workflowApplicationHost.OnDispatchMessage(msg.GetBody()));

                foreach (var activity in GetMatchingActivities<IMessageReceiveActivity>(workflowActivity))
                {
                    var activityType = activity.GetType();
                    var messageType = activityType.GetGenericArguments()[0];

                    logger.InfoWrite("{0} on topic {1}", messageType.FullName, workflowActivity.GetType().Name);

                    // declare and bind the message exchange to the workflow queue
                    exchange = bus.DeclareMessageExchange(messageType, subscriptionConfig);
                    bus.BindMessageExchangeToQueue(exchange, queue, subscriptionConfig);

                    // add consumed by handler
                    if (subscribeAsync)
                        handlers.Add(messageType, (msg, info) => workflowApplicationHost.OnDispatchMessageAsync(msg.GetBody()));                        
                    else
                        handlers.Add(messageType, (msg, info) => workflowApplicationHost.OnDispatchMessage(msg.GetBody()));                        
                }                
            });

            if (autoStart)
            {
                workflowApplicationHost.Start();
            }
        }

        private static IHandlerRegistration Add(this IHandlerRegistration handlerRegistration, Type messageType, Action<IMessage,MessageReceivedInfo> handler)
        {
            var addHandlerMethod = handlerRegistration.GetType().GetMethods().SingleOrDefault(x => x.Name == "Add" && x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof (Action<,>));
            if (addHandlerMethod == null)
            {
                throw new EasyNetQException("API change? IHandlerRegistration.Add(Action<IMessage<T>,MessageReceivedInfo> handler) method not found on IBus.Advanced");
            }            
            return (IHandlerRegistration) addHandlerMethod.MakeGenericMethod(messageType).Invoke(handlerRegistration, new object[] {handler});
        }

        private static IHandlerRegistration Add(this IHandlerRegistration handlerRegistration, Type messageType, Func<IMessage, MessageReceivedInfo, Task> handler)
        {
            var addHandlerMethod = handlerRegistration.GetType().GetMethods().SingleOrDefault(x => x.Name == "Add" && x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<,,>));
            if (addHandlerMethod == null)
            {
                throw new EasyNetQException("API change? IHandlerRegistration.Add(Func<IMessage<T>,MessageReceivedInfo,Task> handler) method not found on IBus.Advanced");
            }            
            return (IHandlerRegistration)addHandlerMethod.MakeGenericMethod(messageType).Invoke(handlerRegistration, new object[] { handler });
        }
                
        public static void SubscribeForOrchestration<TWorkflow>(this IBus bus, string subscriptionId, Action<ISubscriptionConfiguration> subscriptionConfig = null, bool subscribeAsync = true, bool autoStart = true)
            where TWorkflow : Activity, new()            
        {            
            TWorkflow workflowActivity = new TWorkflow();

            bus.SubscribeForOrchestration(
                workflowActivity,
                subscriptionId,
                () => bus.Advanced.Container.Resolve<IWorkflowApplicationHost>(),
                subscriptionConfig,
                subscribeAsync,
                autoStart
                );
        }
        
        public static void SubscribeForOrchestration(this IBus bus, string workflowId, string subscriptionId, Action<ISubscriptionConfiguration> subscriptionConfig = null, bool subscribeAsync = true, bool autoStart = true)
        {
            var repository = bus.Advanced.Container.Resolve<IWorkflowDefinitionRepository>();
            if (repository == null) throw new NullReferenceException("An IWorkflowDefinitionRepository cannot be found");

            // TODO: add support for Workflow Versioning (see: https://msdn.microsoft.com/en-us/library/system.activities.workflowidentity(v=vs.110).aspx)
            var workflowDefinition = repository.Get(workflowId).SingleOrDefault();
            if(workflowDefinition == null) throw new NullReferenceException(String.Format("A valid workflow activity [{0}] cannot be loaded", workflowId));

            bus.SubscribeForOrchestration(
                workflowDefinition.RootActivity,
                subscriptionId,
                () => bus.Advanced.Container.Resolve<IWorkflowApplicationHost>(),
                subscriptionConfig,
                subscribeAsync,
                autoStart
                );
        }

        #endregion

        #region Activity Helper methods

        internal class InArgumentInfo
        {
            public string InArgumentName { get; set; }
            
            public bool InArgumentIsRequired { get; set; }

            public Type InArgumentType { get; set; }
        }
        
        internal static IEnumerable<InArgumentInfo> GetInArgumentsFromActivity(Activity activity)
        {
            var args = new List<InArgumentInfo>();

            var properties = activity.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => typeof (InArgument).IsAssignableFrom(p.PropertyType))
                .ToList();
            
            foreach (var property in properties)
            {
                if (!property.PropertyType.IsGenericType) continue;
                                             
                bool isRequired = property
                    .GetCustomAttributes(false)
                    .OfType<RequiredArgumentAttribute>()
                    .Any();

                Type argumentType = property.PropertyType.GetGenericArguments()[0];
                
                args.Add(new InArgumentInfo
                {
                    InArgumentName = property.Name,                        
                    InArgumentIsRequired = isRequired,
                    InArgumentType = argumentType
                });                                                
            }

            return args;
        }
        
        private static IEnumerable<Activity> GetMatchingActivities<T>(Activity rootActivity)
        {
            var activities = new List<Activity>();

            activities.AddActivities<T>(rootActivity);

            return activities;
        }

        /// <summary>
        /// Recursively inspect nodes of an Activity, and Add matching Activities of type T to an IList
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <param name="root"></param>
        private static void AddActivities<T>(this IList<Activity> items, Activity root)
        {
            if (root is T) items.Add(root);

            var nodes = WorkflowInspectionServices.GetActivities(root);
            foreach (var c1 in nodes)
            {
                if (c1 != root)
                {
                    items.AddActivities<T>(c1);
                }
            }
        }



#endregion
    }      
}