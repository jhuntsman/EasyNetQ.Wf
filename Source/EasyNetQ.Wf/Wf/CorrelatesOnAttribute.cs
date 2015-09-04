using System;
using System.Activities;
using System.Linq;
using System.Reflection;

namespace EasyNetQ.Wf.AutoConsumers
{
    [Serializable]
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class CorrelatesOnAttribute : Attribute
    {
        internal static PropertyInfo FindCorrelatesOnProperty(object message, BindingFlags bindingFlags)
        {
            if (message == null) throw new ArgumentNullException("message");

            // inspect for known attribute
            foreach (var propertyInfo in message.GetType().GetProperties(bindingFlags))
            {
                var correlationAttribute = propertyInfo.GetCustomAttributes(typeof(CorrelatesOnAttribute), true).Cast<CorrelatesOnAttribute>().SingleOrDefault();
                if (correlationAttribute != null)
                {
                    return propertyInfo;
                }
            }

            throw new WorkflowHostException(String.Format("Correlation Id expected but not found on type {0}. Use [CorrelatesOn] attribute to specify a Correlation Id", message.GetType().FullName));
        }

        internal static string GetCorrelatesOnValue(object message)
        {
            return (FindCorrelatesOnProperty(message, BindingFlags.Public | BindingFlags.Instance).GetValue(message
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
                value = (FindCorrelatesOnProperty(message, BindingFlags.Public | BindingFlags.Instance).GetValue(message
#if NET4
                , null
#endif
                ) as string);

                if (String.IsNullOrWhiteSpace(value))
                {
                    value = null;
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        internal static string GetMessageCorrelatesOnTopic(object message)
        {
            string correlatesOnValue = null;
            if (CorrelatesOnAttribute.TryGetCorrelatesOnValue(message, out correlatesOnValue))
            {
                return correlatesOnValue.Split(new[] { '|' })[1];
            }
            return correlatesOnValue;
        }

        internal static void SetCorrelatesOnValue(object message, Guid workflowInstanceId, Activity workflowDefinition)
        {
            FindCorrelatesOnProperty(message, BindingFlags.Public | BindingFlags.Instance).SetValue(message, String.Format("{0}|{1}", workflowInstanceId, workflowDefinition.GetType().Name)
#if NET4
                , null
#endif
                );
        }

    }
}