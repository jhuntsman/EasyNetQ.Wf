using System;
using System.Activities;
using System.Activities.DurableInstancing;
using System.Collections.Generic;
using System.IO;
using System.Runtime.DurableInstancing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;

namespace EasyNetQ.Wf
{
    public class WorkflowConsumerHost : ConsumerHostBase, IRunnableConsumerHost
    {          
        private readonly Activity WorkflowDefinition;        
        private readonly string ArgumentName;
        private readonly Type ArgumentType;
        private readonly IWorkflowConsumerHostStrategies WorkflowStrategy;
        
        private volatile bool _isRunning = false;
        private AutoResetEvent _backgroundTaskEvent = null;        
        private Task _backgroundWorkflowTask = null;

        private InstanceHandle WorkflowHostOwnerHandle = null;
        private InstanceStore WorkflowHostInstanceStore = null;
                        
        public WorkflowConsumerHost(IBus bus, Activity workflowDefinition, string argumentName, Type argumentType)
            : base(bus)
        {
            if(workflowDefinition == null) throw new ArgumentNullException("workflowDefinition");
            if(String.IsNullOrWhiteSpace(argumentName)) throw new ArgumentNullException("argumentName");
            if(argumentType == null) throw new ArgumentNullException("argumentType");

            WorkflowStrategy = bus.Advanced.Container.Resolve<IWorkflowConsumerHostStrategies>();
            
            WorkflowDefinition = workflowDefinition;
            ArgumentName = argumentName;
            ArgumentType = argumentType;

            // create a single instance store per host
            WorkflowHostInstanceStore = WorkflowStrategy.CreateWorkflowInstanceStore(workflowDefinition, out WorkflowHostOwnerHandle);
        }

        public bool IsRunning
        {
            get { return _isRunning; }
        }

        public virtual void Start()
        {            
            // setup a long running workflow host
            _isRunning = true;            
            _backgroundTaskEvent = new AutoResetEvent(false);
            _backgroundWorkflowTask = StartLongRunningWorkflowHost(TimeSpan.FromSeconds(5));
        }

        public virtual void Stop()
        {            
            // stop the background workflow task
            if (_isRunning)
            {                
                _isRunning = false;
                _backgroundTaskEvent.WaitOne();
            }
        }

        private Task StartLongRunningWorkflowHost(TimeSpan timeout)
        {            
            return Task.Factory.StartNew(() =>
            {                                
                while (_isRunning)
                {                    
                    // wait for a runnable workflow instance
                    if (WorkflowStrategy.HasRunnableInstance(WorkflowHostInstanceStore, WorkflowHostOwnerHandle, WorkflowDefinition, timeout))
                    {                        
                        // create a new workflow instance to hold the runnable instance    
                        var wfApp = WorkflowStrategy.CreateWorkflowApplication(WorkflowDefinition, WorkflowHostInstanceStore, WorkflowHostOwnerHandle, null);

                        // load a Runnable Instance
                        if (WorkflowStrategy.TryLoadRunnableInstance(wfApp, TimeSpan.FromSeconds(1)))
                        {
                            try
                            {
                                // resume the instance
                                WorkflowStrategy.Run(wfApp);
                            }
                            catch (WorkflowHostException) { }                            
                            catch (Exception ex)
                            {
                                Log.ErrorWrite(ex);
                            }
                        }
                    }                               
                }
                _backgroundTaskEvent.Set();
            }, TaskCreationOptions.LongRunning);
        }
                        
        private bool _disposed = false;
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // dispose any managed resources
                    WorkflowStrategy.ReleaseWorkflowInstanceOwner(WorkflowHostInstanceStore, WorkflowHostOwnerHandle);
                }                
                _disposed = true;
            }

            base.Dispose(disposing);
        }

        private static object GetMessageBody(object message)
        {
            var messageBody = message.GetType().GetProperty("Body");
            return messageBody.GetValue(message, null);
        }

        private static MessageProperties GetMessageProperties(object message)
        {
            var messageProperties = message.GetType().GetProperty("Properties");
            return (MessageProperties) messageProperties.GetValue(message, null);
        }
        
        public override void OnConsumeAdvanced(object message, MessageReceivedInfo info)
        {
            var messageProperties = GetMessageProperties(message);
            var messageBody = GetMessageBody(message);

            if (messageBody.GetType() == ArgumentType)
                StartWorkflow(messageBody, messageProperties, info);
            else            
                ResumeWorkflow(messageBody, messageProperties, info);
        }
                                
        private void StartWorkflow(object messageBody, MessageProperties messageProperties, MessageReceivedInfo info)
        {            
            var workflowArgs = new Dictionary<string, object>() {{ArgumentName, messageBody}};            
            var wfApp = WorkflowStrategy.CreateWorkflowApplication(WorkflowDefinition, WorkflowHostInstanceStore, WorkflowHostOwnerHandle, workflowArgs);
                                    
            Log.DebugWrite("Workflow Created - {0}", wfApp.Id);
            
            WorkflowStrategy.Run(wfApp);            
        }

        private void ResumeWorkflow(object messageBody, MessageProperties messageProperties, MessageReceivedInfo info)
        {
            if (!messageProperties.HeadersPresent ||
                !messageProperties.Headers.ContainsKey(WorkflowConstants.MessageHeaderWorkflowInstanceIdKey))
            {
                // error - message was not sent correctly to the workfow
                throw new InvalidOperationException(
                    String.Format("{0} message sent to workflow {1} did not contain header {2}",
                        messageBody.GetType().FullName,
                        this.WorkflowDefinition.GetType().FullName,
                        WorkflowConstants.MessageHeaderWorkflowInstanceIdKey
                        ));
            }

            byte[] rawHeaderWorkflowInstanceId = messageProperties.Headers[WorkflowConstants.MessageHeaderWorkflowInstanceIdKey] as byte[];
            if (rawHeaderWorkflowInstanceId == null)
            {
                // error - message was not sent correctly to the workfow
                throw new InvalidOperationException(
                    String.Format("{0} message sent to workflow {1} contained an invalid header {2}",
                        messageBody.GetType().FullName,
                        this.WorkflowDefinition.GetType().FullName,
                        WorkflowConstants.MessageHeaderWorkflowInstanceIdKey
                        ));
            }

            // load a running workflow instance
            // TODO: if unable to run the workflow instance (e.g. already running), need a way to NACK the message indefinitely or wait
            //       (this situation should be very rare if ever)
            Guid workflowInstanceId = Guid.Parse(Encoding.UTF8.GetString(rawHeaderWorkflowInstanceId));
            var wfApp = WorkflowStrategy.CreateWorkflowApplication(WorkflowDefinition, WorkflowHostInstanceStore, WorkflowHostOwnerHandle, null);

            try
            {
                WorkflowStrategy.LoadWorkflowInstance(wfApp, workflowInstanceId);
                Log.DebugWrite("Workflow[{0}-{1}] Loaded", WorkflowDefinition.GetType().FullName, workflowInstanceId);
            }
            catch (Exception ex)
            {
                Log.ErrorWrite("Workflow[{0}-{1}] could not be loaded: {2}", WorkflowDefinition.GetType().FullName, workflowInstanceId, ex.ToString());
                throw;
            }

            // try to resume the workflow at the bookmark
            WorkflowStrategy.Run(wfApp, messageBody);
        }
    }
}