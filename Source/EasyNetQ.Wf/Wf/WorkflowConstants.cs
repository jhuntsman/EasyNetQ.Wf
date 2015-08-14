using System;

namespace EasyNetQ.Wf
{
    class WorkflowConstants
    {
        public const string DispatcherConsumeAdvancedMethod = "ConsumeAdvanced";
        public const string MessageHeaderWorkflowRouteTopicKey = "EasyNetQ-Wf-Route-Topic";
        public const string MessageHeaderWorkflowInstanceIdKey = "EasyNetQ-Wf-InstanceId";
    }
}