using System;
using System.Activities;
using System.Activities.DurableInstancing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.DurableInstancing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;
using EasyNetQ.Consumer;
using EasyNetQ.FluentConfiguration;
using EasyNetQ.NonGeneric;
using EasyNetQ.Producer;
using EasyNetQ.Topology;
using EasyNetQ.Wf.AutoConsumers;

namespace EasyNetQ.Wf
{
    public class DefaultWorkflowApplicationHost : IWorkflowApplicationHost, IWorkflowApplicationHostBehavior
    {
        private readonly IWorkflowApplicationHostInstanceStore _workflowInstanceStore;
        private readonly IList<ISubscriptionResult> _subscriberRegistry = new List<ISubscriptionResult>();
        private Activity _workflowDefinition;        
        private string _argumentName;
        private Type _argumentType;                        
        private volatile bool _isRunning = false;
        private AutoResetEvent _durableDelayInstanceTaskMonitor = null;
        private Task _durableDelayInstanceTask = null;        
        private long _currentRequestCount = 0;
        

        protected readonly IBus Bus;
        protected readonly IEasyNetQLogger Log;


        protected Activity WorkflowDefinition { get { return _workflowDefinition; } }
        protected string ArgumentName { get { return _argumentName; } }
        protected Type ArgumentType { get { return _argumentType; } }

        public DefaultWorkflowApplicationHost(IBus bus)
        {
            Bus = bus;            
            Log = bus.Advanced.Container.Resolve<IEasyNetQLogger>();

            // create a single instance store per host
            _workflowInstanceStore = bus.Advanced.Container.Resolve<IWorkflowApplicationHostInstanceStore>();
        }
        
        #region IWorkflowApplicationHost

        public IWorkflowApplicationHostInstanceStore WorkflowInstanceStore
        {
            get { return _workflowInstanceStore; }
        }

        public void Initialize(Activity workflowDefinition)
        {
            if (workflowDefinition == null) throw new ArgumentNullException("workflowDefinition");

            var argumentInfo = WorkflowBusExtensions.GetInArgumentsFromActivity(workflowDefinition).Single();

            _workflowDefinition = workflowDefinition;
            _argumentName = argumentInfo.InArgumentName;
            _argumentType = argumentInfo.InArgumentType;

            WorkflowInstanceStore.SetDefaultWorkflowInstanceOwner(GetWorkflowHostTypeName(workflowDefinition));
        }

        protected virtual void OnError(Exception exception)
        {
            Log.ErrorWrite(exception);
        }

        public bool IsRunning
        {
            get { return _isRunning; }
        }

        public virtual void Start()
        {
            if (_isRunning)
                return;

            // setup a long running workflow host
            _isRunning = true;            
            _durableDelayInstanceTaskMonitor = new AutoResetEvent(false);
            _durableDelayInstanceTask = StartDurableDelayInstanceProcessing(TimeSpan.FromSeconds(30));
        }

        public virtual void Stop()
        {            
            // stop the background workflow task
            if (_isRunning)
            {               
                // signal the background task 
                _isRunning = false;

                // stop subscriptions to the host
                foreach (var subscriptionResult in GetSubscriptions().ToArray())
                {
                    // stop the consumers
                    CancelSubscription(subscriptionResult);
                }

                // wait for the background task to complete
                _durableDelayInstanceTaskMonitor.WaitOne();

                while (Interlocked.Read(ref _currentRequestCount) > 0)
                {
                    // waiting for running workflows to complete                    
                    Thread.Sleep(100);
                }
            }
        }

        public IEnumerable<ISubscriptionResult> GetSubscriptions()
        {
            return _subscriberRegistry.AsEnumerable();
        }

        public void AddSubscription(ISubscriptionResult subscription)
        {
            _subscriberRegistry.Add(subscription);
        }

        public void CancelSubscription(ISubscriptionResult subscription)
        {
            // remove from subscription list
            _subscriberRegistry.Remove(subscription);
            subscription.Dispose();
        }
        #endregion

        #region Background Workflow Activation Monitoring

        private bool TryLoadRunnableInstance(WorkflowApplication workflowApplication, TimeSpan timeout)
        {
            if (workflowApplication == null) throw new ArgumentNullException("workflowApplication");
            try
            {                                                
                workflowApplication.LoadRunnableInstance();
                return true;
            }            
            catch(TimeoutException) { }
            catch(InstanceNotReadyException) { }
            catch (Exception ex) { Log.ErrorWrite(ex); }
            return false;
        }

        private bool HasRunnableInstance(TimeSpan timeout)
        {                        
            var events = WorkflowInstanceStore.WaitForEvents(timeout);
            if (events != null)
            {
                foreach (var instancePersistenceEvent in events)
                {
                    if (instancePersistenceEvent.Equals(HasRunnableWorkflowEvent.Value))                                            
                        return true;
                    
                }
            }
            return false;
        }

        private Task StartDurableDelayInstanceProcessing(TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
                {
                    restart:
                    Log.DebugWrite("Starting ");
                    _durableDelayInstanceTaskMonitor.Reset();
                    try
                    {
                        while (_isRunning)
                        {
                            // wait for a runnable workflow instance
                            if (HasRunnableInstance(timeout))
                            {
                                // create a new workflow instance to hold the runnable instance    
                                var wfApp = CreateWorkflowApplication(WorkflowDefinition, null);
                                
                                // load a Runnable Instance                                                                
                                if (TryLoadRunnableInstance(wfApp, TimeSpan.FromSeconds(1)))
                                {
                                    Log.DebugWrite("Waking up Workflow {0}-{1} from Idle...", WorkflowDefinition.GetType().Name, wfApp.Id);
                                  
                                    // resume the instance asynchronously                          
                                    ExecuteWorkflowInstanceAsync(wfApp);
                                }
                            }
                        }
                    }
                    catch (Exception unhandledException)
                    {
                        Log.ErrorWrite(unhandledException);

                        // if the service is still running, then restart the background workflow task
                        if (_isRunning) goto restart;
                    }
                    finally
                    {
                        // signal the background task is completed
                        _durableDelayInstanceTaskMonitor.Set();
                    }
                }, TaskCreationOptions.LongRunning);
        }

        #endregion

#region IDisposable        
        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            Stop();
            
            if (!_disposed)
            {
                if (disposing)
                {
                    foreach (var subscriptionResult in _subscriberRegistry)
                    {
                        subscriptionResult.Dispose();
                    }
                    _subscriberRegistry.Clear();

                    // dispose resources here                    
                    WorkflowInstanceStore.Dispose();
                }
                WorkflowBusExtensions.RemoveWorkflowApplicationHost((IWorkflowApplicationHost)this);
                _disposed = true;
            }

            // dispose unmanaged resources here                
        }                
#endregion

        protected virtual XName GetWorkflowHostTypeName(Activity workflowDefinition)
        {
            return XName.Get(workflowDefinition.GetType().FullName, "http://tempuri.org/EasyNetQ-Wf");
        }

        private void AddDefaultWorkflowHostExtensions(WorkflowApplication workflowApplication)
        {
            workflowApplication.Extensions.Add((IWorkflowApplicationHostBehavior)this);
        }

        public virtual string GetBookmarkNameFromMessageType(Type messageType)
        {
            if(messageType == null) throw new ArgumentNullException("messageType");
            return messageType.FullName;
        }

        public virtual void AddWorkflowHostExtensions(WorkflowApplication workflowApplication)
        {
            // custom workflow extensions could go here if needed
        }

        protected virtual WorkflowApplication CreateWorkflowApplication(Activity workflowDefinition, IDictionary<string, object> args)
        {
            WorkflowApplication wfApp = null;

            if (args != null)
                wfApp = new WorkflowApplication(workflowDefinition, args);
            else
                wfApp = new WorkflowApplication(workflowDefinition);

            // setup the Workflow Instance scope
            var wfScope = new Dictionary<XName, object>() { { WorkflowNamespaces.WorkflowHostTypePropertyName, GetWorkflowHostTypeName(workflowDefinition) } };
            wfApp.AddInitialInstanceValues(wfScope);

            // add required workflow extensions
            AddDefaultWorkflowHostExtensions(wfApp);
            
            // add custom workflow extensions
            AddWorkflowHostExtensions(wfApp);
            
            // set the Workflow Application Instance Store
            wfApp.InstanceStore = WorkflowInstanceStore.Store;

            return wfApp;
        }

        protected virtual Task ExecuteWorkflowInstanceAsync(WorkflowApplication workflowApplication,
            object bookmarkResume = null)
        {

            Interlocked.Increment(ref _currentRequestCount);
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();

            workflowApplication.PersistableIdle = (e) => PersistableIdleAction.Unload;
            workflowApplication.Unloaded = (e) => tcs.TrySetResult(true);
            workflowApplication.Aborted = (e) =>
            {
                Log.ErrorWrite(String.Format("Workflow {0}-{1} aborted", WorkflowDefinition.GetType().Name, e.InstanceId), e.Reason);
                
                tcs.TrySetException(
                    new WorkflowHostException(
                        String.Format("Workflow {0}-{1} aborted", WorkflowDefinition.GetType().Name, e.InstanceId),
                        e.Reason));
                
            };
            workflowApplication.OnUnhandledException = (e) =>
            {
                Log.ErrorWrite(String.Format("Workflow {0}-{1} threw unhandled exception: {2}", WorkflowDefinition.GetType().Name,
                    e.InstanceId, e.UnhandledException.ToString()));
                tcs.TrySetException(
                    new WorkflowHostException(
                        String.Format("Workflow {0}-{1} threw unhandled exception", WorkflowDefinition.GetType().Name,
                            e.InstanceId), e.UnhandledException));
                return UnhandledExceptionAction.Abort;
            };
            workflowApplication.Completed = (e) =>
            {
                switch (e.CompletionState)
                {
                    case ActivityInstanceState.Faulted:
                        Log.ErrorWrite("Workflow {0}-{1} Terminated. Exception: {2}\r\n{3}",
                            WorkflowDefinition.GetType().FullName,
                            e.InstanceId,
                            e.TerminationException.GetType().FullName,
                            e.TerminationException.Message);

                        // any exceptions which terminate the workflow will be here       
                        tcs.TrySetException(
                            new WorkflowHostException(
                                String.Format("Workflow {0}-{1} faulted", WorkflowDefinition.GetType().Name,
                                    e.InstanceId), e.TerminationException));
                        break;
                    case ActivityInstanceState.Canceled:
                        Log.DebugWrite("Workflow {0}-{1} Canceled.", WorkflowDefinition.GetType().FullName, e.InstanceId);
                        break;

                    default:
                        // Completed
                        Log.DebugWrite("Workflow {0}-{1} Completed.", WorkflowDefinition.GetType().FullName, e.InstanceId);
                        break;
                }
            };

            if (bookmarkResume != null)
            {
                // set the bookmark and resume the workflow
                var bookmarkResult =
                    workflowApplication.ResumeBookmark(GetBookmarkNameFromMessageType(bookmarkResume.GetType()),
                        bookmarkResume);
            }
            else
            {
                // persist the workflow before we start to get an instance id
                workflowApplication.Persist();
                Guid workflowInstanceId = workflowApplication.Id;

                // TODO: need to add record to persistance mapping the workflowInstanceId and its Workflow Definition

                // run the workflow
                workflowApplication.Run();
            }

            // we are waiting here so that exceptions will be thrown properly
            // and our Try/Finally block remains intact
            return tcs.Task.ContinueWith(t =>
            {
                Interlocked.Decrement(ref _currentRequestCount);
            }, TaskContinuationOptions.ExecuteSynchronously);

        }

        public void OnDispatchMessage(object message)
        {
            if (message == null) throw new ArgumentNullException("message");

            OnDispatchMessageAsync(message).Wait();
        }

        public virtual Task OnDispatchMessageAsync(object message)
        {
            if (message == null) throw new ArgumentNullException("message");

            WorkflowApplication wfApp = null;
            object bookmark = null;
            if (message.GetType() == ArgumentType)
            {
                // start a new instance
                var workflowArgs = new Dictionary<string, object>() {{ArgumentName, message}};
                wfApp = CreateWorkflowApplication(WorkflowDefinition, workflowArgs);
            }
            else
            {
                // find correlation id guid                    
                var correlationValue = CorrelatesOnAttribute.GetCorrelatesOnValue(message).Split(new[] {'|'}, StringSplitOptions.RemoveEmptyEntries);

                // resume a persisted instance using the correlationId                        
                Guid workflowInstanceId;
                if (!Guid.TryParse(correlationValue[0], out workflowInstanceId))
                {
                    throw new WorkflowHostException(String.Format("Correlation Id must be a Guid (or Guid string) on type {0}", message.GetType().FullName));
                }

                bookmark = message;
                wfApp = CreateWorkflowApplication(WorkflowDefinition, null);
                try
                {
                    wfApp.Load(workflowInstanceId);
                    Log.DebugWrite("Workflow[{0}-{1}] Loaded", WorkflowDefinition.GetType().FullName, workflowInstanceId);
                }
                catch (Exception ex)
                {
                    Log.ErrorWrite("Workflow[{0}-{1}] could not be loaded: {2}", WorkflowDefinition.GetType().FullName, workflowInstanceId, ex.ToString());
                    throw;
                }
            }

            // execute the workflow
            return ExecuteWorkflowInstanceAsync(wfApp, bookmark);
        }

        public void OnDispatchMessageAdvanced(IMessage message, MessageReceivedInfo info)
        {
            OnDispatchMessage(message.GetBody());
        }

        public Task OnDispatchMessageAdvancedAsync(IMessage message, MessageReceivedInfo info)
        {
            return OnDispatchMessageAsync(message.GetBody());
        }

        #region IWorkflowApplicationHostBehavior

        public T Resolve<T>() where T:class
        {
            return Bus.Advanced.Container.Resolve<T>();
        }

        public Task PublishMessageWithCorrelationAsync(Guid workflowInstanceId, object message, string topic = null)
        {
            if (message == null) throw new ArgumentNullException("message");

            string correlatesOnValue = null;
            if (!CorrelatesOnAttribute.TryGetCorrelatesOnValue(message, out correlatesOnValue))
            {
                // we are the Parent, so we set Correlation to ourselves
                CorrelatesOnAttribute.SetCorrelatesOnValue(message, workflowInstanceId, WorkflowDefinition.GetType().Name);
            }
            else
            {
                // We are responding to a Parent, so we will route using their correlation and
                // leave it on the message
                topic = CorrelatesOnAttribute.GetCorrelatesOnValue(message) ?? topic;
            }

            return PublishMessageAsync(message, topic);
        }

        public Task PublishMessageAsync(object message, string topic = null)
        {
            if (message == null) throw new ArgumentNullException("message");

            if (!String.IsNullOrWhiteSpace(topic))
            {
                return Bus.PublishExAsync(message.GetType(), message, topic);
            }
            return Bus.PublishExAsync(message.GetType(), message);
        }

        public void PublishMessageWithCorrelation(Guid workflowInstanceId, object message, string topic = null)
        {
            if (message == null) throw new ArgumentNullException("message");

            string correlatesOnValue = null;
            if (!CorrelatesOnAttribute.TryGetCorrelatesOnValue(message, out correlatesOnValue))
            {
                // we are the Parent, so we set Correlation to ourselves
                CorrelatesOnAttribute.SetCorrelatesOnValue(message, workflowInstanceId, WorkflowDefinition.GetType().Name);
            }
            else
            {
                // We are responding to a Parent, so we will route using their correlation and
                // leave it on the message
                topic = CorrelatesOnAttribute.GetCorrelatesOnValue(message) ?? topic;
            }

            PublishMessage(message, topic);
        }

        public void PublishMessage(object message, string topic = null)
        {
            if (message == null) throw new ArgumentNullException("message");

            if (!String.IsNullOrWhiteSpace(topic))
            {
                Bus.PublishEx(message.GetType(), message, topic);
            }
            else
            {
                Bus.PublishEx(message.GetType(), message);
            }
        }

        #endregion        
    }
}