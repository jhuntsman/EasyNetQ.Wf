using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telerik.OpenAccess.Metadata.Fluent;

namespace EasyNetQ.Wf.DurableInstancing.Data
{
    public class WorkflowDurableInstancingFluentMetadataSource : FluentMetadataSource
    {
        protected override IList<MappingConfiguration> PrepareMapping()
        {
            var configurations = new List<MappingConfiguration>();

            var instanceConfig = new MappingConfiguration<Instance>();            
            configurations.Add(instanceConfig);

            var definitionIdentityConfig = new MappingConfiguration<DefinitionIdentity>();            
            configurations.Add(definitionIdentityConfig);

            var lockOwnerConfig = new MappingConfiguration<LockOwner>();            
            configurations.Add(lockOwnerConfig);

            return configurations;
        }
    }
}
