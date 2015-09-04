using System;
using System.Xml.Linq;

namespace EasyNetQ.Wf
{
    public static class WorkflowNamespaces
    {
        public static readonly XNamespace Workflow45Namespace = XNamespace.Get("urn:schemas-microsoft-com:System.Activities/4.5/properties");
        public static readonly XNamespace Workflow40Namespace = XNamespace.Get("urn:schemas-microsoft-com:System.Activities/4.0/properties");
        public static readonly XName WorkflowHostTypePropertyName = Workflow40Namespace.GetName("WorkflowHostType");
        public static readonly XName DefinitionIdentityFilterName = Workflow45Namespace.GetName("DefinitionIdentityFilter");
        public static readonly XName WorkflowApplicationName = Workflow45Namespace.GetName("WorkflowApplication");
        public static readonly XName DefinitionIdentitiesName = Workflow45Namespace.GetName("DefinitionIdentities");
    }
}