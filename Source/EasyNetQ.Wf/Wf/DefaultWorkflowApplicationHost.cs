using System;
using System.Activities;
using System.Activities.DurableInstancing;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.DurableInstancing;
using System.Threading;
using System.Threading.Tasks;
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
        private bool _isRunning = false;
        private CancellationTokenSource _durableDelayInstanceCancellationTokenSource = null;        
        private Task _durableDelayInstanceTask = null;        
        private long _currentTaskCount = 0;
        
        protected readonly IBus Bus;
        protected readonly IEasyNetQLogger Log;
        protected readonly IWorkflowApplicationHostPerformanceMonitor PerfMon;
        
        protected string ArgumentName { get { return _argumentName; } }
        protected Type ArgumentType { get { return _argumentType; } }

        public DefaultWorkflowApplicationHost(IBus bus)
        {
            Bus = bus;            
            Log = bus.Advanced.Container.Resolve<IEasyNetQLogger>();
            PerfMon = bus.Advanced.Container.Resolve<IWorkflowApplicationHostPerformanceMonitor>();

            // create a single instance store per host
            _workflowInstanceStore = bus.Advanced.Container.Resolve<IWorkflowApplicationHostInstanceStore>();
        }

        #region IWorkflowApplicationHost

        public Activity WorkflowDefinition { get { return _workflowDefinition; } }

        public event EventHandler<RequestAdditionalTimeEventArgs> RequestAdditionalTime;

        public IWorkflowApplicationHostInstanceStore WorkflowInstanceStore
        {
            get { return _workflowInstanceStore; }
        }

        public void Initialize(Activity workflowDefinition)
        {
            if (_isRunning) throw new InvalidOperationException("Initialize cannot be called when WorkflowApplicationHost is running");
            if (workflowDefinition == null) throw new ArgumentNullException("workflowDefinition");

            var argumentInfo = WorkflowBusExtensions.GetInArgumentsFromActivity(workflowDefinition).Single();

            _workflowDefinition = workflowDefinition;
            _argumentName = argumentInfo.InArgumentName;
            _argumentType = argumentInfo.InArgumentType;

            WorkflowInstanceStore.SetDefaultWorkflowInstanceOwner(GetWorkflowHostTypeName(workflowDefinition));
        }

        protected virtual string GetWorkflowName()
        {
            return WorkflowDefinition.GetType().Name;
        }

        protected virtual void OnError(WorkflowApplication workflowApplication, Exception exception)
        {
            Log.ErrorWrite(String.Format("Workflow {0}-{1} error", workflowApplication.WorkflowDefinition.GetType().Name, workflowApplication.Id), exception);
        }

        public bool IsRunning
        {
            get { return _isRunning; }
        }

        public virtual void Start()
        {
            if (_isRunning)
                return;

            Log.InfoWrite("WorkflowApplicationHost {0} starting...", WorkflowDefinition.GetType().Name);

            // setup a long running workflow host
            Log.InfoWrite("Starting durable delay monitor task...");
            
            _durableDelayInstanceCancellationTokenSource = new CancellationTokenSource();            
            _durableDelayInstanceTask = StartDurableDelayInstanceProcessing(_durableDelayInstanceCancellationTokenSource.Token, TimeSpan.FromSeconds(30));

            Log.InfoWrite("WorkflowApplicationHost {0} started", WorkflowDefinition.GetType().Name);
            _isRunning = true;
        }

        public virtual void Stop()
        {
            // stop the background workflow task
            if (!_isRunning)
                return;

            Log.InfoWrite("WorkflowApplicationHost {0} stopping...", WorkflowDefinition.GetType().Name);

            // signal the background task 
            Log.InfoWrite("Cancelling durable delay monitor task...");
            OnRequestAdditionalTime(TimeSpan.FromSeconds(5));
            _durableDelayInstanceCancellationTokenSource.Cancel();

            // stop subscriptions to the host
            foreach (var subscriptionResult in GetSubscriptions().ToArray())
            {
                // stop the consumers
                Log.InfoWrite("Cancelling consumer subscription for exchange {0} on queue {1}", subscriptionResult.Exchange.Name, subscriptionResult.Queue.Name);
                CancelSubscription(subscriptionResult);
            }

            int counter = 0;
            // wait for the background task to complete     
            if (_durableDelayInstanceTask != null && (!_durableDelayInstanceTask.IsCanceled || !_durableDelayInstanceTask.IsCompleted))
            {
                Log.InfoWrite("Waiting for durable delay monitor task to complete...");
                try
                {
                    counter = 0;
                    do
                    {
                        // request additional time from Service Manager
                        OnRequestAdditionalTime(TimeSpan.FromSeconds(20));

                        if (_durableDelayInstanceTask.Wait(TimeSpan.FromSeconds(20)))
                        {
                            // task has completed, so break out
                            break;
                        }
                        // task is still running, continue waiting for the task to complete
                        counter++;
                    } while (counter < 4);

                    Log.ErrorWrite("Durable delay monitor task is not responding, forcing Dispose and continuing to shutdown");
                    _durableDelayInstanceTask.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Task was already disposed, so no problem
                }
                catch (TaskCanceledException)
                {
                    // Task has been cancelled and is completed
                    Log.InfoWrite("Durable delay monitor task has been cancelled");
                }
                catch (Exception unexpectedException)
                {
                    Log.ErrorWrite(unexpectedException);
                }
            }
            else
            {
                Log.InfoWrite("Durable delay monitor task has ended");
            }
            
            counter = 0;
            while (counter < 30 && Interlocked.Read(ref _currentTaskCount) > 0)
            {
                Log.InfoWrite("Waiting for {0} running workflows to complete...", Interlocked.Read(ref _currentTaskCount));

                OnRequestAdditionalTime(TimeSpan.FromSeconds(5));   

                // waiting for running workflows to complete                    
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }

            if (Interlocked.Read(ref _currentTaskCount) > 0)
            {
                Log.ErrorWrite("Continuing to shutdown, {0} running workflows are still waiting to complete...", Interlocked.Read(ref _currentTaskCount));
            }
            else
            {
                Log.InfoWrite("All running workflows have completed");
            }            

            _isRunning = false;
            Log.InfoWrite("WorkflowApplicationHost {0} stopped", WorkflowDefinition.GetType().Name);
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

        private Task StartDurableDelayInstanceProcessing(CancellationToken cancellationToken, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
                {
                    restart:
                    Log.InfoWrite("Starting Durable Delay monitor");                    
                    try
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            // wait for a runnable workflow instance
                            if (HasRunnableInstance(timeout))
                            {
                                // create a new workflow instance to hold the runnable instance    
                                var wfApp = CreateWorkflowApplication(WorkflowDefinition, null);
                                
                                // load a Runnable Instance                                                                
                                if (TryLoadRunnableInstance(wfApp, TimeSpan.FromSeconds(1)))
                                {
                                    Log.InfoWrite("Waking up Workflow {0}-{1} from Idle...", WorkflowDefinition.GetType().Name, wfApp.Id);
                                  
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
                        if (!cancellationToken.IsCancellationRequested) goto restart;
                    }                    
                }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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

        protected void OnRequestAdditionalTime(TimeSpan timeout)
        {
            if (RequestAdditionalTime != null)
                RequestAdditionalTime(this, new RequestAdditionalTimeEventArgs(timeout));
        }

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
            DateTime startingTime = DateTime.UtcNow;
            string workflowName = GetWorkflowName();
            Interlocked.Increment(ref _currentTaskCount);
            var tcs = new System.Threading.Tasks.TaskCompletionSource<object>();
            
            workflowApplication.PersistableIdle = (e) => PersistableIdleAction.Unload;
            workflowApplication.Unloaded = (e) => tcs.TrySetResult(null);
            workflowApplication.Aborted = (e) =>
            {
                var exception = new WorkflowHostException(
                    String.Format("Workflow {0}-{1} aborted", WorkflowDefinition.GetType().Name, e.InstanceId),
                    e.Reason);

                OnError(workflowApplication, exception);
                                
                tcs.TrySetException(exception);                
            };
            workflowApplication.OnUnhandledException = (e) =>
            {
                var exception = new WorkflowHostException(
                    String.Format("Workflow {0}-{1} threw unhandled exception", WorkflowDefinition.GetType().Name,
                        e.InstanceId), e.UnhandledException);
                OnError(workflowApplication, exception);

                tcs.TrySetException(exception);

                return UnhandledExceptionAction.Abort;
            };
            workflowApplication.Completed = (e) =>
            {
                switch (e.CompletionState)
                {
                    case ActivityInstanceState.Faulted:
                        // any exceptions which terminate the workflow will be here
                        var exception = new WorkflowHostException(
                            String.Format("Workflow {0}-{1} faulted", WorkflowDefinition.GetType().Name,
                                e.InstanceId), e.TerminationException);

                        OnError(workflowApplication, exception);
                                                                        
                        tcs.TrySetException(exception);
                        break;
                    case ActivityInstanceState.Canceled:
                        Log.InfoWrite("Workflow {0}-{1} Canceled.", WorkflowDefinition.GetType().FullName, e.InstanceId);
                        break;

                    default:
                        // Completed
                        Log.InfoWrite("Workflow {0}-{1} Completed.", WorkflowDefinition.GetType().FullName, e.InstanceId);
                        break;
                }
            };
            
            if (bookmarkResume != null)
            {
                PerfMon.WorkflowResumed(workflowName);
                PerfMon.WorkflowRunning(workflowName);

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
                PerfMon.WorkflowStarted(workflowName);
                PerfMon.WorkflowRunning(workflowName);

                // run the workflow
                workflowApplication.Run();
            }

            // decrement the request counter when the workflow has completed
            tcs.Task.ContinueWith(task =>
            {                
                Interlocked.Decrement(ref _currentTaskCount);

                DateTime stopTime = DateTime.UtcNow;
                PerfMon.WorkflowDuration(workflowName, stopTime.Subtract(startingTime));

                if (task.IsFaulted)
                {
                    PerfMon.WorkflowFaulted(workflowName);
                }
                else
                {
                    PerfMon.WorkflowCompleted(workflowName);
                }                

            }, TaskContinuationOptions.ExecuteSynchronously);
            
            // return the workflow execution task
            return tcs.Task;
        }

        public void OnDispatchMessage(object message)
        {
            if (message == null) throw new ArgumentNullException("message");

            OnDispatchMessageAsync(message).Wait();
        }

        public void OnDispatchMessageAdvanced(IMessage message, MessageReceivedInfo info)
        {
            OnDispatchMessage(message.GetBody());
        }

        public Task OnDispatchMessageAdvancedAsync(IMessage message, MessageReceivedInfo info)
        {
            return OnDispatchMessageAsync(message.GetBody());
        }

        public virtual Task OnDispatchMessageAsync(object message)
        {
            if (message == null) throw new ArgumentNullException("message");
            string workflowName = GetWorkflowName();
            PerfMon.MessageConsumed(workflowName);

            WorkflowApplication wfApp = null;
            object bookmark = null;
            if (message.GetType() == ArgumentType)
            {
                Log.InfoWrite("WorkflowApplicationHost::OnDispatchMessageAsync - Starting workflow instance {0} for message {1}", WorkflowDefinition.GetType().Name, message.GetType().Name);

                // start a new instance
                var workflowArgs = new Dictionary<string, object>() {{ArgumentName, message}};
                wfApp = CreateWorkflowApplication(WorkflowDefinition, workflowArgs);
            }
            else
            {                
                // find correlation id guid                    
                var correlationKey = CorrelatesOnAttribute.GetCorrelatesOnValue(message);
                var correlationValue = correlationKey.Split(new[] {'|'}, StringSplitOptions.RemoveEmptyEntries);

                // resume a persisted instance using the correlationId                        
                Guid workflowInstanceId;
                if (!Guid.TryParse(correlationValue[0], out workflowInstanceId))
                {
                    throw new WorkflowHostException(String.Format("Correlation Id must be a Guid (or Guid string) on type {0}", message.GetType().FullName));
                }

                Log.InfoWrite("WorkflowApplicationHost::OnDispatchMessageAsync - Resuming workflow {0} for message bookmark {1} instance id {2}", WorkflowDefinition.GetType().Name, message.GetType().Name, workflowInstanceId);

                bookmark = message;
                wfApp = CreateWorkflowApplication(WorkflowDefinition, null);
                try
                {
                    wfApp.Load(workflowInstanceId);
                    Log.InfoWrite("Workflow[{0}-{1}] Loaded", WorkflowDefinition.GetType().FullName, workflowInstanceId);
                }
                catch (Exception ex)
                {
                    PerfMon.WorkflowFaulted(workflowName);
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

                correlatesOnValue = CorrelatesOnAttribute.GetCorrelatesOnValue(message);

                // if there is an explicit topic, then we use it, otherwise, inspect the message type for an OnTopic attribute
                topic = topic ?? AdvancedBusConsumerExtensions.GetTopicForMessage(message);
            }
            else
            {
                // We are responding to a Parent, so we will route using their correlation and
                // leave it on the message                
                topic = topic ?? (CorrelatesOnAttribute.GetCorrelatesOnValue(message) ?? AdvancedBusConsumerExtensions.GetTopicForMessage(message));
            }

            Log.InfoWrite("WorkflowApplicationHost::PublishMessageWithCorrelation - publishing message {0} with CorrelationKey {1}, on topic {2}",
                message.GetType().Name, correlatesOnValue, topic);

            // Publish the correlated message directly to the Bus
            if (!String.IsNullOrWhiteSpace(topic))
                Bus.Publish(message.GetType(), message, topic);
            else
                Bus.Publish(message.GetType(), message);
        }

        public Task PublishMessageWithCorrelationAsync(Guid workflowInstanceId, object message, string topic = null)
        {
            if (message == null) throw new ArgumentNullException("message");

            string correlatesOnValue = null;
            if (!CorrelatesOnAttribute.TryGetCorrelatesOnValue(message, out correlatesOnValue))
            {
                // we are the Parent, so we set Correlation to ourselves
                CorrelatesOnAttribute.SetCorrelatesOnValue(message, workflowInstanceId, WorkflowDefinition.GetType().Name);

                correlatesOnValue = CorrelatesOnAttribute.GetCorrelatesOnValue(message);

                // if there is an explicit topic, then we use it, otherwise, inspect the message type for an OnTopic attribute
                topic = topic ?? AdvancedBusConsumerExtensions.GetTopicForMessage(message);
            }
            else
            {
                // We are responding to a Parent, so we will route using their correlation and
                // leave it on the message
                topic = topic ?? (CorrelatesOnAttribute.GetCorrelatesOnValue(message) ?? AdvancedBusConsumerExtensions.GetTopicForMessage(message));
            }

            Log.InfoWrite("WorkflowApplicationHost::PublishMessageWithCorrelationAsync - publishing message {0} with CorrelationKey {1}, on topic {2}",
                message.GetType().Name, correlatesOnValue, topic);

            // Publish the correlated message directly to the Bus
            if (!String.IsNullOrWhiteSpace(topic))
                return Bus.PublishAsync(message.GetType(), message, topic);
            
            return Bus.PublishAsync(message.GetType(), message);
        }


        #endregion
    }
}