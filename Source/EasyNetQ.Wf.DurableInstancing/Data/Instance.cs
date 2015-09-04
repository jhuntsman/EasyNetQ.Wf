using System;
using Telerik.OpenAccess.Metadata;
using Telerik.OpenAccess.Metadata.Fluent;

namespace EasyNetQ.Wf.DurableInstancing.Data
{
    public class Instance
    {
        public Guid Id { get; set; }
        public long SurrogateInstanceId { get; set; }
        public long? SurrogateLockOwnerId { get; set; }
        public byte[] PrimitiveDataProperties { get; set; }
        public byte[] ComplexDataProperties { get; set; }
        public byte[] WriteOnlyPrimitiveDataProperties { get; set; }
        public byte[] WriteOnlyComplexDataProperties { get; set; }

        public byte[] MetadataProperties { get; set; }
        public short? DataEncodingOption { get; set; }
        public short? MetadataEncodingOption { get; set; }
        public long Version { get; set; }
        public DateTime? PendingTimer { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime? LastUpdated { get; set; }
        public Guid WorkflowHostType { get; set; }
        public long? ServiceDeploymentId { get; set; }
        public string SuspensionExceptionName { get; set; }
        public string SuspensionReason { get; set; }
        public string BlockingBookmarks { get; set; }
        public string LastMachineRunOn { get; set; }
        public string ExecutionStatus { get; set; }
        public bool? IsInitialized { get; set; }
        public bool? IsSuspended { get; set; }
        public bool? IsReadyToRun { get; set; }
        public bool? IsCompleted { get; set; }
        public long SurrogateIdentityId { get; set; }

        public static MappingConfiguration GetMapping()
        {
            var mapping = new MappingConfiguration<Instance>();
            mapping.MapType().ToTable("Instances");
            mapping.HasProperty(p => p.SurrogateInstanceId).IsIdentity(KeyGenerator.Autoinc);
            mapping.HasProperty(p => p.PrimitiveDataProperties).WithInfiniteLength().IsNullable();
            mapping.HasProperty(p => p.ComplexDataProperties).WithInfiniteLength().IsNullable();
            mapping.HasProperty(p => p.WriteOnlyPrimitiveDataProperties).WithInfiniteLength().IsNullable();
            mapping.HasProperty(p => p.WriteOnlyComplexDataProperties).WithInfiniteLength().IsNullable();
            mapping.HasProperty(p => p.MetadataProperties).WithInfiniteLength().IsNullable();
            mapping.HasProperty(p => p.SuspensionExceptionName).WithVariableLength(450).IsNullable().IsUnicode();
            mapping.HasProperty(p => p.SuspensionReason).WithInfiniteLength().IsUnicode().IsNullable();
            mapping.HasProperty(p => p.BlockingBookmarks).WithInfiniteLength().IsUnicode().IsNullable();
            mapping.HasProperty(p => p.LastMachineRunOn).WithVariableLength(450).IsUnicode().IsNullable();
            return mapping;
        }
    }
}
