using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Activities;

namespace EasyNetQ.Contrib.Wf.Activities
{    
    public sealed class BusSubscribe<TMessage> : NativeActivity<TMessage>, IReceiveMessageActivity
        where TMessage:class
    {
        
        protected override bool CanInduceIdle
        {
            get { return true; }
        }
                                                
        protected override void Execute(NativeActivityContext context)
        {
            IBus bus = context.GetExtension<IBus>();
            var workflowStrategy = bus.Advanced.Container.Resolve<IWorkflowConsumerHostStrategies>();

            var bookmarkName = workflowStrategy.BookmarkMessageNamingStrategy(typeof (TMessage));            
            context.CreateBookmark(bookmarkName, OnBookmarkResume);            
        }

        private void OnBookmarkResume(NativeActivityContext context, Bookmark bookmark, object value)
        {
            var messageBody = (TMessage) value;
            
            Result.Set(context, messageBody);
        }
    }
}
