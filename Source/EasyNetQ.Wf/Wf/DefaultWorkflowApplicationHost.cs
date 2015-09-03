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
using EasyNetQ.NonGeneric;
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
        private AutoResetEvent _backgroundTaskEvent = null;
        private Task _backgroundWorkflowTask = null;        
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

        public void Initialize(Activity workflowDefinition, string argumentName, Type argumentType)
        {
            if (workflowDefinition == null) throw new ArgumentNullException("workflowDefinition");
            if (String.IsNullOrWhiteSpace(argumentName)) throw new ArgumentNullException("argumentName");
            if (argumentType == null) throw new ArgumentNullException("argumentType");

            _workflowDefinition = workflowDefinition;
            _argumentName = argumentName;
            _argumentType = argumentType;

            WorkflowInstanceStore.SetDefaultWorkflowInstanceOwner(GetWorkflowHostTypeName(workflowDefinition));
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
            //_backgroundWorkflowTask = StartLongRunningWorkflowHost(TimeSpan.FromSeconds(5));
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
                //_backgroundTaskEvent.WaitOne();

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
                workflowApplication.LoadRunnableInstance(TimeSpan.FromSeconds(1));
                return true;
            }
            catch (InstanceNotReadyException) { }
            catch (Exception ex) { Log.ErrorWrite(ex); }
            return false;
        }

        private bool HasRunnableInstance(ref InstanceHandle instanceHandle, TimeSpan timeout)
        {            
            var events = WorkflowInstanceStore.WaitForEvents(ref instanceHandle, timeout);
            foreach (var instancePersistenceEvent in events)
            {
                if (instancePersistenceEvent.Equals(HasRunnableWorkflowEvent.Value))                
                    return true;                
            }
            return false;
        }

        private Task StartLongRunningWorkflowHost(TimeSpan timeout)
        {
            return Task.Factory.StartNew(
#if NET4
                () =>
#else
                async () =>
#endif
                {
                    InstanceHandle instanceHandle = WorkflowInstanceStore.Store.CreateInstanceHandle();
                    restart:
                    _backgroundTaskEvent.Reset();
                    try
                    {
                        while (_isRunning)
                        {
                            // wait for a runnable workflow instance
                            if (HasRunnableInstance(ref instanceHandle, timeout))
                            {
                                // create a new workflow instance to hold the runnable instance    
                                var wfApp = CreateWorkflowApplication(WorkflowDefinition, null);

                                // load a Runnable Instance
                                if (TryLoadRunnableInstance(wfApp, TimeSpan.FromSeconds(1)))
                                {
                                    try
                                    {
                                        // resume the instance
#if NET4
                                        ExecuteWorkflowInstanceAsync(wfApp).Wait();
#else
                                        await ExecuteWorkflowInstanceAsync(wfApp);
#endif
                                    }
                                    catch (WorkflowHostException)
                                    {
                                        // ignore exceptions handled by the WorkflowHost
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.ErrorWrite(ex);
                                    }
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
                        instanceHandle?.Free();
                        // signal the background task is completed
                        _backgroundTaskEvent.Set();
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
        
        protected virtual
#if !NET4
            async 
#endif
            Task ExecuteWorkflowInstanceAsync(WorkflowApplication workflowApplication, object bookmarkResume = null)
        {
#if !NET4
            try
            {
#endif
                Interlocked.Increment(ref _currentRequestCount);
                var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                
                workflowApplication.PersistableIdle = (e) => PersistableIdleAction.Unload;
                workflowApplication.Unloaded = (e) => tcs.SetResult(true);
                workflowApplication.Aborted = (e) => tcs.SetException(new WorkflowHostException(String.Format("Workflow {0}-{1} aborted", WorkflowDefinition.GetType().Name, e.InstanceId), e.Reason));
                workflowApplication.OnUnhandledException = (e) =>
                {
                    tcs.SetException(new WorkflowHostException(String.Format("Workflow {0}-{1} threw unhandled exception", WorkflowDefinition.GetType().Name, e.InstanceId), e.UnhandledException));
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
                            tcs.SetException(new WorkflowHostException(String.Format("Workflow {0}-{1} faulted", WorkflowDefinition.GetType().Name, e.InstanceId), e.TerminationException));
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
                    var bookmarkResult = workflowApplication.ResumeBookmark(GetBookmarkNameFromMessageType(bookmarkResume.GetType()), bookmarkResume);
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
#if NET4
                return tcs.Task.ContinueWith((t) =>
                {
                    Interlocked.Decrement(ref _currentRequestCount);
                });
#else
                await tcs.Task;
            }            
            finally
            {
                Interlocked.Decrement(ref _currentRequestCount);
            }
#endif
        }

        public virtual void OnDispatchMessage(object message)
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
                CorrelatesOnAttribute.SetCorrelatesOnValue(message, workflowInstanceId, WorkflowDefinition);
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
                CorrelatesOnAttribute.SetCorrelatesOnValue(message, workflowInstanceId, WorkflowDefinition);
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