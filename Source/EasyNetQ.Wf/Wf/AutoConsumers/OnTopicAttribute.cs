using System;
using System.Linq;
using System.Reflection;

namespace EasyNetQ.Wf.AutoConsumers
{
    [Serializable]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class OnTopicAttribute : Attribute
    {
        public string Name { get; set; }

        public OnTopicAttribute(string topic)
        {
            Name = topic;
        }        
    }
}