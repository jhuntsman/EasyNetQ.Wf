using System;
using System.Threading.Tasks;

namespace EasyNetQ.Wf.AutoConsumers
{
    public interface IRespond<in TRequest, out TResponse> 
        where TRequest : class 
        where TResponse : class
    {
        TResponse Respond(TRequest request);
    }
    
    public interface IRespondAsync<in TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        Task<TResponse> Respond(TRequest request);
    }    
}