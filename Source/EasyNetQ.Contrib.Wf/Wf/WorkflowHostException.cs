using System;
using System.Runtime.Serialization;

namespace EasyNetQ.Contrib.Wf
{
    [Serializable]
    public class WorkflowHostException : ApplicationException
    {
        public WorkflowHostException(string message) : base(message) { }
        public WorkflowHostException(string message, Exception innerException) : base(message, innerException) { }
        public WorkflowHostException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}