using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.DurableInstancing;
using System.Text;
using System.Threading.Tasks;
using EasyNetQ.FluentConfiguration;
using EasyNetQ.Wf.Activities;
using EasyNetQ.Wf.AutoConsumers;
using EasyNetQ.NonGeneric;

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

            serviceRegister.Register<IWorkflowApplicationHostInstanceStore>(
                (serviceProvider) => new WorkflowApplicationHostInstanceStore(serviceProvider.Resolve<InstanceStore>())
                );
            serviceRegister.Register<IWorkflowApplicationHost>((serviceProvider) => new DefaultWorkflowApplicationHost(serviceProvider.Resolve<IBus>()));
        }

        [Obsolete]
        private static string GetWorkflowRouteTopicFromMessage<T>(IMessage<T> message)
        {
            if (message.Properties.HeadersPresent && message.Properties.Headers.ContainsKey(WorkflowConstants.MessageHeaderWorkflowRouteTopicKey))
            {
                var workflowRouteTopicBytes = message.Properties.Headers[WorkflowConstants.MessageHeaderWorkflowRouteTopicKey] as byte[];
                if (workflowRouteTopicBytes != null)
                {
                    return Encoding.UTF8.GetString(workflowRouteTopicBytes);
                }
            }
            return null;
        }

        [Obsolete]
        private static string GetWorkflowInstanceIdFromMessage<T>(IMessage<T> message)
        {
            if (message.Properties.HeadersPresent && message.Properties.Headers.ContainsKey(WorkflowConstants.MessageHeaderWorkflowInstanceIdKey))
            {
                var workflowInstanceIdBytes = message.Properties.Headers[WorkflowConstants.MessageHeaderWorkflowInstanceIdKey] as byte[];
                if (workflowInstanceIdBytes != null)
                    return Encoding.UTF8.GetString(workflowInstanceIdBytes);
            }
            return null;
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
            topic = CorrelatesOnAttribute.GetMessageCorrelatesOnTopic(message) ?? (AdvancedBusConsumerExtensions.GetTopicForMessage(message) ??  topic);

            // Sync publish
            if (!String.IsNullOrWhiteSpace(topic))
                bus.Publish(message.GetType(), message, topic);
            else
                bus.Publish(message.GetType(), message);
        }

        public static Task PublishExAsync(this IBus bus, Type messageType, object message, string topic = null)
        {
            // Find the correlation property
            topic = CorrelatesOnAttribute.GetMessageCorrelatesOnTopic(message) ?? (AdvancedBusConsumerExtensions.GetTopicForMessage(message) ?? topic);

            // Async publish
            if (!String.IsNullOrWhiteSpace(topic))
                return bus.PublishAsync(message.GetType(), message, topic);            
            return bus.PublishAsync(message.GetType(), message);
        }
                
        #endregion
        
        #region SubscribeWorkflow methods

        public static IWorkflowApplicationHost SubscribeWorkflow<TWorkflow, TMessage>(this IBus bus, string subscriptionId, Func<IWorkflowApplicationHost> createWorkflowHost, Action<ISubscriptionConfiguration> subscriptionConfig = null, bool subscribeAsync=true)
            where TWorkflow : Activity, new()
            where TMessage : class
        {
            if (String.IsNullOrWhiteSpace(subscriptionId)) throw new ArgumentNullException("subscriptionId");

            // load the workflow and inspect it
            var workflowActivity = new TWorkflow(); // TODO: load a workflow activity from an external xaml source

            if (subscriptionConfig == null)
            {
                var connectionConfiguration = bus.Advanced.Container.Resolve<ConnectionConfiguration>();
                subscriptionConfig = (s) =>
                {
                    s.WithTopic(typeof (TWorkflow).Name);
                    s.WithPrefetchCount(connectionConfiguration.PrefetchCount);
                };
            }

            // NOTE: Convention for the RootWorkflowActivity InArgument is InArgument<TMessage>            
            var inArgumentPropInfo = workflowActivity.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Single(x => x.PropertyType == typeof(InArgument<>).MakeGenericType(typeof(TMessage)));

            var receiveActivities = new List<Activity>();
            receiveActivities.AddActivities<IMessageReceiveActivity>(workflowActivity);
            
            // create a new WorkflowApplicationHost as a consumer
            var workflowApplicationHost = createWorkflowHost();
            workflowApplicationHost.Initialize(workflowActivity, inArgumentPropInfo.Name, typeof(TMessage));

            // add the WorkflowApplicationHost to the registry
            _workflowApplicationHostRegistry.Add(workflowApplicationHost);

            // declare the workflow queue                        
            if(subscribeAsync)
                workflowApplicationHost.AddSubscription(bus.SubscribeAsync(typeof (TMessage), subscriptionId, msg => workflowApplicationHost.OnDispatchMessageAsync(msg), subscriptionConfig));
            else
                workflowApplicationHost.AddSubscription(bus.Subscribe(typeof(TMessage), subscriptionId, msg => workflowApplicationHost.OnDispatchMessage(msg), subscriptionConfig));

            foreach (var activity in receiveActivities)
            {
                var activityType = activity.GetType();
                var messageType = activityType.GetGenericArguments()[0];

                // add consumed by handler
                if(subscribeAsync)
                    workflowApplicationHost.AddSubscription(bus.SubscribeAsync(messageType, subscriptionId, msg=> workflowApplicationHost.OnDispatchMessageAsync(msg), subscriptionConfig));
                else
                    workflowApplicationHost.AddSubscription(bus.Subscribe(messageType, subscriptionId, msg => workflowApplicationHost.OnDispatchMessage(msg), subscriptionConfig));
            }
            
            // Advanced Bus Method
            /*
            var conventions = bus.Advanced.Container.Resolve<IConventions>();
            var queue = bus.Advanced.QueueDeclare(conventions.QueueNamingConvention(typeof(TWorkflow), subscriptionId));                        
            bus.Advanced.Consume(queue, handlers =>
            {                
                // add an initiated by handler                
                host.RegisterConsumerHandler(handlers, queue, typeof(TMessage), subscriptionConfig);

                foreach (var activity in receiveActivities)
                {
                    var activityType = activity.GetType();
                    var messageType = activityType.GetGenericArguments()[0];

                    // add consumed by handler
                    host.RegisterConsumerHandler(handlers, queue, messageType, subscriptionConfig);
                }
            });            
            */
            return workflowApplicationHost;
        }

        public static IWorkflowApplicationHost SubscribeWorkflow<TWorkflow, TMessage>(this IBus bus, string subscriptionId, Action<ISubscriptionConfiguration> subscriptionConfig = null, bool subscribeAsync = true)
            where TWorkflow : Activity, new()
            where TMessage : class
        {            
            return bus.SubscribeWorkflow<TWorkflow, TMessage>(
                subscriptionId,
                () => bus.Advanced.Container.Resolve<IWorkflowApplicationHost>(),
                subscriptionConfig,
                subscribeAsync
                );
        }

        #endregion

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
    }
}