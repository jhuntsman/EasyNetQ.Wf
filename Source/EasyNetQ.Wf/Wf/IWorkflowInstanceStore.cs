using System;
using System.Activities.DurableInstancing;
using System.Collections.Generic;
using System.Runtime.DurableInstancing;
using System.Xml.Linq;

namespace EasyNetQ.Wf
{
    public interface IWorkflowApplicationHostInstanceStore : IDisposable
    {
        //InstanceHandle OwnerInstanceHandle { get; }

        InstanceStore Store { get; }

        void SetDefaultWorkflowInstanceOwner(XName workflowHostTypeName);
        void ReleaseDefaultWorkflowInstanceOwner();

        IEnumerable<InstancePersistenceEvent> WaitForEvents(ref InstanceHandle instanceHandle, TimeSpan timeout);
    }

    public class WorkflowApplicationHostInstanceStore : IWorkflowApplicationHostInstanceStore
    {        
        private static object _syncLock = new object();

        private readonly InstanceStore _instanceStore;
        //private InstanceHandle _ownerInstanceHandle;
        private TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);
        
        //public InstanceHandle OwnerInstanceHandle { get; }

        public InstanceStore Store { get { return _instanceStore; } }        

        public WorkflowApplicationHostInstanceStore(InstanceStore instanceStore)
        {
            if(instanceStore == null) throw new ArgumentNullException("instanceStore");

            _instanceStore = instanceStore;            
        }

        public virtual InstanceView ExecuteCommand(InstancePersistenceCommand command, TimeSpan timeout)
        {
            //TryRenewInstanceHandle(ref _ownerInstanceHandle);                        
            var commandHandle = Store.CreateInstanceHandle(Store.DefaultInstanceOwner);
            try
            {
                return Store.Execute(commandHandle, command, timeout);
            }
            finally
            {
                commandHandle?.Free();
            }
        }

        public virtual void SetDefaultWorkflowInstanceOwner(XName workflowHostTypeName)
        {
            if (Store.DefaultInstanceOwner == null)
            {
                lock (_syncLock)
                {
                    if (Store.DefaultInstanceOwner == null)
                    {
                        //TryRenewInstanceHandle(ref _ownerInstanceHandle);
                        if (Store.DefaultInstanceOwner == null)
                        {
                            var createOwnerCmd = new CreateWorkflowOwnerCommand();
                            createOwnerCmd.InstanceOwnerMetadata.Add(WorkflowNamespaces.WorkflowHostTypePropertyName, new InstanceValue(workflowHostTypeName));
                            /*
                            {
                                InstanceOwnerMetadata =
                                {
                                    {WorkflowHostTypePropertyName, new InstanceValue(GetWorkflowHostTypeName(workflowDefinition))},
                                    //{DefinitionIdentityFilterName, new InstanceValue(WorkflowIdentityFilter.Any)},
                                    //{DefinitionIdentitiesName, new InstanceValue(workflowApplication.DefinitionIdentity)},
                                }
                            };
                            */
                            Store.DefaultInstanceOwner = ExecuteCommand(createOwnerCmd, _defaultTimeout).InstanceOwner;
                        }
                    }
                }
            }
        }

        public virtual IEnumerable<InstancePersistenceEvent> WaitForEvents(ref InstanceHandle instanceHandle, TimeSpan timeout)
        {
            TryRenewInstanceHandle(ref instanceHandle);
            return Store.WaitForEvents(instanceHandle, timeout);
        }

        public virtual void ReleaseDefaultWorkflowInstanceOwner()
        {
            if (Store.DefaultInstanceOwner != null)
            {
                ExecuteCommand(new DeleteWorkflowOwnerCommand(), _defaultTimeout);
                Store.DefaultInstanceOwner = null;
            }
        }
                
        public void Dispose()
        {
            ReleaseDefaultWorkflowInstanceOwner();
            /*
            if (_ownerInstanceHandle != null)
            {
                ReleaseDefaultWorkflowInstanceOwner();  

                _ownerInstanceHandle.Free();
                _ownerInstanceHandle = null;
            }
            */
        }

        private void TryRenewInstanceHandle(ref InstanceHandle instanceHandle)
        {
            if (instanceHandle == null || !instanceHandle.IsValid)
            {                
                // free the old instance handle                
                instanceHandle?.Free();
                
                instanceHandle = Store.CreateInstanceHandle();
            }
        }
    }
}