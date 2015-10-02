using System;
using System.Activities;
using System.Activities.DurableInstancing;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.DurableInstancing;
using System.Xml.Linq;

namespace EasyNetQ.Wf
{
    public interface IWorkflowApplicationHostInstanceStore : IDisposable
    {        
        InstanceStore Store { get; }

        void SetDefaultWorkflowInstanceOwner(XName workflowHostTypeName, IEnumerable<WorkflowIdentity> workflowIdentities);

        bool TryRenewDefaultWorkflowInstanceOwner();

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
        private IEnumerable<WorkflowIdentity> _workflowIdentities;
        
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

        public virtual void SetDefaultWorkflowInstanceOwner(XName workflowHostTypeName, IEnumerable<WorkflowIdentity> workflowIdentities)
        {
            if (Store.DefaultInstanceOwner == null || (_ownerInstanceHandle == null || _ownerInstanceHandle.IsValid == false))
            {
                lock (_syncLock)
                {
                    if (Store.DefaultInstanceOwner == null ||
                        (_ownerInstanceHandle == null || _ownerInstanceHandle.IsValid == false))
                    {
                        _workflowHostTypeName = workflowHostTypeName;
                        _workflowIdentities = workflowIdentities;
                        TryRenewInstanceHandle(ref _ownerInstanceHandle);

                        InstancePersistenceCommand createOwnerCmd = null;                        
#if NET4
                        createOwnerCmd = new CreateWorkflowOwnerCommand();
                        ((CreateWorkflowOwnerCommand)createOwnerCmd).InstanceOwnerMetadata.Add(WorkflowNamespaces.WorkflowHostTypePropertyName, new InstanceValue(workflowHostTypeName));
#else
                        if (workflowIdentities == null)
                        {
                            createOwnerCmd = new CreateWorkflowOwnerCommand();
                            ((CreateWorkflowOwnerCommand) createOwnerCmd).InstanceOwnerMetadata.Add(WorkflowNamespaces.WorkflowHostTypePropertyName, new InstanceValue(workflowHostTypeName));
                        }
                        else
                        {
                            // support workflow versioning
                            createOwnerCmd = new CreateWorkflowOwnerWithIdentityCommand();
                            ((CreateWorkflowOwnerWithIdentityCommand)createOwnerCmd).InstanceOwnerMetadata.Add(WorkflowNamespaces.WorkflowHostTypePropertyName, new InstanceValue(workflowHostTypeName));
                            ((CreateWorkflowOwnerWithIdentityCommand)createOwnerCmd).InstanceOwnerMetadata.Add(WorkflowNamespaces.DefinitionIdentityFilterName, new InstanceValue(WorkflowIdentityFilter.Any));
                            ((CreateWorkflowOwnerWithIdentityCommand)createOwnerCmd).InstanceOwnerMetadata.Add(WorkflowNamespaces.DefinitionIdentitiesName, new InstanceValue(workflowIdentities.ToList()));
                        }                                                                   
#endif
                        Store.DefaultInstanceOwner = ExecuteCommand(createOwnerCmd, DefaultTimeout).InstanceOwner;
                    }

                }
            }
        }
        
        public virtual IEnumerable<InstancePersistenceEvent> WaitForEvents(TimeSpan timeout)
        {
            if (!TryRenewDefaultWorkflowInstanceOwner()) return null;
            try
            {
                return Store.WaitForEvents(_ownerInstanceHandle, timeout);
            }
            catch (OperationCanceledException)
            {
                // instance handle most likely has been invalidated due to lost connection with MS SQL Server
            }
            catch (TimeoutException)
            {
                // no waiting instances within the timeout period
            }
            return null;
        }

        public virtual void ReleaseDefaultWorkflowInstanceOwner()
        {
            if (Store.DefaultInstanceOwner == null) return;

            try
            {
                ExecuteCommand(new DeleteWorkflowOwnerCommand(), DefaultTimeout);                
            }
            catch (Exception)
            {
                // instance handle could already be invalid 
            }
            finally
            {
                Store.DefaultInstanceOwner = null;                
                _ownerInstanceHandle?.Free();
                _ownerInstanceHandle = null;
            }
        }
                
        public void Dispose()
        {
            ReleaseDefaultWorkflowInstanceOwner();                     
        }

#region Helper methods
        private InstanceView ExecuteCommandInternal(InstanceHandle handle, InstancePersistenceCommand command, TimeSpan timeout)
        {
            return Store.Execute(handle, command, timeout);
        }

        public bool TryRenewDefaultWorkflowInstanceOwner()
        {            
            try
            {
                SetDefaultWorkflowInstanceOwner(_workflowHostTypeName, _workflowIdentities);
                return true;
            }
            catch (InvalidOperationException)
            {
                ReleaseDefaultWorkflowInstanceOwner();
            }
            return false;
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