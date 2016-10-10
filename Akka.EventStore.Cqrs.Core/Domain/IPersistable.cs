namespace Akka.EventStore.Cqrs.Core.Domain
{
    public interface IPersistable
    {
        string PersistenceId { get; }
    }

    
}
