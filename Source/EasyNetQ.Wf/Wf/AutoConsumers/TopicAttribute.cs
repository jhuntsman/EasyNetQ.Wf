using System;

namespace EasyNetQ.Wf.AutoConsumers
{
    [Serializable]
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class TopicAttribute : Attribute
    {
        public string Name { get; set; }

        public TopicAttribute(string topic)
        {
            Name = topic;
        }
    }
}