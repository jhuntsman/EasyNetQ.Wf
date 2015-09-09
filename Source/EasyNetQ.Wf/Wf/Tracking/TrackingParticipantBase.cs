using System;
using System.Activities.Tracking;

namespace EasyNetQ.Wf.Tracking
{
    /// <summary>
    /// Emit WF Tracking records to a Log
    /// </summary>
    public abstract class TrackingParticipantBase : TrackingParticipant
    {        
        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {            
            var workflowInstanceAbortedRecord = record as WorkflowInstanceAbortedRecord;
            if (workflowInstanceAbortedRecord != null)
            {
                OnWorkflowInstanceAborted(workflowInstanceAbortedRecord, timeout);
                return;
            }

            var workflowInstanceUnhandledExceptionRecord = record as WorkflowInstanceUnhandledExceptionRecord;
            if (workflowInstanceUnhandledExceptionRecord != null)
            {
                OnWorkflowInstanceUnhandledException(workflowInstanceUnhandledExceptionRecord, timeout);
                return;
            }
                        
            var workflowInstanceSuspendedRecord = record as WorkflowInstanceSuspendedRecord;
            if (workflowInstanceSuspendedRecord != null)
            {
                OnWorkflowInstanceSuspended(workflowInstanceSuspendedRecord, timeout);
                return;
            }

            var workflowInstanceTerminatedRecord = record as WorkflowInstanceTerminatedRecord;
            if (workflowInstanceTerminatedRecord != null)
            {
                OnWorkflowInstanceTerminated(workflowInstanceTerminatedRecord, timeout);
                return;
            }

            var workflowInstanceRecord = record as WorkflowInstanceRecord;
            if (workflowInstanceRecord != null)
            {
                OnWorkflowInstance(workflowInstanceRecord, timeout);
                return;
            }

            var activityStateRecord = record as ActivityStateRecord;
            if (activityStateRecord != null)
            {
                OnActivityState(activityStateRecord, timeout);
                return;
            }

            var activityScheduledRecord = record as ActivityScheduledRecord;
            if (activityScheduledRecord != null)
            {
                OnActivityScheduled(activityScheduledRecord, timeout);
                return;
            }

            var faultPropogationRecord = record as FaultPropagationRecord;
            if (faultPropogationRecord != null)
            {
                OnFaultPropogation(faultPropogationRecord, timeout);
                return;
            }

            var cancelRequestedRecord = record as CancelRequestedRecord;
            if (cancelRequestedRecord != null)
            {
                OnCancelRequested(cancelRequestedRecord, timeout);
                return;
            }

            var bookmarkResumptionRecord = record as BookmarkResumptionRecord;
            if (bookmarkResumptionRecord != null)
            {
                OnBookmarkResumption(bookmarkResumptionRecord, timeout);
                return;
            }

            var customTrackingRecord = record as CustomTrackingRecord;
            if (customTrackingRecord != null)
            {
                OnCustomTracking(customTrackingRecord, timeout);
                return;
            }
        }

        protected virtual void OnCustomTracking(CustomTrackingRecord record, TimeSpan timeout) { }

        protected virtual void OnBookmarkResumption(BookmarkResumptionRecord record, TimeSpan timeout) { }

        protected virtual void OnCancelRequested(CancelRequestedRecord record, TimeSpan timeout) { }

        protected virtual void OnFaultPropogation(FaultPropagationRecord record, TimeSpan timeout) { }

        protected virtual void OnActivityScheduled(ActivityScheduledRecord record, TimeSpan timeout) { }

        protected virtual void OnActivityState(ActivityStateRecord record, TimeSpan timeout) { }

        protected virtual void OnWorkflowInstanceTerminated(WorkflowInstanceTerminatedRecord record, TimeSpan timeout) { }

        protected virtual void OnWorkflowInstanceSuspended(WorkflowInstanceSuspendedRecord record, TimeSpan timeout) { }

        protected virtual void OnWorkflowInstanceUnhandledException(WorkflowInstanceUnhandledExceptionRecord record, TimeSpan timeout) { }

        protected virtual void OnWorkflowInstanceAborted(WorkflowInstanceAbortedRecord record, TimeSpan timeout) { }

        protected virtual void OnWorkflowInstance(WorkflowInstanceRecord record, TimeSpan timeout) { }
    }
}