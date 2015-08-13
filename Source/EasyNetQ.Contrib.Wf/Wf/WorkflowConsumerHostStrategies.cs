using System;
using System.Activities;
using System.Activities.DurableInstancing;
using System.Collections.Generic;
using System.Runtime.DurableInstancing;
using System.Threading.Tasks;
using EasyNetQ.Contrib.Consumers;
using System.Threading;
using System.Xml.Linq;

namespace EasyNetQ.Contrib.Wf
{    
    public interface IWorkflowConsumerHostStrategies
    {
        string BookmarkMessageNamingStrategy(Type messageType);
        
        IMessage<TMessage> CreateWorkflowMessage<TMessage>(ActivityContext context, TMessage messageBody, string workflowRouteTopic)
            where TMessage : class;

        WorkflowApplication CreateWorkflowApplication(Activity workflowDefinition, Dictionary<string, object> workflowInputs);
        void AddWorkflowExtensions(WorkflowApplication workflowApplication);
        InstanceStore CreateWorkflowInstanceStore(WorkflowApplication workflowApplication);
        InstanceHandle InitializeWorkflowInstanceOwner(WorkflowApplication workflowApplication, InstanceStore instanceStore, Activity workflowDefinition);
        void LoadWorkflowInstance(WorkflowApplication workflowApplication, Guid workflowInstanceId);
        void ReleaseWorkflowInstanceOwner(WorkflowApplication workflowApplication, InstanceStore instanceStore, InstanceHandle instanceHandle);
        BookmarkResumptionResult ResumeWorkflowFromBookmark<TMessage>(WorkflowApplication workflowApplication, TMessage messageBody) where TMessage : class;
        void Run(WorkflowApplication workflowApplication, InstanceStore instanceStore, InstanceHandle instanceHandle);

        void PublishAdvanced<T>(IMessage<T> message, string topic = null) where T : class;
        Task PublishAdvancedAsync<T>(IMessage<T> message, string topic = null) where T : class;

        void Reply<TMessage, TResponse>(IMessage<TMessage> message, TResponse response) where TResponse : class;
        Task ReplyAsync<TMessage, TResponse>(IMessage<TMessage> message, TResponse response) where TResponse : class;

        XName GetWorkflowHostTypeName(Activity workflowDefinition);
    }

    public class DefaultWorkflowConsumerHostStrategies : IWorkflowConsumerHostStrategies
    {
        private static readonly XName WorkflowHostTypePropertyName = XNamespace.Get("urn:schemas-microsoft-com:System.Activities/4.0/properties").GetName("WorkflowHostType");

        protected readonly IBus Bus;
        protected readonly IEasyNetQLogger Log;        

        public DefaultWorkflowConsumerHostStrategies(IBus bus, IEasyNetQLogger logger)
        {
            if(bus == null) throw new ArgumentNullException("bus");
            if(logger == null) throw new ArgumentNullException("logger");

            Bus = bus;
            Log = logger;
        }

        public virtual string BookmarkMessageNamingStrategy(Type messageType)
        {
            if(messageType == null) throw new ArgumentNullException("messageType");
            return messageType.FullName;
        }

        public virtual IMessage<TMessage> CreateWorkflowMessage<TMessage>(ActivityContext context, TMessage messageBody, string workflowRouteTopic)
            where TMessage : class
        {            
            if(context == null) throw new ArgumentNullException("context");
            if(messageBody == null) throw new ArgumentNullException("messageBody");

            var workflowMessage = new Message<TMessage>(messageBody);

            // add the WorkflowId as the Message CorrelationId
            if (!String.IsNullOrWhiteSpace(workflowRouteTopic))
            {
                workflowMessage.Properties.Headers.Add(WorkflowConstants.MessageHeaderWorkflowRouteTopicKey, workflowRouteTopic);
            }
            workflowMessage.Properties.Headers.Add(WorkflowConstants.MessageHeaderWorkflowInstanceIdKey, context.WorkflowInstanceId.ToString());

            return workflowMessage;
        }

        public virtual WorkflowApplication CreateWorkflowApplication(Activity workflowDefinition, Dictionary<string, object> workflowInputs)
        {
            if (workflowDefinition == null) throw new ArgumentNullException("workflowDefinition");

            WorkflowApplication wfApp = null;

            if(workflowInputs != null)
                wfApp = new WorkflowApplication(workflowDefinition, workflowInputs);
            else 
                wfApp = new WorkflowApplication(workflowDefinition);
            
            // add required workflow extensions
            wfApp.Extensions.Add(Bus);

            // add custom workflow extensions
            AddWorkflowExtensions(wfApp);

            return wfApp;
        }

        public virtual void AddWorkflowExtensions(WorkflowApplication workflowApplication)
        {
            // custom workflow extensions could go here if needed
        }

        public virtual InstanceStore CreateWorkflowInstanceStore(WorkflowApplication workflowApplication)
        {
            if(workflowApplication == null) throw new ArgumentNullException("workflowApplication");

            var instanceStore = Bus.Advanced.Container.Resolve<InstanceStore>();
            workflowApplication.InstanceStore = instanceStore;
            return instanceStore;
        }

        public virtual XName GetWorkflowHostTypeName(Activity workflowDefinition)
        {
            return XName.Get(workflowDefinition.GetType().Name, workflowDefinition.GetType().Namespace);
        }

        public virtual InstanceHandle InitializeWorkflowInstanceOwner(WorkflowApplication workflowApplication, InstanceStore instanceStore, Activity workflowDefinition)
        {
            if (workflowApplication == null) throw new ArgumentNullException("workflowApplication");
            if(instanceStore == null) throw new ArgumentNullException("instanceStore");
            if(workflowDefinition == null) throw new ArgumentNullException("workflowDefinition");
            
            var instanceHandle = instanceStore.CreateInstanceHandle();
            var createOwnerCmd = new CreateWorkflowOwnerCommand()
            {
                InstanceOwnerMetadata =
                {
                    {WorkflowHostTypePropertyName, new InstanceValue(GetWorkflowHostTypeName(workflowDefinition))}
                }
            };
            var view = instanceStore.Execute(instanceHandle, createOwnerCmd, TimeSpan.FromSeconds(30));
            instanceStore.DefaultInstanceOwner = view.InstanceOwner;

            return instanceHandle;
        }

        public virtual void LoadWorkflowInstance(WorkflowApplication workflowApplication, Guid workflowInstanceId)
        {
            if (workflowApplication == null) throw new ArgumentNullException("workflowApplication");
            
            workflowApplication.Load(workflowInstanceId);            
        }

        public virtual void Run(WorkflowApplication workflowApplication, InstanceStore instanceStore, InstanceHandle instanceHandle)
        {
            var syncEvent = new AutoResetEvent(false);
            Exception terminationException = null;
            
            workflowApplication.Completed += e =>
            {
                switch (e.CompletionState)
                {
                    case ActivityInstanceState.Faulted:
                        Log.ErrorWrite("Workflow Terminated. Exception: {0}\r\n{1}",
                            e.TerminationException.GetType().FullName,
                            e.TerminationException.Message);

                        // any exceptions which terminate the workflow will be here       
                        terminationException = e.TerminationException;
                        break;
                    case ActivityInstanceState.Canceled:
                        Log.DebugWrite("Workflow Canceled.");
                        break;

                    default:
                        // Completed
                        Log.DebugWrite("Workflow Completed.");
                        break;
                }
            };
            workflowApplication.Aborted += e =>
            {
                Log.ErrorWrite("Workflow Aborted. Exception: {0}\r\n{1}\r\n{2}",
                    e.Reason.GetType().FullName,
                    e.Reason.Message, 
                    e.Reason.ToString());

                terminationException = e.Reason;
            };
            workflowApplication.OnUnhandledException += e =>
            {
                Log.ErrorWrite("Unhandled Exception: {0}\r\n{1}\r\n{2}",
                    e.UnhandledException.GetType().FullName,
                    e.UnhandledException.Message, 
                    e.UnhandledException.ToString());
                
                return UnhandledExceptionAction.Terminate;
            };
            workflowApplication.PersistableIdle += args => PersistableIdleAction.Unload;
            workflowApplication.Unloaded += args =>
            {
                Log.DebugWrite("Workflow Unloaded.");
                syncEvent.Set();
            };

            workflowApplication.Run();
            syncEvent.WaitOne();

            ReleaseWorkflowInstanceOwner(workflowApplication, instanceStore, instanceHandle);

            if (terminationException != null)
            {
                // need to throw the exception so the message isn't ACK'd
                throw new WorkflowHostException(String.Format("Workflow Terminated: {0}", terminationException.Message), terminationException);
            }            
        }

        public virtual BookmarkResumptionResult ResumeWorkflowFromBookmark<TMessage>(WorkflowApplication workflowApplication, TMessage messageBody) where TMessage : class
        {
            if (workflowApplication == null) throw new ArgumentNullException("workflowApplication");
            if(messageBody == null) throw new ArgumentNullException("messageBody");

            return workflowApplication.ResumeBookmark(
                BookmarkMessageNamingStrategy(messageBody.GetType()),
                messageBody);
        }
        
        public virtual void ReleaseWorkflowInstanceOwner(WorkflowApplication workflowApplication, InstanceStore instanceStore, InstanceHandle instanceHandle)
        {
            if (workflowApplication == null) throw new ArgumentNullException("workflowApplication");
            if (instanceStore == null) throw new ArgumentNullException("instanceStore");
            if(instanceHandle == null) throw new ArgumentNullException("instanceHandle");

            var deleteOwnerCmd = new DeleteWorkflowOwnerCommand();
            instanceStore.Execute(instanceHandle, deleteOwnerCmd, TimeSpan.FromSeconds(10));
        }
        
        public virtual void PublishAdvanced<T>(IMessage<T> message, string topic = null) where T : class
        {
            if (!String.IsNullOrWhiteSpace(topic))
                Bus.PublishAdvanced(message, topic);
            else
                Bus.PublishAdvanced(message);
        }
        
        public virtual Task PublishAdvancedAsync<T>(IMessage<T> message, string topic = null) where T : class
        {
            if (!String.IsNullOrWhiteSpace(topic))
                return Bus.PublishAdvancedAsync(message, topic);
            
            return Bus.PublishAdvancedAsync(message);
        }


        public void Reply<TMessage, TResponse>(IMessage<TMessage> message, TResponse response) where TResponse : class
        {
            Bus.Reply(message, response);
        }

        public Task ReplyAsync<TMessage, TResponse>(IMessage<TMessage> message, TResponse response) where TResponse : class
        {
            return Bus.ReplyAsync(message, response);
        }        
    }
}