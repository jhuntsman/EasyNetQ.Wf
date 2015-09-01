using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public static void RegisterWorkflowComponents(this IServiceRegister serviceRegister)
        {
            // register default components
            serviceRegister.RegisterConsumers();
            serviceRegister.Register<IWorkflowConsumerHostStrategies, DefaultWorkflowConsumerHostStrategies>();            
        }

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

        public static void Reply<TMessage, TResponse>(this IBus bus, IMessage<TMessage> message, TResponse response)
            where TResponse : class
        {
            // respond back to the workflow instance
            var workflowMessage = new Message<TResponse>(response);
            string workflowRouteTopic = GetWorkflowRouteTopicFromMessage(message);
            string workflowInstanceId = GetWorkflowInstanceIdFromMessage(message);
                                                
            if (!String.IsNullOrWhiteSpace(workflowInstanceId))
            {                
                workflowMessage.Properties.Headers.Add(WorkflowConstants.MessageHeaderWorkflowInstanceIdKey, workflowInstanceId);
            }

            var workflowStrategy = bus.Advanced.Container.Resolve<IWorkflowConsumerHostStrategies>();
            workflowStrategy.PublishAdvanced(workflowMessage, workflowRouteTopic);
        }

        public static Task ReplyAsync<TMessage, TResponse>(this IBus bus, IMessage<TMessage> message, TResponse response)
            where TResponse : class
        {
            var workflowMessage = new Message<TResponse>(response);
            string workflowRouteTopic = GetWorkflowRouteTopicFromMessage(message);
            string workflowInstanceId = GetWorkflowInstanceIdFromMessage(message);

            if (!String.IsNullOrWhiteSpace(workflowInstanceId))
            {
                workflowMessage.Properties.Headers.Add(WorkflowConstants.MessageHeaderWorkflowInstanceIdKey, workflowInstanceId);
            }

            var workflowStrategy = bus.Advanced.Container.Resolve<IWorkflowConsumerHostStrategies>();
            return workflowStrategy.PublishAdvancedAsync(workflowMessage, workflowRouteTopic);            
        }

        public static IRunnableConsumerHost SubscribeWorkflow<TWorkflow, TMessage>(this IBus bus, string subscriptionId, Func<IBus, TWorkflow, string, Type, WorkflowConsumerHost> createWorkflowHost, Action<SubscriptionConfiguration> subscriptionConfig = null)
            where TWorkflow : Activity, new()
            where TMessage : class
        {
            if (String.IsNullOrWhiteSpace(subscriptionId)) throw new ArgumentNullException("subscriptionId");

            // load the workflow and inspect it
            var workflowActivity = new TWorkflow();

            if (subscriptionConfig == null)
            {
                subscriptionConfig = (s) => s.WithTopic(typeof (TWorkflow).Name);
            }

            // NOTE: Convention for the RootWorkflowActivity InArgument is InArgument<TMessage>            
            var inArgumentPropInfo = workflowActivity.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Single(x => x.PropertyType == typeof(InArgument<>).MakeGenericType(typeof(TMessage)));

            var receiveActivities = new List<Activity>();
            receiveActivities.AddActivities<IReceiveMessageActivity>(workflowActivity);

            var conventions = bus.Advanced.Container.Resolve<IConventions>();

            // declare the workflow queue            
            var queue = bus.Advanced.QueueDeclare(conventions.QueueNamingConvention(typeof(TWorkflow), subscriptionId));

            // create a new SagaHost as a consumer
            var host = createWorkflowHost(bus, workflowActivity, inArgumentPropInfo.Name, typeof (TMessage));
            
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
            return host;
        }

        public static IRunnableConsumerHost SubscribeWorkflow<TWorkflow, TMessage>(this IBus bus, string subscriptionId, Action<SubscriptionConfiguration> subscriptionConfig = null)
            where TWorkflow : Activity, new()
            where TMessage : class
        {            
            return bus.SubscribeWorkflow<TWorkflow, TMessage>(
                subscriptionId,
                (theBus, workflow, argName, msgType) => new WorkflowConsumerHost(theBus, workflow, argName, msgType),
                subscriptionConfig
                );
        }

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