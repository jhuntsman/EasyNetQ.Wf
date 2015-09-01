using System;
using System.Runtime.DurableInstancing;

namespace EasyNetQ.Wf
{
    public interface IWorkflowInstanceStore : IDisposable
    {
        InstanceHandle OwnerInstanceHandle { get; }

        InstanceStore Store { get; }

        
    }
}