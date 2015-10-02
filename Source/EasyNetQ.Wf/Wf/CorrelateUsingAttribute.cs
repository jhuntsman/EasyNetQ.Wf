using System;
using System.Activities;
using System.Linq;
using System.Reflection;

namespace EasyNetQ.Wf.AutoConsumers
{    
    [Serializable]
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class CorrelateUsingAttribute : Attribute
    {
        private static PropertyInfo FindCorrelatesOnProperty(object message, BindingFlags bindingFlags, bool required=true)
        {
            if (message == null) throw new ArgumentNullException("message");

            // inspect for known attribute
            foreach (var propertyInfo in message.GetType().GetProperties(bindingFlags))
            {
                var correlationAttribute = propertyInfo.GetCustomAttributes(typeof(CorrelateUsingAttribute), true).Cast<CorrelateUsingAttribute>().SingleOrDefault();
                if (correlationAttribute != null)
                {
                    return propertyInfo;
                }
            }
            if(required)
                throw new WorkflowHostException(String.Format("Correlation Key expected but not found on type {0}. Use [CorrelateUsing] attribute to specify a Correlation Key", message.GetType().FullName));
            return null;
        }

        internal static string GetCorrelatesOnValue(object message)
        {
            var correlatesOnProperty = FindCorrelatesOnProperty(message, BindingFlags.Public | BindingFlags.Instance, required:false);
            if (correlatesOnProperty == null)
                return null;

            return (correlatesOnProperty.GetValue(message
#if NET4
                , null                
#endif
                ) as string);
        }

        internal static bool TryGetCorrelatesOnValue(object message, out string value)
        {
            value = null;
            try
            {
                value = GetCorrelatesOnValue(message);
                if (String.IsNullOrWhiteSpace(value))
                {
                    value = null;
                    return false;
                }
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
            return true;
        }
        internal static string GetMessageCorrelatesOnTopic(object message)
        {
            string correlatesOnValue = null;
            if (TryGetCorrelatesOnValue(message, out correlatesOnValue))
            {
                return correlatesOnValue.Split(new[] { '|' })[1];
            }
            return correlatesOnValue;
        }

        internal static void SetCorrelatesOnValue(object message, Guid workflowInstanceId, string workflowRouteTopic)
        {
            FindCorrelatesOnProperty(message, BindingFlags.Public | BindingFlags.Instance, required:true).SetValue(message, String.Format("{0}|{1}", workflowInstanceId, workflowRouteTopic)
#if NET4
                , null
#endif
                );
        }

    }
}