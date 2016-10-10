using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.EventStore.Cqrs.Core
{
    public interface ISnapShotable
    {
        object Momento { get; }
        void ApplyMomento(object momento, long version);
    }

    public interface ISnapShotable<T> : ISnapShotable
    {
        bool ApplySnapshotMomento(T snapshot);
    }
}
