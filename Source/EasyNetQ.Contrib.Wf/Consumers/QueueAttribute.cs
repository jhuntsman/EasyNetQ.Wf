using System;

namespace EasyNetQ.Contrib.Consumers
{
    [Serializable]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class QueueAttribute : Attribute
    {
        public string Name { get; set; }

        public QueueAttribute(string queue)
        {
            Name = queue;
        }
    }
}