using System;
using System.Activities;
using System.Collections.Generic;
using System.Runtime.DurableInstancing;
using System.Text;
using System.Timers;

namespace EasyNetQ.Wf
{        
    public class WorkflowConsumerHost : ConsumerHost
    {          
        private readonly Activity WorkflowDefinition;        
        private readonly string ArgumentName;
        private readonly Type ArgumentType;
        private readonly IWorkflowConsumerHostStrategies WorkflowStrategy;
        
        private System.Timers.Timer _idleExecutionTimer = null;
        
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
        }
        
        public override void Start()
        {
            // TODO: idle timer to run for runnable workflow instances
            //_idleExecutionTimer = new System.Timers.Timer(TimeSpan.FromSeconds(10).TotalMilliseconds) { AutoReset = true };
            try
            {

            }
            catch (Exception)
            {

            }
            finally
            {
                //_idleExecutionTimer.Start();
            }
        }

        private void onTimerElapsed(object sender, ElapsedEventArgs e)
        {
            while (true)
            {
                var wfApp = WorkflowStrategy.CreateWorkflowApplication(WorkflowDefinition, null);
                var instanceStore = WorkflowStrategy.CreateWorkflowInstanceStore(wfApp);
                var instanceHandle = WorkflowStrategy.InitializeWorkflowInstanceOwner(wfApp, instanceStore, WorkflowDefinition);
                try
                {
                    // TODO: need to solve a way to load a specific type of workflow instance from a multi-instance store
                    wfApp.LoadRunnableInstance(TimeSpan.FromSeconds(1));

                    WorkflowStrategy.Run(wfApp, instanceStore, instanceHandle);
                }
                catch (InstanceNotReadyException)
                {
                    // ignore because we are polling for instances that are runnable
                    break;
                }
                catch (Exception)
                {
                    return;
                }
            }
        }
        

        private bool _disposed = false;
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // top the timer here   
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
            var wfApp = WorkflowStrategy.CreateWorkflowApplication(WorkflowDefinition, workflowArgs);

            var instanceStore = WorkflowStrategy.CreateWorkflowInstanceStore(wfApp);
            var instanceHandle = WorkflowStrategy.InitializeWorkflowInstanceOwner(wfApp, instanceStore, WorkflowDefinition);

            Log.DebugWrite("Workflow Created - {0}", wfApp.Id);

            WorkflowStrategy.Run(wfApp, instanceStore, instanceHandle);
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
            var wfApp = WorkflowStrategy.CreateWorkflowApplication(WorkflowDefinition, null);
            var instanceStore = WorkflowStrategy.CreateWorkflowInstanceStore(wfApp);
            var instanceHandle = WorkflowStrategy.InitializeWorkflowInstanceOwner(wfApp, instanceStore, WorkflowDefinition);

            try
            {                
                WorkflowStrategy.LoadWorkflowInstance(wfApp, workflowInstanceId);
                Log.DebugWrite("Workflow[{0}-{1}] Loaded", WorkflowDefinition.GetType().FullName, workflowInstanceId);
            }
            catch(Exception ex)
            {
                Log.ErrorWrite("Workflow[{0}-{1}] could not be loaded: {2}", WorkflowDefinition.GetType().FullName, workflowInstanceId, ex.ToString());
                throw;
            }
            
            // try to resume the workflow at the bookmark
            WorkflowStrategy.ResumeWorkflowFromBookmark(wfApp, messageBody);
                        
            // run the workflow
            WorkflowStrategy.Run(wfApp, instanceStore, instanceHandle);
        }             
    }
}