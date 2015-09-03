using System;
using System.Threading.Tasks;

namespace EasyNetQ.Wf
{
    public interface IWorkflowApplicationHostBehavior
    {
        T Resolve<T>() where T : class;

        string GetBookmarkNameFromMessageType(Type messageType);

        Task PublishMessageWithCorrelationAsync(Guid workflowInstanceId, object message, string topic = null);
        void PublishMessageWithCorrelation(Guid workflowInstanceId, object message, string topic = null);

        void PublishMessage(object message, string topic = null);

        Task PublishMessageAsync(object message, string topic = null);
    }
}