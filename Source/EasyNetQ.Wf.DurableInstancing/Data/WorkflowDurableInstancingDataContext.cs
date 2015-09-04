using System;
using System.Linq;
using Telerik.OpenAccess;
using Telerik.OpenAccess.Metadata;

namespace EasyNetQ.Wf.DurableInstancing.Data
{
    public interface IWorkflowDurableInstancingUnitOfWork : IUnitOfWork
    {
        IQueryable<DefinitionIdentity> DefinitionIdentities { get; }
        IQueryable<Instance> Instances { get; }
        IQueryable<LockOwner> LockOwners { get; }
    }

    public class WorkflowDurableInstancingDataContext : OpenAccessContext, IWorkflowDurableInstancingUnitOfWork
    {
        private static readonly string DefaultConnectionStringName = "WorkflowDurableInstancingConnection";
        private static readonly BackendConfiguration DefaultBackend = GetBackendConfiguration();
        private static readonly MetadataSource DefaultMetadataSource = new WorkflowDurableInstancingFluentMetadataSource();

        public WorkflowDurableInstancingDataContext() : this(DefaultConnectionStringName, DefaultBackend, DefaultMetadataSource) { }

        public WorkflowDurableInstancingDataContext(string connectionString) : this(connectionString, DefaultBackend, DefaultMetadataSource) { }

        public WorkflowDurableInstancingDataContext(string connectionString, BackendConfiguration backend, MetadataSource metadataSource)
            : base(connectionString, backend, metadataSource)
        {
            var schemaHandler = GetSchemaHandler();
            string script = null;
            if (schemaHandler.DatabaseExists())
            {
                script = schemaHandler.CreateUpdateDDLScript(null);
            }
            else
            {
                schemaHandler.CreateDatabase();
                script = schemaHandler.CreateDDLScript();
            }
            if (!String.IsNullOrEmpty(script))
            {
                schemaHandler.ExecuteDDLScript(script);
            }
        }
        
        public static BackendConfiguration GetBackendConfiguration()
        {
            var backendConfig = new BackendConfiguration()
            {
                Backend = "MsSql",
                ProviderName = "System.Data.SqlClient",                
            };
            return backendConfig;
        }

        public IQueryable<DefinitionIdentity> DefinitionIdentities { get { return this.GetAll<DefinitionIdentity>(); } }
        public IQueryable<Instance> Instances { get { return this.GetAll<Instance>(); } }
        public IQueryable<LockOwner> LockOwners { get { return this.GetAll<LockOwner>(); } }
    }
}