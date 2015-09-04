using System;
using System.Activities.DurableInstancing;
using System.Collections.Generic;
using System.Runtime.DurableInstancing;
using System.Xml.Linq;

namespace EasyNetQ.Wf
{
    public interface IWorkflowApplicationHostInstanceStore : IDisposable
    {        
        InstanceStore Store { get; }

        void SetDefaultWorkflowInstanceOwner(XName workflowHostTypeName);
        void ReleaseDefaultWorkflowInstanceOwner();

        IEnumerable<InstancePersistenceEvent> WaitForEvents(TimeSpan timeout);
    }

    public class DefaultWorkflowApplicationHostInstanceStore : IWorkflowApplicationHostInstanceStore
    {        
        private object _syncLock = new object();

        private readonly InstanceStore _instanceStore;
        private InstanceHandle _ownerInstanceHandle;
        private TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);
        private XName _workflowHostTypeName;
        
        public InstanceStore Store { get { return _instanceStore; } }        

        public DefaultWorkflowApplicationHostInstanceStore(InstanceStore instanceStore)
        {
            if(instanceStore == null) throw new ArgumentNullException("instanceStore");

            _instanceStore = instanceStore;            
        }
        
        public virtual TimeSpan DefaultTimeout
        {
            get { return _defaultTimeout; }
        }

        public virtual InstanceView ExecuteCommand(InstancePersistenceCommand command, TimeSpan timeout)
        {
            TryRenewInstanceHandle(ref _ownerInstanceHandle);                                                
            return ExecuteCommandInternal(_ownerInstanceHandle, command, timeout);                        
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
                        // TODO: support workflow versioning
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
                        Store.DefaultInstanceOwner = ExecuteCommand(createOwnerCmd, DefaultTimeout).InstanceOwner;
                    }

                }
            }
        }
        
        public virtual IEnumerable<InstancePersistenceEvent> WaitForEvents(TimeSpan timeout)
        {
            TryRenewDefaultWorkflowInstanceOwner();
            try
            {
                return Store.WaitForEvents(_ownerInstanceHandle, timeout);
            }
            catch (TimeoutException) { }
            return null;
        }

        public virtual void ReleaseDefaultWorkflowInstanceOwner()
        {
            if (Store.DefaultInstanceOwner != null)
            {
                ExecuteCommand(new DeleteWorkflowOwnerCommand(), DefaultTimeout);
                Store.DefaultInstanceOwner = null;
                _workflowHostTypeName = null;
                _ownerInstanceHandle?.Free();
            }
        }
                
        public void Dispose()
        {
            ReleaseDefaultWorkflowInstanceOwner();
            _ownerInstanceHandle?.Free();            
        }

        #region Helper methods
        private InstanceView ExecuteCommandInternal(InstanceHandle handle, InstancePersistenceCommand command, TimeSpan timeout)
        {
            return Store.Execute(handle, command, timeout);
        }

        private void TryRenewDefaultWorkflowInstanceOwner()
        {
            if (_workflowHostTypeName != null)
                SetDefaultWorkflowInstanceOwner(_workflowHostTypeName);
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
        #endregion
    }
}