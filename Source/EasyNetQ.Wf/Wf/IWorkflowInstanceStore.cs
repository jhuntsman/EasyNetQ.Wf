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

        IEnumerable<InstancePersistenceEvent> WaitForEvents(TimeSpan timeout);
    }

    public class WorkflowApplicationHostInstanceStore : IWorkflowApplicationHostInstanceStore
    {        
        private static object _syncLock = new object();

        private readonly InstanceStore _instanceStore;
        private InstanceHandle _ownerInstanceHandle;
        private TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);
        private XName _workflowHostTypeName;
        //public InstanceHandle OwnerInstanceHandle { get; }

        public InstanceStore Store { get { return _instanceStore; } }        

        public WorkflowApplicationHostInstanceStore(InstanceStore instanceStore)
        {
            if(instanceStore == null) throw new ArgumentNullException("instanceStore");

            _instanceStore = instanceStore;            
        }

        private InstanceView ExecuteCommandInternal(InstanceHandle handle, InstancePersistenceCommand command, TimeSpan timeout)
        {
            return Store.Execute(handle, command, timeout);
        }

        public virtual InstanceView ExecuteCommand(InstancePersistenceCommand command, TimeSpan timeout)
        {
            //TryRenewInstanceHandle(ref _ownerInstanceHandle);                        
            var commandHandle = Store.CreateInstanceHandle(Store.DefaultInstanceOwner);
            try
            {
                return ExecuteCommandInternal(commandHandle, command, timeout);
            }
            finally
            {
                commandHandle?.Free();
            }
        }

        public virtual void SetDefaultWorkflowInstanceOwner(XName workflowHostTypeName)
        {
            if (Store.DefaultInstanceOwner == null || (_ownerInstanceHandle != null && _ownerInstanceHandle.IsValid == false))
            {
                lock (_syncLock)
                {
                    if (Store.DefaultInstanceOwner == null ||
                        (_ownerInstanceHandle != null && _ownerInstanceHandle.IsValid == false))
                    {
                        _workflowHostTypeName = workflowHostTypeName;
                        TryRenewInstanceHandle(ref _ownerInstanceHandle);

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

        private void TryRenewDefaultWorkflowInstanceOwner()
        {
            if(_workflowHostTypeName != null)
                SetDefaultWorkflowInstanceOwner(_workflowHostTypeName);
        }

        public virtual IEnumerable<InstancePersistenceEvent> WaitForEvents(TimeSpan timeout)
        {
            TryRenewDefaultWorkflowInstanceOwner();
            return Store.WaitForEvents(_ownerInstanceHandle, timeout);
        }

        public virtual void ReleaseDefaultWorkflowInstanceOwner()
        {
            if (Store.DefaultInstanceOwner != null)
            {
                ExecuteCommand(new DeleteWorkflowOwnerCommand(), _defaultTimeout);
                Store.DefaultInstanceOwner = null;
                _workflowHostTypeName = null;
                _ownerInstanceHandle?.Free();
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