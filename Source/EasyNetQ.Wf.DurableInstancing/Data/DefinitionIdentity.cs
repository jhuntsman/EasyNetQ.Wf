using System;
using Telerik.OpenAccess.Metadata;
using Telerik.OpenAccess.Metadata.Fluent;

namespace EasyNetQ.Wf.DurableInstancing.Data
{
    public class DefinitionIdentity
    {
        public long SurrogateIdentityId { get; set; }
        public Guid DefinitionIdentityHash { get; set; }
        public Guid DefinitionIdentityAnyRevisionHash { get; set; }
        public string Name { get; set; }
        public string Package { get; set; }
        public long? Build { get; set; }
        public long? Major { get; set; }
        public long? Minor { get; set; }
        public long? Revision { get; set; }

        public static MappingConfiguration GetMapping()
        {
            var mapping = new MappingConfiguration<DefinitionIdentity>();
            mapping.MapType().ToTable("DefinitionIdentities");
            mapping.HasProperty(p => p.SurrogateIdentityId).IsIdentity(KeyGenerator.Autoinc);
            mapping.HasProperty(p => p.Name).WithInfiniteLength().IsUnicode();
            mapping.HasProperty(p => p.Package).WithInfiniteLength().IsUnicode();
            return mapping;
        }
    }

    public class LockOwner
    {
        public Guid Id { get; set; }
        public long SurrogateLockOwnerId { get; set; }
        public DateTime LockExpiration { get; set; }
        public Guid? WorkflowHostType { get; set; }
        public string MachineName { get; set; }
        public bool EnqueueCommand { get; set; }
        public bool DeletesInstanceOnCompletion { get; set; }
        public byte[] PrimitiveLockOwnerData { get; set; }
        public byte[] ComplexLockOwnerData { get; set; }
        public byte[] WriteOnlyPrimitiveLockOwnerData { get; set; }
        public byte[] WriteOnlyComplexLockOwnerData { get; set; }
        public short? EncodingOption { get; set; }
        public short WorkflowIdentityFilter { get; set; }

        public static MappingConfiguration GetMapping()
        {
            var mapping = new MappingConfiguration<LockOwner>();
            mapping.MapType().ToTable("LockOwners");
            mapping.HasProperty(p => p.SurrogateLockOwnerId).IsIdentity(KeyGenerator.Autoinc);
            mapping.HasProperty(p => p.PrimitiveLockOwnerData).WithInfiniteLength().IsNullable();
            mapping.HasProperty(p => p.ComplexLockOwnerData).WithInfiniteLength().IsNullable();
            mapping.HasProperty(p => p.WriteOnlyPrimitiveLockOwnerData).WithInfiniteLength().IsNullable();
            mapping.HasProperty(p => p.WriteOnlyComplexLockOwnerData).WithInfiniteLength().IsNullable();
            return mapping;
        }
    }
}