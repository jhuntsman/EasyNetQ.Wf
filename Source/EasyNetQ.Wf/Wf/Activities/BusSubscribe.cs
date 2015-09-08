using System;
using System.Activities;

namespace EasyNetQ.Wf.Activities
{    
    public sealed class BusSubscribe<TMessage> : NativeActivity<TMessage>, IMessageReceiveActivity
        where TMessage:class
    {
        
        protected override bool CanInduceIdle
        {
            get { return true; }
        }
                                                
        protected override void Execute(NativeActivityContext context)
        {            
            var hostBehavior = context.GetExtension<IWorkflowApplicationHostBehavior>();
            var bookmarkName = hostBehavior.GetBookmarkNameFromMessageType(typeof (TMessage));

            context.CreateBookmark(bookmarkName, OnBookmarkResume);            
        }

        private void OnBookmarkResume(NativeActivityContext context, Bookmark bookmark, object value)
        {
            var messageBody = (TMessage) value;
            
            Result.Set(context, messageBody);
        }
    }
}
