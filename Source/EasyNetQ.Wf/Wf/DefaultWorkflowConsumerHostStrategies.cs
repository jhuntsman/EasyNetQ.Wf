using System;
using System.Activities;
using System.Activities.DurableInstancing;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Runtime.DurableInstancing;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using EasyNetQ.Wf.AutoConsumers;

namespace EasyNetQ.Wf
{    
    public interface IWorkflowConsumerHostStrategies
    {
        string BookmarkMessageNamingStrategy(Type messageType);
        
        IMessage<TMessage> CreateWorkflowMessage<TMessage>(ActivityContext context, TMessage messageBody, string workflowRouteTopic)
            where TMessage : class;

        WorkflowApplication CreateWorkflowApplication(Activity workflowDefinition, InstanceStore instanceStore, InstanceHandle instanceHandle, IDictionary<string, object> workflowInputs);
        void AddWorkflowExtensions(WorkflowApplication workflowApplication);
        InstanceStore CreateWorkflowInstanceStore(Activity workflowDefinition, out InstanceHandle instanceHandle);
        InstanceHandle RenewDefaultWorkflowInstanceOwner(InstanceStore instanceStore, Activity workflowDefinition);
        void LoadWorkflowInstance(WorkflowApplication workflowApplication, Guid workflowInstanceId);

        bool TryLoadRunnableInstance(WorkflowApplication workflowApplication, TimeSpan timeout);

        bool HasRunnableInstance(InstanceStore instanceStore, InstanceHandle instanceHandle, Activity workflowDefinition, TimeSpan timeout);
        
        void ReleaseWorkflowInstanceOwner(InstanceStore instanceStore, InstanceHandle instanceHandle);
        //BookmarkResumptionResult ResumeWorkflowFromBookmark<TMessage>(WorkflowApplication workflowApplication, TMessage messageBody) where TMessage : class;
        void Run(WorkflowApplication workflowApplication, object bookmarkMessage=null);

        void PublishAdvanced<T>(IMessage<T> message, string topic = null) where T : class;
        Task PublishAdvancedAsync<T>(IMessage<T> message, string topic = null) where T : class;

        void Reply<TMessage, TResponse>(IMessage<TMessage> message, TResponse response) where TResponse : class;
        Task ReplyAsync<TMessage, TResponse>(IMessage<TMessage> message, TResponse response) where TResponse : class;

        XName GetWorkflowHostTypeName(Activity workflowDefinition);              
    }

    public class DefaultWorkflowConsumerHostStrategies : IWorkflowConsumerHostStrategies
    {
        private static readonly XNamespace Workflow45Namespace = XNamespace.Get("urn:schemas-microsoft-com:System.Activities/4.5/properties");
        private static readonly XNamespace Workflow40Namespace = XNamespace.Get("urn:schemas-microsoft-com:System.Activities/4.0/properties");
        private static readonly XName WorkflowHostTypePropertyName = Workflow40Namespace.GetName("WorkflowHostType");
        private static readonly XName DefinitionIdentityFilterName = Workflow45Namespace.GetName("DefinitionIdentityFilter");
        private static readonly XName WorkflowApplicationName = Workflow45Namespace.GetName("WorkflowApplication");
        private static readonly XName DefinitionIdentitiesName = Workflow45Namespace.GetName("DefinitionIdentities");
        
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

        public virtual WorkflowApplication CreateWorkflowApplication(Activity workflowDefinition, InstanceStore instanceStore, InstanceHandle instanceHandle, IDictionary<string, object> workflowInputs)
        {
            if (workflowDefinition == null) throw new ArgumentNullException("workflowDefinition");
            if(instanceStore == null) throw new ArgumentNullException("instanceStore");

            WorkflowApplication wfApp = null;

            if(workflowInputs != null)
                wfApp = new WorkflowApplication(workflowDefinition, workflowInputs);
            else 
                wfApp = new WorkflowApplication(workflowDefinition);

            // setup the Workflow Instance scope
            var wfScope = new Dictionary<XName, object>() {{WorkflowHostTypePropertyName, GetWorkflowHostTypeName(workflowDefinition)}};
            wfApp.AddInitialInstanceValues(wfScope);

            // renew the owner instance handle if expired
            if (!instanceHandle.IsValid)
            {
                try
                {
                    // attempt to free the old instance handle
                    instanceHandle.Free();
                }
                catch { }

                // set a new owner instance handle
                instanceHandle = RenewDefaultWorkflowInstanceOwner(instanceStore, workflowDefinition);
            }

            wfApp.InstanceStore = instanceStore;
                            
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

        public virtual InstanceStore CreateWorkflowInstanceStore(Activity workflowDefinition, out InstanceHandle instanceHandle)
        {            
            var instanceStore = Bus.Advanced.Container.Resolve<InstanceStore>();            

            instanceHandle = RenewDefaultWorkflowInstanceOwner(instanceStore, workflowDefinition);

            return instanceStore;
        }
        
        public virtual XName GetWorkflowHostTypeName(Activity workflowDefinition)
        {
            // TODO: need to apply some sort of version for a workflow            
            return XName.Get(workflowDefinition.GetType().FullName, "http://tempuri.org/EasyNetQ-Wf");
        }
        
        public virtual InstanceHandle RenewDefaultWorkflowInstanceOwner(InstanceStore instanceStore, Activity workflowDefinition)
        {            
            if(instanceStore == null) throw new ArgumentNullException("instanceStore");
            if(workflowDefinition == null) throw new ArgumentNullException("workflowDefinition");
            
            var instanceHandle = instanceStore.CreateInstanceHandle();                      
            var createOwnerCmd = new CreateWorkflowOwnerCommand();
            createOwnerCmd.InstanceOwnerMetadata.Add(WorkflowHostTypePropertyName, new InstanceValue(GetWorkflowHostTypeName(workflowDefinition)));
            /*
            {
                InstanceOwnerMetadata =
                {
                    {WorkflowHostTypePropertyName, new InstanceValue(GetWorkflowHostTypeName(workflowDefinition))},
                    //{DefinitionIdentityFilterName, new InstanceValue(WorkflowIdentityFilter.Any)},
                    //{DefinitionIdentitiesName, new InstanceValue(workflowApplication.DefinitionIdentity)},
                }
            };
            */
            instanceStore.DefaultInstanceOwner = instanceStore.Execute(instanceHandle, createOwnerCmd, TimeSpan.FromSeconds(30)).InstanceOwner;
                
            //WorkflowApplication.CreateDefaultInstanceOwner(instanceStore,workflowApplication.DefinitionIdentity,  WorkflowIdentityFilter.Any);                        
            return instanceHandle;            
        }

        public virtual void LoadWorkflowInstance(WorkflowApplication workflowApplication, Guid workflowInstanceId)
        {
            if (workflowApplication == null) throw new ArgumentNullException("workflowApplication");
            
            workflowApplication.Load(workflowInstanceId);            
        }

        public virtual bool TryLoadRunnableInstance(WorkflowApplication workflowApplication, TimeSpan timeout)
        {
            if (workflowApplication == null) throw new ArgumentNullException("workflowApplication");
            try
            {
                workflowApplication.LoadRunnableInstance(TimeSpan.FromSeconds(1));
                return true;
            }
            catch (Exception) { }            
            return false;
        }

        public virtual bool HasRunnableInstance(InstanceStore instanceStore, InstanceHandle instanceHandle, Activity workflowDefinition, TimeSpan timeout)
        {            
            // renew the owner instance handle if expired
            if (!instanceHandle.IsValid)
            {
                try
                {
                    // attempt to free the old instance handle
                    instanceHandle.Free();
                }
                catch { }

                // set a new owner instance handle
                instanceHandle = RenewDefaultWorkflowInstanceOwner(instanceStore, workflowDefinition);
            }

            var events = instanceStore.WaitForEvents(instanceHandle, timeout);
            foreach (var instancePersistenceEvent in events)
            {
                if (instancePersistenceEvent.Equals(HasRunnableWorkflowEvent.Value))
                {
                    return true;
                }
            }
            return false;
        }
                        
        public virtual void Run(WorkflowApplication workflowApplication, object bookmarkMessage=null)
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
                syncEvent.Set();
            };
            workflowApplication.OnUnhandledException += e =>
            {                
                Log.ErrorWrite("Unhandled Exception: {0}\r\n{1}\r\n{2}",
                    e.UnhandledException.GetType().FullName,
                    e.UnhandledException.Message, 
                    e.UnhandledException.ToString());
                terminationException = e.UnhandledException;
                syncEvent.Set();
                return UnhandledExceptionAction.Terminate;
            };
            workflowApplication.PersistableIdle += args => PersistableIdleAction.Unload;
            workflowApplication.Unloaded += args =>
            {
                Log.DebugWrite("Workflow Unloaded.");
                syncEvent.Set();
            };

            if (bookmarkMessage != null)
            {
                // resume from bookmark
                var bookmarkResult = ResumeWorkflowFromBookmark(workflowApplication, bookmarkMessage);                
                // TODO: check bookmarkResult
            }
            else
            {
                // run the workflow
                workflowApplication.Run();
            }
            
            // wait until the workflow thread completes
            syncEvent.WaitOne();
                                    
            if (terminationException != null)
            {
                // need to throw the exception so the message isn't ACK'd
                throw new WorkflowHostException(String.Format("Workflow Terminated: {0}", terminationException.Message), terminationException);
            }            
        }

        public virtual BookmarkResumptionResult ResumeWorkflowFromBookmark(WorkflowApplication workflowApplication, object messageBody) 
        {
            if (workflowApplication == null) throw new ArgumentNullException("workflowApplication");
            if(messageBody == null) throw new ArgumentNullException("messageBody");

            return workflowApplication.ResumeBookmark(
                BookmarkMessageNamingStrategy(messageBody.GetType()),
                messageBody);
        }
        
        public virtual void ReleaseWorkflowInstanceOwner(InstanceStore instanceStore, InstanceHandle instanceHandle)
        {            
            if (instanceStore == null) throw new ArgumentNullException("instanceStore");
                        
            var deleteOwnerCmd = new DeleteWorkflowOwnerCommand();
            instanceStore.Execute(instanceHandle, deleteOwnerCmd, TimeSpan.FromSeconds(10));
            instanceHandle.Free();            
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