using System;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace EasyNetQ.Wf.Tracking
{
    public class LoggerTrackingParticipant : TrackingParticipantBase
    {
        private IEasyNetQLogger Log;

        public LoggerTrackingParticipant(IEasyNetQLogger logger)
        {
            if(logger == null)
                throw new ArgumentNullException("logger");

            Log = logger;
        }

        protected override void OnWorkflowInstance(WorkflowInstanceRecord record, TimeSpan timeout)
        {
            Log.InfoWrite("Tracking::WorkflowInstance - Id:{0}, InstanceId:{1}, EventTime:{2}, Record#:{3}, Level:{4}, State:{5}", record.ActivityDefinitionId, record.InstanceId, record.EventTime, record.RecordNumber, record.Level, record.State);
        }

        protected override void OnWorkflowInstanceAborted(WorkflowInstanceAbortedRecord record, TimeSpan timeout)
        {
            Log.InfoWrite("Tracking::WorkflowInstanceAborted - Id:{0}, InstanceId:{1}, EventTime:{2}, Record#:{3}, Level:{4}, State:{5}, Reason:{6}", record.ActivityDefinitionId, record.InstanceId, record.EventTime, record.RecordNumber, record.Level, record.State, record.Reason);
        }

        protected override void OnWorkflowInstanceTerminated(WorkflowInstanceTerminatedRecord record, TimeSpan timeout)
        {
            Log.InfoWrite("Tracking::WorkflowInstanceTerminated - Id:{0}, InstanceId:{1}, EventTime:{2}, Record#:{3}, Level:{4}, State:{5}, Reason:{6}", record.ActivityDefinitionId, record.InstanceId, record.EventTime, record.RecordNumber, record.Level, record.State, record.Reason);
        }

        protected override void OnWorkflowInstanceSuspended(WorkflowInstanceSuspendedRecord record, TimeSpan timeout)
        {
            Log.InfoWrite("Tracking::WorkflowInstanceSuspended - Id:{0}, InstanceId:{1}, EventTime:{2}, Record#:{3}, Level:{4}, State:{5}, Reason:{6}", record.ActivityDefinitionId, record.InstanceId, record.EventTime, record.RecordNumber, record.Level, record.State, record.Reason);
        }

        protected override void OnFaultPropogation(FaultPropagationRecord record, TimeSpan timeout)
        {
            Log.InfoWrite("Tracking::FaultPropogation - Source:{0} Instance Id:{1}, EventTime:{2}, Record#:{3}, Level:{4}, Exception:{5}", record.FaultSource.Name, record.InstanceId, record.EventTime, record.RecordNumber, record.Level, record.Fault.ToString());
        }

        protected override void OnActivityState(ActivityStateRecord record, TimeSpan timeout)
        {
            Log.InfoWrite("Tracking::ActivityState - InstanceId:{0}, Activity:{1}, EventTime:{2}, Record#:{3}, Level:{4}, State:{5}", record.InstanceId, record.Activity.Name, record.EventTime, record.RecordNumber, record.Level, record.State);
        }

        protected override void OnBookmarkResumption(BookmarkResumptionRecord record, TimeSpan timeout)
        {
            Log.InfoWrite("Tracking::BookmarkResumption - InstanceId:{0}, Bookmark Name:{1}, EventTime:{2}, Record#:{3}, Level:{4}, Payload Type:{5}", record.InstanceId, record.BookmarkName, record.EventTime, record.RecordNumber, record.Level, record.Payload.GetType().FullName);
        }
    }
}
